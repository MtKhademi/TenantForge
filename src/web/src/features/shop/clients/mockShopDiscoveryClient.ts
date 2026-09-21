import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  discoverySortSchema,
  storefrontProductListSchema,
  type DiscoveryQuery,
  type StorefrontProductSummary,
} from '../contracts/discoveryContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { ShopDiscoveryClient } from './ShopDiscoveryClient'

/**
 * S33 storefront discovery — deterministic mock implementation of
 * `ShopDiscoveryClient` (F045). F055 swaps in `httpShopDiscoveryClient` behind
 * the same interface. Scenario names live only in this file and the dev-only
 * switcher; they never reach product-facing markup.
 *
 * Behavior mirrors B037: `q` is trimmed and truncated to 100 characters and
 * matches product names case-insensitively, `categorySlug` resolves to a known
 * active category or returns the same non-leaking `404`, invalid `sort` values
 * return `400`, `saleOnly` includes only products whose compare-at price is
 * greater than the displayed card price, and pagination metadata reflects the
 * filtered set. Latency is simulated through the shared abort-aware `delay`
 * helper, so a superseded request rejects with `AbortError` instead of
 * resolving into stale UI.
 */

const READ_LATENCY_MS = 600

export type ShopDiscoveryScenarioKey =
  | 'populated'
  | 'onePage'
  | 'emptyResults'
  | 'invalidFilters'
  | 'missingCategory'
  | 'unavailable'

export interface ShopDiscoveryScenario {
  key: ShopDiscoveryScenarioKey
  label: string
}

export const SHOP_DISCOVERY_SCENARIOS: readonly ShopDiscoveryScenario[] = [
  { key: 'populated', label: 'فهرست چندصفحه‌ای' },
  { key: 'onePage', label: 'فهرست تک‌صفحه‌ای' },
  { key: 'emptyResults', label: 'نتیجه خالی' },
  { key: 'invalidFilters', label: 'فیلتر نامعتبر (400)' },
  { key: 'missingCategory', label: 'دسته‌ی ناموجود (404)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
] as const

const SCENARIO_STORAGE_KEY = 'tfDiscoveryScenario'

function readStoredScenarioKey(): ShopDiscoveryScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_DISCOVERY_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopDiscoveryScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_DISCOVERY_SCENARIOS[0].key
}

let activeScenarioKey: ShopDiscoveryScenarioKey = readStoredScenarioKey()

export function getShopDiscoveryScenarioKey(): ShopDiscoveryScenarioKey {
  return activeScenarioKey
}

