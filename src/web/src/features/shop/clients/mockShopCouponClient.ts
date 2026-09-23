import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  couponListResponseSchema,
  couponSchema,
  type Coupon,
  type CouponListResponse,
} from '../contracts/couponRulesContract'
import { ShopClientError, type PageQuery } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { ShopCouponClient } from './ShopCouponClient'

/**
 * S37 coupon rules — deterministic mock implementation of `ShopCouponClient`
 * (F049). F059 swaps in `httpShopCouponClient` behind the same interface.
 * Scenario names live only in this file and the dev-only switcher; they never
 * reach product-facing markup.
 *
 * Behavior mirrors B041 (docs/design/shop/http-contracts.md, "S37 / B041"):
 * - a created coupon is always active, version 1; a duplicate code is a `409`;
 * - `update` is guarded by `expectedVersion` — a mismatch is `409 stale_version`;
 *   a new `redemptionLimit` below the current `redeemedCount` is a `400` naming
 *   `redemptionLimit`;
 * - `deactivate` flips `isActive` to false and bumps `version`;
 * - `null` is "unlimited" for `maximumDiscountAmount` and `redemptionLimit`;
 * - B041's field validations are re-applied on create/update in the `default`
 *   scenario, so a bad body surfaces a `400` with per-field errors;
 * - a missing coupon is a plain `404`; a missing `Shop.Shipping.Manage`
 *   permission is a `403`; a network failure throws `ApiUnavailableError`.
 *
 * In-memory state: one coupon list per tenant, lazily seeded per scenario.
 * `redeemedCount` is NEVER decremented by this mock — a checkout preview only
 * reads it, so the "usage only changes on a real redemption" rule holds by
 * construction. Mutations persist for the page lifetime; a reload re-seeds the
 * store deterministically. Latency is simulated through the shared abort-aware
 * `delay` helper, so a superseded request rejects with `AbortError` instead of
 * resolving into stale UI.
 */

const READ_LATENCY_MS = 500
const WRITE_LATENCY_MS = 600

/** B041 redemption-limit bounds (mirrored from the backend policy). */
const MIN_REDEMPTION_LIMIT = 1
const MAX_REDEMPTION_LIMIT = 1_000_000

// Scenario definitions ---------------------------------------------------------

export type ShopCouponScenarioKey =
  | 'default'
  | 'empty'
  | 'staleVersion'
  | 'validation'
  | 'notFound'
  | 'forbidden'
  | 'unavailable'

export interface ShopCouponScenario {
  key: ShopCouponScenarioKey
  label: string
}

export const SHOP_COUPON_SCENARIOS: readonly ShopCouponScenario[] = [
  { key: 'default', label: 'فهرست کامل (پیش‌فرض)' },
  { key: 'empty', label: 'خالی' },
  { key: 'staleVersion', label: 'تصادف نسخه (409)' },
  { key: 'validation', label: 'اعتبارسنجی نامعتبر (400)' },
  { key: 'notFound', label: 'کد یافت نشد (404)' },
  { key: 'forbidden', label: 'بدون مجوز (403)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
] as const

const SCENARIO_STORAGE_KEY = 'tfCouponScenario'

function readStoredScenarioKey(): ShopCouponScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_COUPON_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopCouponScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_COUPON_SCENARIOS[0].key
}

let activeScenarioKey: ShopCouponScenarioKey = readStoredScenarioKey()

export function getShopCouponScenarioKey(): ShopCouponScenarioKey {
  return activeScenarioKey
}

