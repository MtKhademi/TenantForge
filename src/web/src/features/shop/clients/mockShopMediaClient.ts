import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  MAX_GALLERY_IMAGES,
  ShopMediaError,
  shopProductWithGallerySchema,
  type ProductGallery,
  type ShopProductWithGallery,
} from '../contracts/mediaContract'
import {
  delay,
  encodeImageAsWebP,
  readImageFile,
  type ShopMediaClient,
} from './ShopMediaClient'

/**
 * S32 product media — deterministic mock implementation of `ShopMediaClient`
 * (F044). F054 swaps in `httpShopMediaClient` behind the same interface; this
 * file must stay out of the production bundle's rendering path (its scenario
 * names never reach product text or the DOM).
 *
 * Behavior mirrors B036: canonical 13-character TSID strings, ordered images,
 * a monotonically increasing `galleryVersion` (stale version → 409), the
 * eight-image cap, and the exact error statuses from
 * `docs/design/shop/http-contracts.md` §B036 (400/403/404/409/410/429).
 * Latency is simulated through the shared abort-aware `delay` so every call
 * can be cancelled and a cancelled call rejects with `AbortError` — the UI
 * treats that as "superseded", not a failure.
 *
 * Determinism: every product id hashes to the same fixture (same images,
 * dimensions, dimensions and content), and the same scenario sequence always
 * produces the same state. Nothing here depends on `Math.random()` or the
 * wall clock.
 */

const READ_LATENCY_MS = 600
const UPLOAD_LATENCY_MS = 900
const MUTATION_LATENCY_MS = 500
const UPLOAD_PROGRESS_LATENCY_MS = 2_600

export type ShopMediaScenarioKey =
  | 'populated'
  | 'empty'
  | 'eightImage'
  | 'uploadProgress'
  | 'uploadFailure'
  | 'uploadForbidden'
  | 'uploadValidation'
  | 'staleVersion'
  | 'missingProduct'
  | 'forbidden'
  | 'gone'
  | 'rateLimited'
  | 'unavailable'
  | 'brokenImage'

export interface ShopMediaScenario {
  key: ShopMediaScenarioKey
  label: string
}

/** Named, dev-only scenarios. Selection happens in `ShopClientsProvider`. */
export const SHOP_MEDIA_SCENARIOS: readonly ShopMediaScenario[] = [
  { key: 'populated', label: 'گالری پُر (تصویر اصلی مشخص است)' },
  { key: 'empty', label: 'گالری خالی (تصویری بارگذاری نشده)' },
  { key: 'eightImage', label: 'حد ۸ تصویر (آپلود غیرفعال)' },
  { key: 'uploadProgress', label: 'آپلود در حال انجام (نشانگر پیشرفت)' },
  { key: 'uploadFailure', label: 'شکست آپلود (نیمه‌کاره نمی‌ماند)' },
  { key: 'uploadForbidden', label: 'آپلود: بدون مجوز (403)' },
  { key: 'uploadValidation', label: 'آپلود: خطای اعتبارسنجی (400)' },
  { key: 'staleVersion', label: 'نسخه قدیمی گالری (409)' },
  { key: 'missingProduct', label: 'محصول وجود ندارد (404)' },
  { key: 'forbidden', label: 'دسترسی به گالری رد شد (403)' },
  { key: 'gone', label: 'رسانه‌ها دیگر در دسترس نیست (410)' },
  { key: 'rateLimited', label: 'بیش از حد درخواست (429)' },
  { key: 'unavailable', label: 'سرور در دسترس نیست (تلاش دوباره)' },
  { key: 'brokenImage', label: 'تصویر خراب (جایگزین نمایش داده شود)' },
] as const

const SCENARIO_STORAGE_KEY = 'tfMediaScenario'

function readStoredScenarioKey(): ShopMediaScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_MEDIA_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopMediaScenarioKey
    }
  } catch {
    // Storage can be unavailable (private browsing); the default scenario wins.
  }
  return SHOP_MEDIA_SCENARIOS[0].key
}

let activeScenarioKey: ShopMediaScenarioKey = readStoredScenarioKey()

function activeScenario(): ShopMediaScenario {
  const key = activeScenarioKey
  const scenario = SHOP_MEDIA_SCENARIOS.find((entry) => entry.key === key)
  return scenario ?? { ...SHOP_MEDIA_SCENARIOS[0] }
}