export function setShopDiscoveryScenario(key: ShopDiscoveryScenarioKey): void {
  if (!SHOP_DISCOVERY_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop discovery scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Fixture data -----------------------------------------------------------------

// The slugs this mock treats as active, resolvable categories (the mock's
// stand-in for the backend's active-category check). They are private: the UI
// owns its own category filter *options* (view model); it only passes the
// chosen slug to the client. A real backend (F055/B038) will resolve slugs via
// its own category endpoint instead.
const KNOWN_CATEGORY_SLUGS = ['shoes', 'bags', 'accessories'] as const
type KnownCategorySlug = (typeof KNOWN_CATEGORY_SLUGS)[number]

interface ProductFixture {
  id: string
  name: string
  slug: string
  categorySlug: KnownCategorySlug
  basePrice: number
  compareAtPrice: number | null
  isSoldOut: boolean
  thumbnailUrl: string | null
}

function productFixtureUrl(name: string, seed: number): string {
  const hue = seed % 360
  const svg =
    `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="800" viewBox="0 0 800 800">` +
    `<rect width="800" height="800" fill="hsl(${hue} 45% 88%)"/>` +
    `<circle cx="400" cy="360" r="220" fill="hsl(${hue} 50% 70%)"/>` +
    `<text x="400" y="700" font-size="44" text-anchor="middle" fill="hsl(${hue} 35% 30%)" ` +
    `font-family="sans-serif">${name}</text></svg>`
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`
}

const SHOE_NAMES = [
  'کفش پیاده‌روی',
  'کفش رسمی',
  'کفش ورزشی',
  'کفش زمستانی',
  'کفش ساحلی',
  'کفش جلوباز',
  'کفش چرمی',
  'کفش بگودان',
  'کفش کتانی',
  'کفش رسمی مردانه',
] as const

const BAG_NAMES = [
  'کیف دستی',
  'کیف لپ‌تاپ',
  'کیف سفر',
  'کیف کمری',
  'کیف چرمی',
  'کیف پول',
  'کیف کتاب',
  'کیف بچگانه',
  'کیف دوشی',
  'کیف شب',
] as const

const ACCESSORY_NAMES = [
  'ساعت هوشمند',
  'هدفون بی‌سیم',
  'شارژر فست',
  'محافظ موبایل',
  'قاب موبایل',
  'کیبورد فشرده',
  'ماوس بی‌سیم',
  'هولدر خودرو',
  'عینک آفتابی',
  'کمربند چرمی',
] as const

/**
 * A stable 30-product catalog (ten per category) so search, sale, sort and
 * pagination all have a coherent many-page dataset to act on. IDs are
 * sequential and time-sortable, so `newest` = `id desc` reads as "last added
 * first". Prices vary across and within categories so price sorts are visible.
 */
function buildPopulatedFixtures(): ProductFixture[] {
  const groups: Array<{ categorySlug: KnownCategorySlug; names: readonly string[] }> = [
    { categorySlug: 'shoes', names: SHOE_NAMES },
    { categorySlug: 'bags', names: BAG_NAMES },
    { categorySlug: 'accessories', names: ACCESSORY_NAMES },
  ]

  const fixtures: ProductFixture[] = []
  let sequence = 0
  for (const { categorySlug, names } of groups) {
    for (const name of names) {
      const index = sequence
      const basePrice = 650_000 + (index * 97_500) % 2_400_000
      const isOnSale = index % 3 === 0
      const isSoldOut = index % 5 === 0
      fixtures.push({
        id: `p${String(index + 1).padStart(12, '0')}`,
        name,
        slug: `${categorySlug}-${String(index + 1).padStart(4, '0')}`,
        categorySlug,
        basePrice,
        compareAtPrice: isOnSale ? basePrice + 150_000 : null,
        isSoldOut,
        thumbnailUrl: index % 6 === 0 ? null : productFixtureUrl(name, index * 37),
      })
      sequence += 1
    }
  }
  return fixtures
}

function buildOnePageFixtures(): ProductFixture[] {
  return buildPopulatedFixtures().slice(0, 3)
}

// Query helpers ------------------------------------------------------------------

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

function invalidSortError(): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'مقدار مرتب‌سازی معتبر نیست.',
    detail: 'مقدار مرتب‌سازی باید یکی از مقادیر مجاز باشد.',
    errors: { sort: ['مقدار sort نامعتبر است.'] },
  })
}

function missingCategoryError(): ShopClientError {
  return new ShopClientError({
    status: 404,
    title: 'این دسته‌بندی پیدا نشد یا دیگر در دسترس نیست.',
    detail: 'این دسته‌بندی پیدا نشد یا دیگر در دسترس نیست.',
  })
}

function summaryFromFixture(fixture: ProductFixture): StorefrontProductSummary {
  // B037 card price: the displayed price is the lowest in-stock variant price;
  // when no variant is in stock the product is sold out and falls back to
  // `BasePrice`. The mock collapses that rule into `basePrice` + `isSoldOut`,
  // and sale status always compares against this displayed price.
  const effectivePrice = fixture.basePrice
  return {
    id: fixture.id,
    name: fixture.name,
    slug: fixture.slug,
    effectivePrice,
    compareAtPrice: fixture.compareAtPrice,
    isOnSale: fixture.compareAtPrice !== null && fixture.compareAtPrice > effectivePrice,
    isSoldOut: fixture.isSoldOut,
    thumbnailUrl: fixture.thumbnailUrl,
  }
}

function applyQuery(fixtures: ProductFixture[], query: DiscoveryQuery): ProductFixture[] {
  let result = [...fixtures]

  const q = query.q.trim().slice(0, 100)
  if (q.length > 0) {
    const needle = q.toLocaleLowerCase('fa')
    result = result.filter((fixture) => fixture.name.toLocaleLowerCase('fa').includes(needle))
  }

  const categorySlug = query.categorySlug?.trim() ?? ''
  if (categorySlug.length > 0) {
    result = result.filter((fixture) => fixture.categorySlug === categorySlug)
  }

  if (query.saleOnly) {
    result = result.filter((fixture) => {
      const summary = summaryFromFixture(fixture)
      return summary.isOnSale
    })
  }

  const sorted = [...result].sort((a, b) => {
    switch (query.sort) {
      case 'newest':
        return a.id < b.id ? 1 : a.id > b.id ? -1 : 0
      case 'price-asc': {
        const priceA = summaryFromFixture(a).effectivePrice
        const priceB = summaryFromFixture(b).effectivePrice
        if (priceA !== priceB) return priceA - priceB
        return a.id < b.id ? -1 : a.id > b.id ? 1 : 0
      }
      case 'price-desc': {
        const priceA = summaryFromFixture(a).effectivePrice
        const priceB = summaryFromFixture(b).effectivePrice
        if (priceA !== priceB) return priceB - priceA
        return a.id < b.id ? -1 : a.id > b.id ? 1 : 0
      }
      case 'name': {
        const compared = a.name.localeCompare(b.name, 'fa')
        if (compared !== 0) return compared
        return a.id < b.id ? -1 : a.id > b.id ? 1 : 0
      }
    }
  })

  return sorted
}

function paginate(sorted: ProductFixture[], pageNumber: number, pageSize: number): StorefrontProductSummary[] {
  const totalCount = sorted.length
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize)
  const safePageNumber = totalCount === 0 ? 1 : Math.min(Math.max(pageNumber, 1), totalPages)
  const start = (safePageNumber - 1) * pageSize
  return sorted.slice(start, start + pageSize).map(summaryFromFixture)
}

function buildResponse(products: StorefrontProductSummary[], totalCount: number, pageNumber: number, pageSize: number) {
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / pageSize)
  const safePageNumber = totalCount === 0 ? 1 : Math.min(Math.max(pageNumber, 1), totalPages)
  const response = {
    products,
    pagination: {
      pageNumber: safePageNumber,
      pageSize,
      totalCount,
      totalPages,
      hasPreviousPage: safePageNumber > 1,
      hasNextPage: safePageNumber < totalPages,
    },
  }
  return storefrontProductListSchema.parse(response)
}

// Client --------------------------------------------------------------------------

export const mockShopDiscoveryClient: ShopDiscoveryClient = {
  async listProducts(_tenantId, query, signal) {
    const scenario = activeScenarioKey
    if (scenario === 'unavailable') {
      await delay(READ_LATENCY_MS, signal)
      assertNotAborted(signal)
      throw new ApiUnavailableError()
    }

    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (scenario === 'invalidFilters' || !discoverySortSchema.safeParse(query.sort).success) {
      throw invalidSortError()
    }

    const categorySlug = query.categorySlug?.trim() ?? ''
    if (scenario === 'missingCategory' || (categorySlug.length > 0 && !KNOWN_CATEGORY_SLUGS.includes(categorySlug as KnownCategorySlug))) {
      throw missingCategoryError()
    }

    if (query.pageNumber < 1 || query.pageSize < 1) {
      throw new ShopClientError({
        status: 400,
        title: 'مقادیر صفحه‌بندی معتبر نیست.',
        detail: 'شماره صفحه و تعداد موارد در صفحه باید مثبت باشند.',
        errors: { pageNumber: ['مقدار pageNumber نامعتبر است.'] },
      })
    }

    let fixtures: ProductFixture[]
    if (scenario === 'onePage') fixtures = buildOnePageFixtures()
    else if (scenario === 'emptyResults') fixtures = []
    else fixtures = buildPopulatedFixtures()

    const filtered = applyQuery(fixtures, query)
    const page = paginate(filtered, query.pageNumber, query.pageSize)
    return buildResponse(page, filtered.length, query.pageNumber, query.pageSize)
  },
}