export function setShopCouponScenario(key: ShopCouponScenarioKey): void {
  if (!SHOP_COUPON_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop coupon scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Fixture ----------------------------------------------------------------------

/**
 * The deterministic `default` seed — one row per visual state the task names:
 * unlimited (both null), exhausted redemption limit, redemption-locked
 * (redeemedCount > 0), expired, and inactive.
 */
function seedCoupons(): Coupon[] {
  return [
    couponSchema.parse({
      id: '019c000001001',
      code: 'WELCOME10',
      discountType: 'Percentage',
      discountValue: 10,
      minimumSubtotal: 0,
      maximumDiscountAmount: null,
      redemptionLimit: null,
      redeemedCount: 0,
      isActive: true,
      expiresAtUtc: null,
      version: 1,
    }),
    couponSchema.parse({
      id: '019c000001002',
      code: 'FIXED50K',
      discountType: 'FixedAmount',
      discountValue: 50_000,
      minimumSubtotal: 200_000,
      maximumDiscountAmount: null,
      redemptionLimit: 5,
      redeemedCount: 5,
      isActive: true,
      expiresAtUtc: null,
      version: 2,
    }),
    couponSchema.parse({
      id: '019c000001003',
      code: 'SPRING15',
      discountType: 'Percentage',
      discountValue: 15,
      minimumSubtotal: 500_000,
      maximumDiscountAmount: 120_000,
      redemptionLimit: 20,
      redeemedCount: 3,
      isActive: true,
      expiresAtUtc: null,
      version: 4,
    }),
    couponSchema.parse({
      id: '019c000001004',
      code: 'BIGSPEND5',
      discountType: 'FixedAmount',
      discountValue: 5_000,
      minimumSubtotal: 2_000_000,
      maximumDiscountAmount: null,
      redemptionLimit: null,
      redeemedCount: 0,
      isActive: true,
      expiresAtUtc: null,
      version: 1,
    }),
    couponSchema.parse({
      id: '019c000001006',
      code: 'OLD20',
      discountType: 'Percentage',
      discountValue: 20,
      minimumSubtotal: 0,
      maximumDiscountAmount: null,
      redemptionLimit: null,
      redeemedCount: 7,
      isActive: true,
      expiresAtUtc: '2025-06-01T00:00:00Z',
      version: 1,
    }),
    couponSchema.parse({
      id: '019c000001005',
      code: 'DEAD5',
      discountType: 'FixedAmount',
      discountValue: 5_000,
      minimumSubtotal: 0,
      maximumDiscountAmount: null,
      redemptionLimit: null,
      redeemedCount: 0,
      isActive: false,
      expiresAtUtc: null,
      version: 3,
    }),
  ]
}

/** In-memory store: one coupon list per tenant, lazily seeded per scenario. */
const storeByTenant = new Map<string, Coupon[]>()
const seededFor = new Map<string, ShopCouponScenarioKey>()

function getOrSeed(tenantId: string): Coupon[] {
  if (!seededFor.has(tenantId) || seededFor.get(tenantId) !== activeScenarioKey) {
    storeByTenant.set(tenantId, activeScenarioKey === 'empty' ? [] : seedCoupons().map((c) => ({ ...c })))
    seededFor.set(tenantId, activeScenarioKey)
  }
  return storeByTenant.get(tenantId) as Coupon[]
}

let mintedIdSequence = 0
/** Mints the next deterministic TSID-style id (13 chars). */
function mintCouponId(): string {
  mintedIdSequence += 1
  return `019c000003${String(mintedIdSequence).padStart(3, '0')}`
}

// Error helpers — every error uses the shared RFC7807 `ShopClientError` shape.

function staleVersionError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'stale_version',
    title: 'تغییرات ذخیره نشد.',
    detail: 'این کد تخفیف پیش از اعمال درخواست شما تغییر کرده است. فهرست را بارگذاری کنید و دوباره تلاش کنید.',
  })
}

function duplicateCodeError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    title: 'تکرار کد تخفیف.',
    detail: 'کد تخفیفی با همین کد از قبل برای این فروشگاه وجود دارد.',
    errors: { code: ['کد تخفیفی با همین کد از قبل وجود دارد.'] },
  })
}

function notFoundError(): ShopClientError {
  return new ShopClientError({
    status: 404,
    title: 'کد تخفیف یافت نشد.',
  })
}

function forbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'دسترسی مدیریت فروشگاه مجاز نیست.',
    detail: 'حساب فعلی مجوز مدیریت کدهای تخفیف این مستأجر را ندارد.',
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

/** The deterministic 400 body the `validation` scenario returns on every write. */
function forcedValidationError(): ShopClientError {
  return validationError({
    discountValue: ['درصد تخفیف باید بین ۱ تا ۱۰۰ باشد.'],
    minimumSubtotal: ['حداقل جمع سبد باید صفر یا بیشتر باشد.'],
    redemptionLimit: ['سقف استفاده باید بین ۱ تا ۱٬۰۰۰٬۰۰۰ باشد.'],
  })
}

// B041 field rules (re-applied on create/update in the `default` scenario) ------

