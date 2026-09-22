import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  publicShopProfileSchema,
  shopProfileSchema,
  type PublicShopProfile,
  type SaveShopProfileRequest,
  type ShopProfile,
} from '../contracts/shopProfileContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { ShopProfileClient } from './ShopProfileClient'

/**
 * S35 storefront identity and policies — deterministic mock implementation of
 * `ShopProfileClient` (F047). F057 swaps in `httpShopProfileClient` behind the
 * same interface. Scenario names live only in this file and the dev-only
 * switcher; they never reach product-facing markup.
 *
 * Behavior mirrors B039:
 * - admin `getAdmin` returns the stored profile, or `null` before the first
 *   save (the "null-first" setup state — a 200, never a 404);
 * - `save` with `expectedVersion === null` is a create; any other value must
 *   equal the stored `version` or the mock rejects with `409 stale_version`;
 *   a successful save bumps `version` and rewrites `updatedAtUtc`;
 * - `getPublic` returns the published profile only — `null` when the profile
 *   is missing or unpublished (the anonymous route's 404);
 * - the B039 field validations (lengths, phone allowlist, instagram host)
 *   are re-applied on `save` and surface as `400` with per-field errors;
 * - the `unavailable` scenario throws `ApiUnavailableError`, the `forbidden`
 *   scenario throws `403`, exactly like the F044–F046 mocks.
 *
 * Mutations persist in memory for the page lifetime; a reload re-seeds the
 * store deterministically. Latency is simulated through the shared
 * abort-aware `delay` helper, so a superseded request rejects with
 * `AbortError` instead of resolving into stale UI.
 */

const READ_LATENCY_MS = 500
const WRITE_LATENCY_MS = 600

/** Canonical 13-character TSID-style IDs, stable and time-sortable. */
const TENANT_ID = '019c000000001'
const PROFILE_ID = '019c000000002'

// Scenario definitions ---------------------------------------------------------

export type ShopProfileScenarioKey =
  | 'nullFirst'
  | 'published'
  | 'unpublished'
  | 'staleVersion'
  | 'forbidden'
  | 'unavailable'

export interface ShopProfileScenario {
  key: ShopProfileScenarioKey
  label: string
}

export const SHOP_PROFILE_SCENARIOS: readonly ShopProfileScenario[] = [
  { key: 'nullFirst', label: 'بدون پروفایل (خروج null)' },
  { key: 'published', label: 'منتشرشده' },
  { key: 'unpublished', label: 'غیرمنتشرشده' },
  { key: 'staleVersion', label: 'تصادف نسخه (409)' },
  { key: 'forbidden', label: 'بدون مجوز (403)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
] as const

const SCENARIO_STORAGE_KEY = 'tfProfileScenario'

function readStoredScenarioKey(): ShopProfileScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_PROFILE_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopProfileScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_PROFILE_SCENARIOS[0].key
}

let activeScenarioKey: ShopProfileScenarioKey = readStoredScenarioKey()

export function getShopProfileScenarioKey(): ShopProfileScenarioKey {
  return activeScenarioKey
}