/** The scenario key the mock currently runs with (for the dev switcher). */
export function getShopMediaScenarioKey(): ShopMediaScenarioKey {
  return activeScenarioKey
}

/**
 * Switches the active scenario, persists it for the page reload that follows,
 * and re-seeds state deterministically.
 */
export function setShopMediaScenario(key: ShopMediaScenarioKey): void {
  if (!SHOP_MEDIA_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop media scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
  seedState()
}

/** Revokes every mock-created object URL and resets to the default scenario (test/demo hygiene). */
export function resetShopMediaMock(): void {
  for (const url of objectUrls) URL.revokeObjectURL(url)
  objectUrls.clear()
  galleryState.clear()
  activeScenarioKey = SHOP_MEDIA_SCENARIOS[0].key
  seedState()
}

// Deterministic helpers -------------------------------------------------------

function hashString(value: string): number {
  let hash = 2166136261
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

function seededNumber(seed: number, index: number, min: number, span: number): number {
  let value = (seed + index * 2654435761) >>> 0
  value ^= value >>> 13
  value = Math.imul(value, 0x5bd1e995)
  value ^= value >>> 15
  return min + (value % span)
}

function imageDimensions(productId: string, imageNumber: number) {
  const seed = hashString(`${productId}:${imageNumber}`)
  const width = seededNumber(seed, 1, 600, 800)
  const height = seededNumber(seed, 2, 600, 800)
  return { width, height }
}

function fixtureHue(seed: number): number {
  return seed % 360
}

/**
 * Deterministic placeholder art as a data: URL (SVG). The same id always
 * returns the same picture, so reloads and re-renders are stable. The broken
 * scenario instead stores a deliberately invalid data URL, which makes the
 * real `<img>` fire `onerror` and the UI render its fallback.
 */
function fixtureContentUrl(productId: string, imageNumber: number): string {
  const seed = hashString(`${productId}:${imageNumber}`)
  const hue = fixtureHue(seed)
  const label = `تصویر ${imageNumber + 1}`
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="800" viewBox="0 0 800 800">` +
    `<rect width="800" height="800" fill="hsl(${hue} 45% 88%)"/>` +
    `<circle cx="400" cy="360" r="220" fill="hsl(${hue} 50% 70%)"/>` +
    `<text x="400" y="700" font-size="44" text-anchor="middle" fill="hsl(${hue} 35% 30%)" ` +
    `font-family="sans-serif">${label}</text></svg>`
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}

function brokenContentUrl(): string {
  return 'data:image/webp;base64,AAAAmock-broken-image-content'
}

// The mock's protected-byte route returns stored bytes as a Blob without any
// `fetch`: fixture images are converted synchronously from their data-URL
// payload, and the deliberately broken fixture converts to bytes whose real
// image content is invalid — a real <img> fails to render it, exactly like a
// corrupt server payload would.
function dataUrlToBlob(dataUrl: string): Blob {
  const comma = dataUrl.indexOf(',')
  const header = dataUrl.slice(0, comma)
  const mediaType = /data:(.*?)(;|$)/.exec(header)?.[1] ?? 'application/octet-stream'
  if (header.includes('base64')) {
    const binary = atob(dataUrl.slice(comma + 1))
    const bytes = new Uint8Array(binary.length)
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index)
    return new Blob([bytes], { type: mediaType })
  }
  return new Blob([decodeURIComponent(dataUrl.slice(comma + 1))], { type: mediaType })
}

// State -----------------------------------------------------------------------

type GalleryState = {
  product: ShopProductWithGallery
  /** imageId -> blob: URL created by this mock for uploaded images. */
  objectUrls: Map<string, string>
  /** imageId -> stored bytes for the protected-content route. */
  contentBlobs: Map<string, Blob>
}

const galleryState = new Map<string, GalleryState>()
const objectUrls = new Set<string>()

function trackObjectUrl(url: string): string {
  objectUrls.add(url)
  return url
}

function seedProduct(productId: string, categoryId: string, name: string, slug: string): ShopProductWithGallery {
  return {
    id: productId,
    tenantId: '01demo0000001',
    categoryId,
    name,
    slug,
    description: 'محصول نمایشی برای بررسی مدیریت رسانه.',
    basePrice: 1_250_000,
    compareAtPrice: null,
    isActive: true,
    variants: [
      { id: `${productId.slice(0, 10)}v1`, color: 'سرمه‌ای', size: 'L', sku: 'TF-0001-L', stockQuantity: 12, priceOverride: null },
      { id: `${productId.slice(0, 10)}v2`, color: 'خاکی', size: 'XL', sku: 'TF-0001-XL', stockQuantity: 0, priceOverride: 1_300_000 },
    ],
    sizeGuideColumns: [],
    sizeGuideRows: [],
    images: [],
    galleryVersion: 1,
  }
}

/**
 * Seeds a product's images in display order and stores every image's bytes in
 * `contentBlobs` — the mock's stand-in for the server's storage, so the
 * protected-content route can return real bytes for seeded images.
 */
function seedGallery(
  product: ShopProductWithGallery,
  count: number,
  contentFor: (index: number) => string,
): GalleryState {
  const contentBlobs = new Map<string, Blob>()
  product.images = Array.from({ length: count }, (_, index) => {
    const { width, height } = imageDimensions(product.id, index)
    const contentUrl = contentFor(index)
    const image = {
      id: `img${product.id.slice(3)}${String(index + 1).padStart(4, '0')}`,
      altText: `${product.name} — نمای ${index + 1}`,
      displayOrder: index,
      width,
      height,
      contentUrl,
    }
    contentBlobs.set(image.id, dataUrlToBlob(contentUrl))
    return image
  })
  if (count > 0) product.galleryVersion = 2
  return {
    product: shopProductWithGallerySchema.parse(product),
    objectUrls: new Map(),
    contentBlobs,
  }
}

let staleVersionArmed = false

function seedState(): void {
  for (const url of objectUrls) URL.revokeObjectURL(url)
  objectUrls.clear()
  galleryState.clear()
  staleVersionArmed = false
}

seedState()

/**
 * Lazily seeds a product's gallery for the active scenario.
 *
 * State is keyed by **product id only** — the mock models one gallery per
 * product, not per tenant (the real backend's tenant isolation arrives with
 * the HTTP client in F054; until then the demo works with any tenant's
 * products). The scenario chooses the gallery's content, so the same product
 * + same scenario always renders the same state:
 *
 * - `empty`        → no images (the "no uploads yet" state);
 * - `eightImage`   → exactly the 8-image cap (upload disabled);
 * - `brokenImage`  → populated, but the first image's stored bytes are corrupt;
 * - everything else → a 3-image populated gallery (the default demo data).
 *
 * Seeding happens on first access, so a freshly created catalog product opens
 * into the scenario's state without any per-product fixture table.
 */
function getOrCreateEntry(tenantId: string, productId: string): GalleryState {
  const existing = galleryState.get(productId)
  if (existing) return existing
  const product =
    productId === 'seedprod0000001'
      ? seedProduct('seedprod0000001', 'seedcat000001', 'ژاکت پاییزه', 'autumn-jacket')
      : seedProduct(
          productId,
          'seedcat000001',
          'محصول نمایشی',
          `demo-${productId.slice(-8)}`,
        )
  let imageCount = 3
  const scenario = activeScenario()
  if (scenario.key === 'empty') imageCount = 0
  if (scenario.key === 'eightImage') imageCount = MAX_GALLERY_IMAGES
  const entry = seedGallery(product, imageCount, (index) =>
    index === 0 && scenario.key === 'brokenImage'
      ? brokenContentUrl()
      : fixtureContentUrl(productId, index),
  )
  // The seed's tenantId is cosmetic in the mock; keep the caller's tenant so
  // the returned wire object reads consistently for any admin tenant.
  entry.product.tenantId = tenantId
  galleryState.set(productId, entry)
  return entry
}

// Error helpers ----------------------------------------------------------------

function abortError(): Error {
  return new DOMException('Aborted', 'AbortError')
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw abortError()
}

function scenarioError(status: number, message: string, options: ConstructorParameters<typeof ShopMediaError>[2] = {}): ShopMediaError {
  return new ShopMediaError(status, message, options)
}

function assertScenarioAllows(key: 'read' | 'upload' | 'mutate'): void {
  const { key: scenario } = activeScenario()
  if (key === 'read') {
    if (scenario === 'unavailable') throw new ApiUnavailableError()
    if (scenario === 'missingProduct') throw scenarioError(404, 'این محصول پیدا نشد یا دیگر در دسترس نیست.')
    if (scenario === 'forbidden') throw scenarioError(403, 'شما اجازه مشاهده رسانه‌های این محصول را ندارید.')
    if (scenario === 'gone') throw scenarioError(410, 'رسانه‌های این محصول دیگر در دسترس نیست.')
    if (scenario === 'rateLimited') throw scenarioError(429, 'درخواست‌های زیادی ارسال شده است. بعد از چند ثانیه دوباره تلاش کنید.', { retryAfterSeconds: 20 })
    return
  }
  if (key === 'upload') {
    if (scenario === 'unavailable') throw new ApiUnavailableError()
    if (scenario === 'missingProduct') throw scenarioError(404, 'این محصول پیدا نشد یا دیگر در دسترس نیست.')
    if (scenario === 'forbidden') throw scenarioError(403, 'شما اجازه ویرایش رسانه‌های این محصول را ندارید.')
    if (scenario === 'gone') throw scenarioError(410, 'رسانه‌های این محصول دیگر در دسترس نیست.')
    if (scenario === 'rateLimited') throw scenarioError(429, 'درخواست‌های زیادی ارسال شده است. بعد از چند ثانیه دوباره تلاش کنید.', { retryAfterSeconds: 20 })
    if (scenario === 'uploadForbidden') throw scenarioError(403, 'شما اجازه بارگذاری تصویر برای این محصول را ندارید.')
    // B036 names 415 (unsupported/corrupt media) as the upload failure; the
    // mock simulates the server rejecting the decoded bytes after the delay.
    if (scenario === 'uploadFailure') throw scenarioError(415, 'محتوای تصویر قابل بارگذاری نبود. یک فایل معتبر دیگر امتحان کنید.')
    // B036 field validation is 400: the scenario simulates the server
    // rejecting the request fields after the delay.
    if (scenario === 'uploadValidation') throw scenarioError(400, 'اطلاعات ارسالی کامل و معتبر نیست.', {
      fieldErrors: { altText: 'متن جایگزین نباید بیش از ۲۰۰ نویسه باشد.' },
    })
    return
  }
  if (key === 'mutate') {
    if (scenario === 'unavailable') throw new ApiUnavailableError()
  }
}

function getEntry(tenantId: string, productId: string): GalleryState {
  return getOrCreateEntry(tenantId, productId)
}

function snapshot(entry: GalleryState): ShopProductWithGallery {
  return shopProductWithGallerySchema.parse(entry.product)
}

function snapshotGallery(entry: GalleryState): ProductGallery {
  return { images: entry.product.images.map((image) => ({ ...image })), galleryVersion: entry.product.galleryVersion }
}

const STALE_CONFLICT_MESSAGE =
  'این گالری توسط دیگری به‌روزرسانی شده است. دوباره بارگذاری کنید و تغییرات را تکرار کنید.'

/**
 * Stale-version simulation (B036 409). In `staleVersion` mode the first
 * mutation succeeds, then the mock acts as if a concurrent editor bumped the
 * gallery version: every later mutation rejects with `409` carrying the
 * server's fresh `galleryVersion`, so the UI must tell the user to refresh
 * instead of silently overwriting (spec step 16 / definition of done).
 */
function scenarioAllowsMutation(entry: GalleryState, expectedVersion: number): void {
  if (activeScenario().key === 'staleVersion' && staleVersionArmed) {
    throw scenarioError(409, STALE_CONFLICT_MESSAGE, {
      fieldErrors: { galleryVersion: String(entry.product.galleryVersion) },
    })
  }
  if (expectedVersion !== entry.product.galleryVersion) {
    throw scenarioError(409, STALE_CONFLICT_MESSAGE, {
      fieldErrors: { galleryVersion: String(entry.product.galleryVersion) },
    })
  }
}

function commitMutation(entry: GalleryState): void {
  entry.product.galleryVersion += 1
  if (activeScenario().key === 'staleVersion') staleVersionArmed = true
}

// Client -----------------------------------------------------------------------

/** The mock ShopMediaClient. Deterministic per scenario; abort-aware. */
export const mockShopMediaClient: ShopMediaClient = {
  async getProduct(tenantId, productId, signal) {
    assertScenarioAllows('read')
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    return snapshot(getEntry(tenantId, productId))
  },

  async getProtectedContent(tenantId, productId, imageId, signal) {
    assertScenarioAllows('read')
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    const entry = getEntry(tenantId, productId)
    const image = entry.product.images.find((candidate) => candidate.id === imageId)
    if (!image) throw scenarioError(404, 'این تصویر پیدا نشد یا دیگر در دسترس نیست.')
    const stored = entry.contentBlobs.get(imageId)
    if (!stored) throw scenarioError(404, 'این تصویر پیدا نشد یا دیگر در دسترس نیست.')
    return stored
  },

  async upload(tenantId, productId, file, altText, expectedGalleryVersion, signal) {
    assertScenarioAllows('upload')
    const latency = activeScenario().key === 'uploadProgress' ? UPLOAD_PROGRESS_LATENCY_MS : UPLOAD_LATENCY_MS
    await delay(latency, signal)
    assertNotAborted(signal)

    const entry = getEntry(tenantId, productId)
    scenarioAllowsMutation(entry, expectedGalleryVersion)
    if (entry.product.images.length >= MAX_GALLERY_IMAGES) {
      throw scenarioError(409, 'حداکثر ۸ تصویر در گالری مجاز است. برای افزودن تصویر جدید یکی را حذف کنید.')
    }

    const dimensions = await readImageFile(file)
    if (!dimensions.isSupported) {
      // B036: unsupported/corrupt media is 415, not field validation.
      throw scenarioError(415, 'فقط تصویرهای JPEG، PNG یا WebP مجاز است.', {
        fieldErrors: { file: 'فرمت فایل پشتیبانی نمی‌شود.' },
      })
    }
    if (file.size > 5 * 1024 * 1024) {
      // B036: oversized media is 413.
      throw scenarioError(413, 'حجم تصویر باید حداکثر ۵ مگابایت باشد.', {
        fieldErrors: { file: 'حجم فایل بیش از حد مجاز است.' },
      })
    }

    const content = await encodeImageAsWebP(file)
    const imageId = `img${productId.slice(3)}u${String(entry.product.images.length + 1).padStart(4, '0')}`
    const contentUrl = trackObjectUrl(URL.createObjectURL(content))
    entry.objectUrls.set(imageId, contentUrl)
    entry.contentBlobs.set(imageId, content)
    entry.product.images = [
      ...entry.product.images,
      {
        id: imageId,
        altText: altText.trim() || `تصویر ${entry.product.images.length + 1}`,
        displayOrder: entry.product.images.length,
        width: dimensions.width,
        height: dimensions.height,
        contentUrl,
      },
    ]
    commitMutation(entry)
    return snapshotGallery(entry)
  },

  async reorder(tenantId, productId, body, signal) {
    assertScenarioAllows('mutate', body.expectedGalleryVersion)
    await delay(MUTATION_LATENCY_MS, signal)
    assertNotAborted(signal)
    const entry = getEntry(tenantId, productId)
    scenarioAllowsMutation(entry, body.expectedGalleryVersion)
    const current = entry.product.images.map((image) => image.id)
    if (body.imageIds.length !== current.length || !body.imageIds.every((id) => current.includes(id))) {
      throw scenarioError(400, 'فهرست تصاویر با گالری فعلی مطابقت ندارد.')
    }
    entry.product.images = body.imageIds.map((id, index) => ({
      ...entry.product.images.find((image) => image.id === id)!,
      displayOrder: index,
    }))
    commitMutation(entry)
    return snapshotGallery(entry)
  },

  async remove(tenantId, productId, imageId, expectedGalleryVersion, signal) {
    assertScenarioAllows('mutate', expectedGalleryVersion)
    await delay(MUTATION_LATENCY_MS, signal)
    assertNotAborted(signal)
    const entry = getEntry(tenantId, productId)
    scenarioAllowsMutation(entry, expectedGalleryVersion)
    const image = entry.product.images.find((candidate) => candidate.id === imageId)
    if (!image) throw scenarioError(404, 'این تصویر پیدا نشد یا دیگر در دسترس نیست.')
    const stored = entry.objectUrls.get(imageId)
    if (stored) {
      URL.revokeObjectURL(stored)
      objectUrls.delete(stored)
      entry.objectUrls.delete(imageId)
    }
    entry.contentBlobs.delete(imageId)
    entry.product.images = entry.product.images
      .filter((candidate) => candidate.id !== imageId)
      .map((candidate, index) => ({ ...candidate, displayOrder: index }))
    commitMutation(entry)
  },
}