/** Re-applies B041's write validation; returns a `400` error or null when valid. */
function validateWrite(
  body: {
    discountType: 'Percentage' | 'FixedAmount'
    discountValue: number
    minimumSubtotal: number
    maximumDiscountAmount: number | null
    redemptionLimit: number | null
  },
  currentRedeemedCount: number,
): ShopClientError | null {
  const errors: Record<string, string[]> = {}
  if (body.discountValue <= 0 || (body.discountType === 'Percentage' && body.discountValue > 100)) {
    errors.discountValue =
      body.discountType === 'Percentage'
        ? ['درصد تخفیف باید بین ۱ تا ۱۰۰ باشد.']
        : ['مقدار تخفیف باید بزرگ‌تر از صفر باشد.']
  }
  if (body.minimumSubtotal < 0) {
    errors.minimumSubtotal = ['حداقل جمع سبد باید صفر یا بیشتر باشد.']
  }
  if (body.maximumDiscountAmount !== null && body.maximumDiscountAmount < 0) {
    errors.maximumDiscountAmount = ['سقف تخفیف باید صفر یا بیشتر باشد.']
  }
  if (
    body.redemptionLimit !== null &&
    (body.redemptionLimit < MIN_REDEMPTION_LIMIT || body.redemptionLimit > MAX_REDEMPTION_LIMIT)
  ) {
    errors.redemptionLimit = ['سقف استفاده باید بین ۱ تا ۱٬۰۰۰٬۰۰۰ باشد.']
  }
  if (
    body.redemptionLimit !== null &&
    body.redemptionLimit < currentRedeemedCount
  ) {
    errors.redemptionLimit = ['سقف استفاده نمی‌تواند کمتر از تعداد استفاده‌ی فعلی باشد.']
  }
  return Object.keys(errors).length > 0 ? validationError(errors) : null
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

// Scenario short-circuits ------------------------------------------------------

/**
 * Applies the scenario's forced failure (if any) before real behavior runs.
 * `notFound` only short-circuits update/deactivate (the id-targeted writes);
 * list and create keep their real behavior so the checkout coupon preview and
 * the empty/create flows still work in that scenario.
 */
function scenarioGuard(scenario: 'read' | 'write', mutation: 'update' | 'deactivate' | null): void {
  if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
  if (activeScenarioKey === 'forbidden') throw forbiddenError()
  if (scenario === 'write') {
    if (activeScenarioKey === 'staleVersion' && mutation === 'update') throw staleVersionError()
    if (activeScenarioKey === 'validation') throw forcedValidationError()
    if (activeScenarioKey === 'notFound' && (mutation === 'update' || mutation === 'deactivate')) {
      throw notFoundError()
    }
  }
}

function toListResponse(coupons: Coupon[], query: PageQuery): CouponListResponse {
  const ordered = [...coupons].sort((a, b) => (a.code === b.code ? a.id.localeCompare(b.id) : a.code.localeCompare(b.code)))
  const totalCount = ordered.length
  const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / query.pageSize)
  const start = (query.pageNumber - 1) * query.pageSize
  const pageItems = ordered.slice(start, start + query.pageSize)
  return couponListResponseSchema.parse({
    coupons: pageItems.map((c) => ({ ...c })),
    pagination: {
      pageNumber: query.pageNumber,
      pageSize: query.pageSize,
      totalCount,
      totalPages,
      hasPreviousPage: query.pageNumber > 1,
      hasNextPage: query.pageNumber < totalPages,
    },
  })
}

// Client -------------------------------------------------------------------------

export const mockShopCouponClient: ShopCouponClient = {
  async list(tenantId, query, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    scenarioGuard('read', null)
    return toListResponse(getOrSeed(tenantId), query)
  },

  async create(tenantId, body, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    scenarioGuard('write', null)

    const store = getOrSeed(tenantId)
    if (store.some((c) => c.code.toUpperCase() === body.code.trim().toUpperCase())) {
      throw duplicateCodeError()
    }
    const problem = validateWrite(body, 0)
    if (problem !== null) throw problem

    const created = couponSchema.parse({
      id: mintCouponId(),
      code: body.code.trim(),
      discountType: body.discountType,
      discountValue: body.discountValue,
      minimumSubtotal: body.minimumSubtotal,
      maximumDiscountAmount: body.maximumDiscountAmount,
      redemptionLimit: body.redemptionLimit,
      redeemedCount: 0,
      isActive: true,
      expiresAtUtc: body.expiresAtUtc,
      version: 1,
    })
    store.push({ ...created })
    return { ...created }
  },

  async update(tenantId, couponId, body, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    scenarioGuard('write', 'update')

    const store = getOrSeed(tenantId)
    const index = store.findIndex((c) => c.id === couponId)
    if (index === -1) throw notFoundError()
    const existing = store[index]

    // The update request carries no `discountType` (it is immutable), so the
    // percentage-vs-fixed value rule is checked against the stored row's type.
    const problem = validateWrite(
      { ...body, discountType: existing.discountType },
      existing.redeemedCount,
    )
    if (problem !== null) throw problem
    if (existing.version !== body.expectedVersion) throw staleVersionError()

    const updated = couponSchema.parse({
      ...existing,
      discountValue: body.discountValue,
      minimumSubtotal: body.minimumSubtotal,
      maximumDiscountAmount: body.maximumDiscountAmount,
      redemptionLimit: body.redemptionLimit,
      expiresAtUtc: body.expiresAtUtc,
      isActive: body.isActive,
      version: existing.version + 1,
    })
    store[index] = { ...updated }
    return { ...updated }
  },

  async deactivate(tenantId, couponId, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    scenarioGuard('write', 'deactivate')

    const store = getOrSeed(tenantId)
    const index = store.findIndex((c) => c.id === couponId)
    if (index === -1) throw notFoundError()
    const existing = store[index]
    const deactivated = couponSchema.parse({
      ...existing,
      isActive: false,
      version: existing.version + 1,
    })
    store[index] = { ...deactivated }
    return { ...deactivated }
  },
}