export function setShopProfileScenario(key: ShopProfileScenarioKey): void {
  if (!SHOP_PROFILE_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop profile scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Fixture ----------------------------------------------------------------------

const SEED_PROFILE: ShopProfile = {
  id: PROFILE_ID,
  tenantId: TENANT_ID,
  name: 'فروشگاه نور',
  tagline: 'خانه‌ی کالاهای دیجیتال، با گارانتی معتبر و ارسال سریع',
  supportPhone: '۰۲۱-۹۱۰۰۰۰۰۰',
  instagramUrl: 'https://www.instagram.com/noorshop',
  aboutText:
    'فروشگاه نور از سال ۱۳۹۵ در کنار شماست.\nما معتقدیم خرید آنلاین باید راحت، شفاف و بدون نگرانی باشد؛ به همین دلیل همه‌ی محصولات ما گارانتی اصالت دارند.',
  shippingPolicy:
    'ارسال به تهران معمولاً ۱ تا ۲ روز کاری طول می‌کشد.\nارسال به شهرستان‌ها ۲ تا ۵ روز کاری.\nسفارش‌های بالای ۵۰۰ هزار تومان ارسال رایگان دارند.',
  paymentPolicy: 'پرداخت از طریق درگاه بانکی معتبر انجام می‌شود.\nپرداخت در محل برای سفارش‌های تهران فعال است.',
  returnPolicy: 'کالا تا ۷ روز پس از تحویل قابل بازگشت است.\nشرط بازگشت: کالا بدون استفاده و در بسته‌بندی اولیه باشد.',
  privacyPolicy: 'اطلاعات شما فقط برای پردازش سفارش استفاده می‌شود.\nما هرگز اطلاعات شما را به شخص ثالث نمی‌فروشیم.',
  isPublished: true,
  version: 3,
  updatedAtUtc: '2026-09-01T09:00:00Z',
}

/** In-memory store: one profile row per tenant, lazily seeded per scenario. */
let stored: ShopProfile | null = null
let seededForScenario: ShopProfileScenarioKey | null = null

function seedForScenario(scenario: ShopProfileScenarioKey): ShopProfile | null {
  if (scenario === 'nullFirst') return null
  if (scenario === 'published') return { ...SEED_PROFILE }
  if (scenario === 'unpublished') return { ...SEED_PROFILE, isPublished: false }
  // 'staleVersion', 'forbidden', 'unavailable' all operate on a real profile.
  return { ...SEED_PROFILE }
}

function getOrSeed(): ShopProfile | null {
  if (seededForScenario !== activeScenarioKey) {
    stored = seedForScenario(activeScenarioKey)
    seededForScenario = activeScenarioKey
  }
  return stored
}

// Error helpers — every error uses the shared RFC7807 `ShopClientError` shape.

function staleVersionError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'stale_version',
    title: 'تغییرات ذخیره نشد.',
    detail: 'نسخه‌ی پروفایل به‌روزرسانی شده است؛ تغییرات جدید را بارگذاری کنید و دوباره امتحان کنید.',
  })
}

function forbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'دسترسی مدیریت فروشگاه مجاز نیست.',
    detail: 'حساب فعلی مجوز مدیریت هویت و سیاست‌های فروشگاه این مستأجر را ندارد.',
  })
}

function validationError(fields: Record<string, string[]>): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'مقادیر ارسالی معتبر نیست.',
    detail: 'لطفاً مقادیر مشخص‌شده را اصلاح کنید.',
    errors: fields,
  })
}

// B039 field rules ---------------------------------------------------------------

const FIELD_LIMITS: Record<string, number> = {
  name: 100,
  tagline: 180,
  supportPhone: 30,
  instagramUrl: 300,
  aboutText: 4000,
  shippingPolicy: 6000,
  paymentPolicy: 6000,
  returnPolicy: 6000,
  privacyPolicy: 6000,
}

