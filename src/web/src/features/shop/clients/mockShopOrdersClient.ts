import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  adminOrderDetailSchema,
  adminOrderListResponseSchema,
  adminOrderSummarySchema,
  type AdminOrderDetail,
  type AdminOrderStatus,
} from '../contracts/adminOrdersContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { AdminOrderFilters, ShopOrdersClient } from './ShopOrdersClient'

/**
 * S38 (B042) — deterministic mock implementation of `ShopOrdersClient` (F050).
 * F060 swaps in `httpShopOrdersClient` behind the same interface. Scenario
 * names live only in this file and the dev-only switcher; they never reach
 * product-facing markup.
 *
 * Behavior mirrors B042 (docs/design/shop/http-contracts.md, "S38 / B042"):
 * - the list applies `q` (case-insensitive contains across order number,
 *   tracking code, customer phone, customer name), `status`, and the
 *   inclusive-start / exclusive-end date window, AND-combined, then sorts
 *   `CreatedAtUtc desc, Id desc` and paginates;
 * - an invalid filter is a `400` naming the field (unknown `status`, a
 *   malformed date, or a range over 366 days);
 * - the detail returns the SAME non-leaking `404` for a malformed, missing or
 *   other-tenant order id;
 * - payment attempts are capped at the **20 newest** (descending by
 *   `CreatedAtUtc`, then `Id`);
 * - a member without `Shop.Orders.View` is a `403`; a network failure throws
 *   `ApiUnavailableError`.
 *
 * The mock is deterministic: the same input always yields the same output.
 * Latency is simulated through the shared abort-aware `delay` helper, so a
 * superseded request rejects with `AbortError` instead of resolving into stale
 * UI.
 */

const READ_LATENCY_MS = 500

/**
 * The only tenant the mock seeds orders for: the F029 demo boutique. B042
 * scopes every read to the route tenant, so an order id in any other tenant's
 * scope finds no order and collapses into the SAME non-leaking `404` as a
 * malformed or missing id (Required states: "foreign/unknown order ID").
 */
const DEMO_TENANT_ID = '0RN590ZYXKNZ2'

// Scenario definitions ---------------------------------------------------------

export type ShopOrdersScenarioKey =
  | 'default'
  | 'empty'
  | 'invalidFilter'
  | 'forbidden'
  | 'notFound'
  | 'unavailable'
  | 'cappedPaymentHistory'

export interface ShopOrdersScenario {
  key: ShopOrdersScenarioKey
  label: string
}

export const SHOP_ORDERS_SCENARIOS: readonly ShopOrdersScenario[] = [
  { key: 'default', label: 'فهرست کامل (پیش‌فرض)' },
  { key: 'empty', label: 'خالی' },
  { key: 'invalidFilter', label: 'فیلتر نامعتبر (400)' },
  { key: 'forbidden', label: 'بدون مجوز (403)' },
  { key: 'notFound', label: 'سفارش یافت نشد (404)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
  { key: 'cappedPaymentHistory', label: 'تاریخچه پرداخت محدودشده' },
] as const

const SCENARIO_STORAGE_KEY = 'tfOrdersScenario'

function readStoredScenarioKey(): ShopOrdersScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_ORDERS_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopOrdersScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_ORDERS_SCENARIOS[0].key
}

let activeScenarioKey: ShopOrdersScenarioKey = readStoredScenarioKey()

export function getShopOrdersScenarioKey(): ShopOrdersScenarioKey {
  return activeScenarioKey
}

export function setShopOrdersScenario(key: ShopOrdersScenarioKey): void {
  if (!SHOP_ORDERS_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop orders scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Deterministic fixture --------------------------------------------------------

/**
 * The canonical 13-character TSID-style order ids, stable and time-sortable.
 * `createdAtUtc` is derived from the same index so the sort key and the id
 * suffix agree (both descending with index).
 */
const ORDER_IDS = [
  '019c000004001', '019c000004002', '019c000004003', '019c000004004',
  '019c000004005', '019c000004006', '019c000004007', '019c000004008',
  '019c000004009', '019c000004010', '019c000004011', '019c000004012',
  '019c000004013', '019c000004014', '019c000004015', '019c000004016',
  '019c000004017', '019c000004018', '019c000004019', '019c000004020',
  '019c000004021', '019c000004022', '019c000004023',
] as const

const STATUSES: readonly AdminOrderStatus[] = ['PendingPayment', 'Paid', 'Cancelled', 'Fulfilled'] as const

const CUSTOMERS = [
  { name: 'سارا محمدی', phone: '09121112201', province: 'تهران', city: 'تهران', line: 'خیابان ولی‌عصر، پلاک ۱۰', postal: '14731' },
  { name: 'آرین رضایی', phone: '09121112202', province: 'اصفهان', city: 'اصفهان', line: 'خیابان زنده‌یاد، پلاک ۴۲', postal: '81345' },
  { name: 'نگار کریمی', phone: '09121112203', province: 'فارس', city: 'شیراز', line: 'خیابان مدرس، پلاک ۷', postal: '71392' },
  { name: 'پویا شریفی', phone: '09121112204', province: 'آلبرز', city: 'کرج', line: 'بلاوار، پلاک ۱۲۰', postal: '15841' },
  { name: 'مهسا قاسمی', phone: '09121112205', province: 'خراسان رضوی', city: 'مشهد', line: 'خیابان احمدآباد، پلاک ۳', postal: '91741' },
  { name: 'کیان نادری', phone: '09121112206', province: 'آذربایجان شرقی', city: 'تبریز', line: 'خیابان ائل‌اشرف، پلاک ۵۵', postal: '54637' },
  { name: 'لیلا احمدی', phone: '09121112207', province: 'گیلان', city: 'رشت', line: 'بلوار ملت، پلاک ۹', postal: '41488' },
  { name: 'بردیا موسوی', phone: '09121112208', province: 'اصفهان', city: 'نجف‌آباد', line: 'خیابان فردوسی، پلاک ۱۸', postal: '81720' },
  { name: 'شیرین حسینی', phone: '09121112209', province: 'تهران', city: 'تهران', line: 'خیابان انقلاب، پلاک ۳۳', postal: '13773' },
  { name: 'مهراد صادقی', phone: '09121112210', province: 'خراسان رضوی', city: 'نیشابور', line: 'خیابان امام، پلاک ۲۱', postal: '93815' },
  { name: 'آیدا فرهادی', phone: '09121112211', province: 'مازندران', city: 'ساری', line: 'خیابان شهید رجایی، پلاک ۴', postal: '47153' },
  { name: 'کیانوش توکلی', phone: '09121112212', province: 'کرمان', city: 'کرمان', line: 'خیابان بهشت، پلاک ۶۶', postal: '76134' },
] as const

const PRODUCT_SNAPSHOTS = [
  { name: 'چای سبز ارگانیک', variant: 'بسته ۱۰۰ گرم', price: 185_000 },
  { name: 'عسل کوهی طبیعی', variant: 'بشقابی ۵۰۰ گرم', price: 340_000 },
  { name: 'صابون دست‌ساز', variant: '۱۰۰ گرم', price: 95_000 },
  { name: 'روغن زیتون فرابکر', variant: 'شیشه ۵۰۰ میلی‌لیتر', price: 420_000 },
] as const

/** The newest order (index 0) is dated from the anchor; each later order is 2 days older. */
const ANCHOR_CREATED_UTC = Date.parse('2026-09-20T09:00:00.000Z')
const STEP_MS = 2 * 24 * 60 * 60 * 1000

/**
 * A full, non-paginated order record the mock seeds (not a wire type).
 * Exported so `mockShopOrderOperationsClient` (F051) can read and write the
 * SAME records — a successful status change mutates this in place and is
 * therefore visible the next time `orders.get` reads it.
 */
export interface SeedOrder {
  id: string
  orderNumber: string
  trackingCode: string
  status: AdminOrderStatus
  customerName: string
  customerPhone: string
  grandTotal: number
  createdAtUtc: string
  detail: Omit<AdminOrderDetail, 'id' | 'orderNumber' | 'trackingCode' | 'status' | 'createdAtUtc'>
}

function buildSeedOrders(): SeedOrder[] {
  const orders: SeedOrder[] = ORDER_IDS.map((id, index) => {
    const customer = CUSTOMERS[index % CUSTOMERS.length]
    const status = STATUSES[index % STATUSES.length]
    const createdAtUtc = new Date(ANCHOR_CREATED_UTC - index * STEP_MS).toISOString()
    const unitPrice = PRODUCT_SNAPSHOTS[index % PRODUCT_SNAPSHOTS.length].price
    const quantity = (index % 3) + 1
    const subTotal = unitPrice * quantity
    const shippingCost = index % 2 === 0 ? 45_000 : 0
    const discountAmount = index % 4 === 0 ? 20_000 : 0
    const grandTotal = subTotal + shippingCost - discountAmount
    const item = PRODUCT_SNAPSHOTS[index % PRODUCT_SNAPSHOTS.length]
    const detailBase = {
      customer: {
        name: customer.name,
        phone: customer.phone,
        shippingProvince: customer.province,
        shippingCity: customer.city,
        shippingAddressLine: customer.line,
        shippingPostalCode: customer.postal,
      },
      totals: { subTotal, shippingCost, discountAmount, grandTotal },
      items: [
        {
          productNameSnapshot: item.name,
          variantLabelSnapshot: item.variant,
          unitPrice,
          quantity,
        },
      ],
      version: status === 'Fulfilled' ? 2 : 1,
    }
    return {
      id,
      orderNumber: `TF-2026-${String(1001 + index)}`,
      trackingCode: `TRK-9${String(100000 + index)}`,
      status,
      customerName: customer.name,
      customerPhone: customer.phone,
      grandTotal,
      createdAtUtc,
      detail: { ...detailBase, paymentAttempts: seedAttempts(id, index, createdAtUtc, 2) },
    }
  })
  return orders
}

/** Deterministic payment attempts for a seed order (count is scenario-independent). */
function seedAttempts(orderId: string, index: number, createdAtUtc: string, count: number) {
  const base = Date.parse(createdAtUtc)
  const statuses = ['Initiated', 'Paid', 'Declined']
  return Array.from({ length: count }, (_, i) => ({
    id: `${orderId}A${String(i + 1).padStart(2, '0')}`,
    status: statuses[(index + i) % statuses.length],
    createdAtUtc: new Date(base + i * 5 * 60 * 1000).toISOString(),
  }))
}

/**
 * The `cappedPaymentHistory` scenario: the order's stored history holds 25
 * attempts, but B042 returns only the 20 newest — so the UI's capped list is
 * proven to not grow without bound.
 */
function cappedAttempts(orderId: string, createdAtUtc: string) {
  const base = Date.parse(createdAtUtc)
  return Array.from({ length: 25 }, (_, i) => ({
    id: `${orderId}C${String(i + 1).padStart(2, '0')}`,
    status: i % 3 === 0 ? 'Paid' : i % 3 === 1 ? 'Initiated' : 'Declined',
    createdAtUtc: new Date(base + i * 10 * 60 * 1000).toISOString(),
  }))
}

/** In-memory store: one order list per tenant, lazily seeded per scenario. */
const storeByTenant = new Map<string, SeedOrder[]>()
const seededFor = new Map<string, ShopOrdersScenarioKey>()

/**
 * Shared read access for the order-operations mock (F051). Resolves the
 * tenant's seeded order list (tenant-isolated, scenario-aware — same rules as
 * `getOrSeed`) and returns the mutable record for `orderId`, or `undefined`
 * when the id is malformed, missing or belongs to another tenant. The
 * operations mock mutates this record in place, so the change is visible to a
 * subsequent `orders.get` call without a second data source.
 */
export function findSeedOrder(tenantId: string, orderId: string): SeedOrder | undefined {
  const orders = getOrSeed(tenantId)
  if (isMalformedId(orderId)) return undefined
  return orders.find((order) => order.id === orderId)
}

function getOrSeed(tenantId: string): SeedOrder[] {
  // Tenant isolation: only the demo tenant is seeded. A foreign tenant scope
  // yields an empty list, so a valid-format order id there resolves to the
  // same generic `404` as a malformed or missing id.
  const orders = tenantId === DEMO_TENANT_ID
    ? (activeScenarioKey === 'empty' ? [] : buildSeedOrders())
    : []
  if (!seededFor.has(tenantId) || seededFor.get(tenantId) !== activeScenarioKey) {
    storeByTenant.set(tenantId, orders)
    seededFor.set(tenantId, activeScenarioKey)
  }
  return storeByTenant.get(tenantId) as SeedOrder[]
}

// Error helpers — every error uses the shared RFC7807 `ShopClientError` shape.

function notFoundError(): ShopClientError {
  // The non-leaking B042 detail: identical body for malformed/missing/foreign.
  return new ShopClientError({
    status: 404,
    title: 'سفارش یافت نشد.',
  })
}

function forbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'مشاهدهٔ سفارش‌ها مجاز نیست.',
    detail: 'حساب فعلی مجوز مشاهدهٔ سفارش‌های این مستأجر را ندارد.',
  })
}