const PHONE_ALLOWED = /^[0-9\s+\-()۰-۹]*$/
const INSTAGRAM_URL = /^https:\/\/([a-z0-9-]+\.)?instagram\.com(\/[\w\-./?#=&%@+,:;~!]*)?$/i

/** Re-apply B039's trim + format rules; returns a `400` error or null. */
function validateSave(body: SaveShopProfileRequest): ShopClientError | null {
  const errors: Record<string, string[]> = {}
  const textFields = [
    'name',
    'tagline',
    'supportPhone',
    'instagramUrl',
    'aboutText',
    'shippingPolicy',
    'paymentPolicy',
    'returnPolicy',
    'privacyPolicy',
  ] as const

  for (const field of textFields) {
    const raw = body[field] ?? ''
    if (raw.length > FIELD_LIMITS[field]) {
      errors[field] = [`حداکثر ${FIELD_LIMITS[field]} کاراکتر مجاز است.`]
    }
  }

  if (body.name.trim().length === 0) {
    errors.name = ['نام فروشگاه الزامی است.']
  }

  if (body.supportPhone.length > 0 && !PHONE_ALLOWED.test(body.supportPhone)) {
    errors.supportPhone = ['فقط ارقام، فاصله و + - ( ) مجاز است.']
  }

  if (body.instagramUrl && !INSTAGRAM_URL.test(body.instagramUrl)) {
    errors.instagramUrl = ['نشانی باید HTTPS و دامنه‌ی instagram.com باشد.']
  }

  return Object.keys(errors).length > 0 ? validationError(errors) : null
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

function toPublicProfile(profile: ShopProfile): PublicShopProfile {
  return publicShopProfileSchema.parse({
    name: profile.name,
    tagline: profile.tagline,
    supportPhone: profile.supportPhone,
    instagramUrl: profile.instagramUrl,
    aboutText: profile.aboutText,
    shippingPolicy: profile.shippingPolicy,
    paymentPolicy: profile.paymentPolicy,
    returnPolicy: profile.returnPolicy,
    privacyPolicy: profile.privacyPolicy,
    isPublished: profile.isPublished,
  })
}

// Client -------------------------------------------------------------------------

export const mockShopProfileClient: ShopProfileClient = {
  async getAdmin(_tenantId, signal) {
    if (activeScenarioKey === 'unavailable') {
      await delay(READ_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    const profile = getOrSeed()
    return profile ? shopProfileSchema.parse(profile) : null
  },

  async save(_tenantId, body, signal) {
    if (activeScenarioKey === 'unavailable') {
      await delay(WRITE_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'forbidden') throw forbiddenError()

    const existing = getOrSeed()

    // The stale-version scenario stands in for a save whose `expectedVersion`
    // no longer matches the server's row (another request won the race). It
    // rejects every update — and the loser of a concurrent first-create —
    // with the exact B039 conflict shape, so the UI's conflict state renders
    // deterministically.
    if (activeScenarioKey === 'staleVersion' && (existing !== null || body.expectedVersion !== null)) {
      throw staleVersionError()
    }

    const problem = validateSave(body)
    if (problem !== null) throw problem

    const trimmed: SaveShopProfileRequest = {
      ...body,
      name: body.name.trim(),
      tagline: body.tagline.trim(),
      supportPhone: body.supportPhone.trim(),
      instagramUrl: body.instagramUrl?.trim() === '' ? null : body.instagramUrl?.trim() ?? null,
      aboutText: body.aboutText.trim(),
      shippingPolicy: body.shippingPolicy.trim(),
      paymentPolicy: body.paymentPolicy.trim(),
      returnPolicy: body.returnPolicy.trim(),
      privacyPolicy: body.privacyPolicy.trim(),
    }

    let saved: ShopProfile
    if (body.expectedVersion === null) {
      if (existing !== null) {
        // Concurrent create: the unique TenantId constraint rejects the loser.
        throw staleVersionError()
      }
      saved = {
        id: PROFILE_ID,
        tenantId: TENANT_ID,
        name: trimmed.name,
        tagline: trimmed.tagline,
        supportPhone: trimmed.supportPhone,
        instagramUrl: trimmed.instagramUrl,
        aboutText: trimmed.aboutText,
        shippingPolicy: trimmed.shippingPolicy,
        paymentPolicy: trimmed.paymentPolicy,
        returnPolicy: trimmed.returnPolicy,
        privacyPolicy: trimmed.privacyPolicy,
        isPublished: trimmed.isPublished,
        version: 1,
        updatedAtUtc: new Date().toISOString(),
      }
    } else {
      if (existing === null) {
        // No row to update — the backend 404s; surface it as a 409 so the
        // UI's conflict recovery path (reload) is the single recovery.
        throw staleVersionError()
      }
      if (body.expectedVersion !== existing.version) {
        throw staleVersionError()
      }
      saved = {
        ...existing,
        name: trimmed.name,
        tagline: trimmed.tagline,
        supportPhone: trimmed.supportPhone,
        instagramUrl: trimmed.instagramUrl,
        aboutText: trimmed.aboutText,
        shippingPolicy: trimmed.shippingPolicy,
        paymentPolicy: trimmed.paymentPolicy,
        returnPolicy: trimmed.returnPolicy,
        privacyPolicy: trimmed.privacyPolicy,
        isPublished: trimmed.isPublished,
        version: existing.version + 1,
        updatedAtUtc: new Date().toISOString(),
      }
    }

    stored = saved
    return shopProfileSchema.parse(saved)
  },

  async getPublic(_tenantId, signal) {
    if (activeScenarioKey === 'unavailable') {
      await delay(READ_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    const profile = getOrSeed()
    // Missing or unpublished → the anonymous route's 404 → the client
    // resolves to null (the UI renders the neutral fallback, not an error).
    if (profile === null || !profile.isPublished) return null
    return toPublicProfile(profile)
  },
}