function validationError(fields: Record<string, string[]>): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'مقادیر ارسالی معتبر نیست.',
    detail: 'لطفاً فیلترهای مشخص‌شده را اصلاح کنید.',
    errors: fields,
  })
}

// B042 filter rules (re-applied so an invalid filter surfaces a `400`) -----------

const MAX_Q_LENGTH = 100
const MAX_RANGE_DAYS = 366
const MS_PER_DAY = 24 * 60 * 60 * 1000

function isMalformedId(id: string): boolean {
  return id.length !== 13 || !/^[0-9a-z]+$/i.test(id)
}

/**
 * Applies B042's filter validation. Returns a `400` error naming the first
 * violated field, or `null` when every supplied filter is valid. Mirrors the
 * backend: `q` ≤ 100 chars, `status` one of the four enum values (case-
 * sensitive), `fromUtc`/`toUtc` parseable, and the range ≤ 366 days.
 */
function validateFilters(filters: AdminOrderFilters): ShopClientError | null {
  const errors: Record<string, string[]> = {}
  if (filters.q !== undefined && filters.q.trim().length > MAX_Q_LENGTH) {
    errors.q = ['برای جست‌وجو حداکثر ۱۰۰ کاراکتر وارد کنید.']
  }
  if (filters.status !== undefined && !STATUSES.includes(filters.status)) {
    errors.status = ['وضعیت سفارش معتبر نیست.']
  }
  let from: number | null = null
  let to: number | null = null
  if (filters.fromUtc !== undefined) {
    from = Date.parse(filters.fromUtc)
    if (Number.isNaN(from)) errors.fromUtc = ['تاریخ «از» معتبر نیست.']
  }
  if (filters.toUtc !== undefined) {
    to = Date.parse(filters.toUtc)
    if (Number.isNaN(to)) errors.toUtc = ['تاریخ «تا» معتبر نیست.']
  }
  if (from !== null && to !== null && to - from > MAX_RANGE_DAYS * MS_PER_DAY) {
    errors.toUtc = ['بازهٔ انتخابی نمی‌تواند بیش از ۳۶۶ روز باشد.']
  }
  return Object.keys(errors).length > 0 ? validationError(errors) : null
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

// Client -------------------------------------------------------------------------

export const mockShopOrdersClient: ShopOrdersClient = {
  async list(tenantId, filters, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    // B042 validates every list call; the `invalidFilter` scenario forces a
    // `400` (naming the first violated field, or `q` when all are valid) so
    // the validation-error state is deterministically demonstrable.
    const problem = validateFilters(filters)
    if (problem !== null) throw problem
    if (activeScenarioKey === 'invalidFilter') {
      const fields: Record<string, string[]> = { q: ['برای جست‌وجو حداکثر ۱۰۰ کاراکتر وارد کنید.'] }
      if (filters.status !== undefined) fields.status = ['وضعیت سفارش معتبر نیست.']
      if (filters.fromUtc !== undefined) fields.fromUtc = ['تاریخ «از» معتبر نیست.']
      if (filters.toUtc !== undefined) fields.toUtc = ['تاریخ «تا» معتبر نیست.']
      throw validationError(fields)
    }

    const orders = getOrSeed(tenantId)
    const q = filters.q?.trim().toLowerCase()
    const matched = orders.filter((order) => {
      if (q) {
        const haystack = `${order.orderNumber} ${order.trackingCode} ${order.customerPhone} ${order.customerName}`.toLowerCase()
        if (!haystack.includes(q)) return false
      }
      if (filters.status !== undefined && order.status !== filters.status) return false
      const created = Date.parse(order.createdAtUtc)
      if (filters.fromUtc !== undefined && !Number.isNaN(Date.parse(filters.fromUtc)) && created < Date.parse(filters.fromUtc)) return false
      if (filters.toUtc !== undefined && !Number.isNaN(Date.parse(filters.toUtc)) && created >= Date.parse(filters.toUtc)) return false
      return true
    })

    // Always `CreatedAtUtc desc, Id desc` — stable across pages.
    matched.sort((a, b) => {
      const byDate = b.createdAtUtc.localeCompare(a.createdAtUtc)
      return byDate !== 0 ? byDate : b.id.localeCompare(a.id)
    })

    const totalCount = matched.length
    const totalPages = totalCount === 0 ? 0 : Math.ceil(totalCount / filters.pageSize)
    const start = (filters.pageNumber - 1) * filters.pageSize
    const pageItems = matched.slice(start, start + filters.pageSize)
    return adminOrderListResponseSchema.parse({
      orders: pageItems.map((order) =>
        adminOrderSummarySchema.parse({
          id: order.id,
          orderNumber: order.orderNumber,
          customerName: order.customerName,
          customerPhone: order.customerPhone,
          status: order.status,
          grandTotal: order.grandTotal,
          createdAtUtc: order.createdAtUtc,
        }),
      ),
      pagination: {
        pageNumber: filters.pageNumber,
        pageSize: filters.pageSize,
        totalCount,
        totalPages,
        hasPreviousPage: filters.pageNumber > 1,
        hasNextPage: filters.pageNumber < totalPages,
      },
    })
  },

  async get(tenantId, orderId, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    if (activeScenarioKey === 'notFound' || isMalformedId(orderId)) throw notFoundError()

    const orders = getOrSeed(tenantId)
    const found = orders.find((order) => order.id === orderId)
    // Tenant isolation is the first predicate; a foreign/missing/malformed id
    // all collapse into the same generic 404 (non-leaking).
    if (found === undefined) throw notFoundError()

    // B042's temporary cap: the 20 newest, descending by createdAtUtc then id.
    let attempts =
      activeScenarioKey === 'cappedPaymentHistory'
        ? cappedAttempts(found.id, found.createdAtUtc)
        : found.detail.paymentAttempts
    attempts = [...attempts].sort((a, b) => {
      const byDate = b.createdAtUtc.localeCompare(a.createdAtUtc)
      return byDate !== 0 ? byDate : b.id.localeCompare(a.id)
    }).slice(0, 20)

    return adminOrderDetailSchema.parse({
      id: found.id,
      orderNumber: found.orderNumber,
      trackingCode: found.trackingCode,
      status: found.status,
      customer: found.detail.customer,
      totals: found.detail.totals,
      items: found.detail.items,
      paymentAttempts: attempts,
      version: found.detail.version,
      createdAtUtc: found.createdAtUtc,
    })
  },
}
