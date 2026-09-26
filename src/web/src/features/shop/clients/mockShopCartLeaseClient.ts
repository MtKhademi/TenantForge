import { ApiUnavailableError } from '@/features/auth/authTypes'
import { getOrCreateCartId } from '../cartStorage'
import {
  cartExpiredProblemSchema,
  cartItemSchema,
  cartResponseSchema,
  CartLeaseExpired,
  type CartItem,
  type CartResponse,
} from '../contracts/cartLeaseContract'
import { ShopClientError } from '../contracts/shopContract'
import { assertNotRateLimited } from '../rateLimitScenario'
import { delay } from './shopFetch'
import type { ShopCartLeaseClient } from './ShopCartLeaseClient'

/**
 * S36 cart reservation lease — deterministic mock implementation of
 * `ShopCartLeaseClient` (F048). F058 swaps in `httpShopCartLeaseClient`
 * behind the same interface. Scenario names live only in this file and the
 * dev-only switcher; they never reach product-facing markup.
 *
 * Behavior mirrors B040 (docs/design/shop/http-contracts.md, "S36 / B040"):
 * - a plain `getCart` NEVER extends the lease;
 * - a successful add/update/remove extends it to a fresh lease window;
 * - an expired lease answers RFC 7807 `410` with `type: 'shop_cart_expired'`
 *   (thrown as `CartLeaseExpired` — the ONE trigger of the UI's recovery
 *   flow on cart, checkout and review);
 * - a cart or item that does not exist is a plain `404` (`ShopClientError`)
 *   and must NOT be treated as expiry;
 * - a network failure throws `ApiUnavailableError` (the retry state).
 *
 * In-memory state: one cart per tenant. The FIRST stored cart a tenant
 * contacts is seeded with a small canned item list (deterministic) so the
 * countdown and the mutations have something to show; every cart created
 * afterwards (a recovery "start over" after expiry) is deliberately EMPTY —
 * the mock never silently rebuilds a cart the shopper gave up. A cart id
 * different from the tenant's stored one is a fresh empty cart only when the
 * stored one is already expired; while the stored cart is still live it is an
 * unknown id → plain 404. Reloads re-seed the store. Latency is simulated
 * through the shared abort-aware `delay` helper, so a superseded request
 * rejects with `AbortError` instead of resolving into stale UI.
 */

const READ_LATENCY_MS = 500
const WRITE_LATENCY_MS = 600

/** The quantity window the backend enforces on add/update. */
const MIN_QUANTITY = 1
const MAX_QUANTITY = 99

// Scenario definitions ---------------------------------------------------------

export type ShopCartLeaseScenarioKey =
  | 'shortLease'
  | 'normalLease'
  | 'expired'
  | 'notFound'
  | 'unavailable'

export interface ShopCartLeaseScenario {
  key: ShopCartLeaseScenarioKey
  label: string
}

export const SHOP_CART_LEASE_SCENARIOS: readonly ShopCartLeaseScenario[] = [
  { key: 'shortLease', label: 'اجاره‌ی کوتاه (۳۰ ثانیه)' },
  { key: 'normalLease', label: 'اجاره‌ی معمول (۲۰ دقیقه)' },
  { key: 'expired', label: 'منقضی‌شده (410)' },
  { key: 'notFound', label: 'یافت نشد (404)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
] as const

const SCENARIO_STORAGE_KEY = 'tfCartLeaseScenario'

function readStoredScenarioKey(): ShopCartLeaseScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (SHOP_CART_LEASE_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as ShopCartLeaseScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return SHOP_CART_LEASE_SCENARIOS[0].key
}

let activeScenarioKey: ShopCartLeaseScenarioKey = readStoredScenarioKey()

export function getShopCartLeaseScenarioKey(): ShopCartLeaseScenarioKey {
  return activeScenarioKey
}

export function setShopCartLeaseScenario(key: ShopCartLeaseScenarioKey): void {
  if (!SHOP_CART_LEASE_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown shop cart-lease scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

/**
 * Lease window per scenario. `expired` seeds a lease that ended 60s ago, so
 * the very first read of the stored cart already 410s (demo-friendly).
 */
const LEASE_MS: Record<ShopCartLeaseScenarioKey, number> = {
  shortLease: 30_000,
  normalLease: 1_200_000,
  expired: -60_000,
  notFound: 30_000,
  unavailable: 30_000,
}

// In-memory store ---------------------------------------------------------------

interface StoredCart {
  cartId: string
  items: CartItem[]
  subTotal: number
  expiresAtUtc: string
}

/**
 * Canonical 13-character TSID-style ids, stable and time-sortable. Canned,
 * deterministic values — the mock never reads the clock for identifiers.
 */
const SEED_ITEM_A_ID = '019c000002101'
const SEED_ITEM_B_ID = '019c000002102'
let mintedIdSequence = 0

/** Mints the next deterministic TSID-style id (item). */
function mintItemId(): string {
  mintedIdSequence += 1
  const body = String(mintedIdSequence).padStart(3, '0')
  return `019c000020${body}`
}

function freshLeaseEndUtc(fromMs: number): string {
  return new Date(fromMs + LEASE_MS[activeScenarioKey]).toISOString()
}

/** The deterministic seed a tenant's FIRST cart starts from. */
function seedCart(cartId: string, fromMs: number): StoredCart {
  const items = [
    cartItemSchema.parse({
      id: SEED_ITEM_A_ID,
      productVariantId: '019c000002103',
      productName: 'کفش پیاده‌روی شهری نور',
      variantLabel: 'رنگ: کهربایی — سایز ۴۲',
      quantity: 1,
      unitPrice: 890_000,
    }),
    cartItemSchema.parse({
      id: SEED_ITEM_B_ID,
      productVariantId: '019c000002104',
      productName: 'کیف لپ‌تاپ ۱۵ اینچ',
      variantLabel: 'رنگ: خاکستری تیره',
      quantity: 2,
      unitPrice: 350_000,
    }),
  ]
  return {
    cartId,
    items,
    subTotal: items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0),
    expiresAtUtc: freshLeaseEndUtc(fromMs),
  }
}

/** A cart created AFTER an expiry is empty — never a silent rebuild. */
function emptyCart(cartId: string, fromMs: number): StoredCart {
  return {
    cartId,
    items: [],
    subTotal: 0,
    expiresAtUtc: freshLeaseEndUtc(fromMs),
  }
}

/** One cart per tenant; `firstCartSeen` marks the seeded one. */
const cartsByTenant = new Map<string, StoredCart>()
const firstCartSeen = new Set<string>()

function isExpired(cart: StoredCart): boolean {
  return Date.parse(cart.expiresAtUtc) <= Date.now()
}

function recomputeSubTotal(cart: StoredCart): void {
  cart.subTotal = cart.items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0)
}

/**
 * Resolves the cart a call refers to. A tenant's first contact seeds the
 * canned cart; a foreign id after an expiry starts a fresh empty cart (the
 * recovery "start over"); a foreign id while the stored cart is live is a
 * plain, non-leaking 404.
 */
function resolveCart(tenantId: string, cartId: string): StoredCart {
  const stored = cartsByTenant.get(tenantId)
  if (stored === undefined || !firstCartSeen.has(tenantId)) {
    const seeded = seedCart(cartId, Date.now())
    cartsByTenant.set(tenantId, seeded)
    firstCartSeen.add(tenantId)
    return seeded
  }
  if (stored.cartId !== cartId) {
    if (isExpired(stored)) {
      const fresh = emptyCart(cartId, Date.now())
      cartsByTenant.set(tenantId, fresh)
      return fresh
    }
    throw notFoundError()
  }
  return stored
}

/** Read path: 410 when the lease is up, 404 for an unknown id. */
function readCart(tenantId: string, cartId: string): StoredCart {
  const cart = resolveCart(tenantId, cartId)
  if (isExpired(cart)) throw cartExpiredError()
  return cart
}

/**
 * Mutation path: resolves the tenant's STORED cart id (mirroring the real
 * adapter's `ensureCartId`) so the interface keeps `cartAdapter`'s
 * parameter lists verbatim.
 */
function mutationCart(tenantId: string): StoredCart {
  const cartId = getOrCreateCartId(tenantId)
  return readCart(tenantId, cartId)
}

// Error helpers — every error uses the shared RFC7807 shape.

function cartExpiredError(): CartLeaseExpired {
  const problem = cartExpiredProblemSchema.parse({
    status: 410,
    type: 'shop_cart_expired',
    title: 'رزرو سبد خرید منقضی شد.',
    detail: 'زمان رزرو سبد خرید شما به پایان رسیده است و موجودی رها شده است.',
  })
  return new CartLeaseExpired(problem)
}

function notFoundError(): ShopClientError {
  return new ShopClientError({
    status: 404,
    title: 'سبد خرید یا آیتم یافت نشد.',
  })
}

function invalidQuantityError(): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'تعداد معتبر نیست.',
    detail: `تعداد باید عدد صحیحی بین ${MIN_QUANTITY} تا ${MAX_QUANTITY} باشد.`,
    errors: { quantity: ['تعداد باید عدد صحیحی بین ۱ تا ۹۹ باشد.'] },
  })
}

function assertValidQuantity(quantity: number): void {
  if (!Number.isInteger(quantity) || quantity < MIN_QUANTITY || quantity > MAX_QUANTITY) {
    throw invalidQuantityError()
  }
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

function toResponse(cart: StoredCart): CartResponse {
  return cartResponseSchema.parse({
    cartId: cart.cartId,
    items: cart.items.map((item) => ({ ...item })),
    subTotal: cart.subTotal,
    expiresAtUtc: cart.expiresAtUtc,
  })
}

function extendLease(cart: StoredCart): void {
  // B040: a successful mutation is the ONLY thing that pushes the lease out.
  cart.expiresAtUtc = freshLeaseEndUtc(Date.now())
}

// Client -------------------------------------------------------------------------

export const mockShopCartLeaseClient: ShopCartLeaseClient = {
  async getCart(tenantId, cartId, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)
    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'notFound') throw notFoundError()

    const cart = resolveCart(tenantId, cartId)
    if (isExpired(cart)) throw cartExpiredError()
    // NOTE: deliberately no extendLease() here — a GET never extends.
    return toResponse(cart)
  },

  async addItem(tenantId, productVariantId, quantity, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    // S42/B046: a dev rate-limit scenario on `cart` rejects the mutation with the
    // one generic 429, independent of the lease scenario (dev-only; a no-op in prod).
    assertNotRateLimited('cart')
    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'notFound') throw notFoundError()

    const cart = mutationCart(tenantId)
    assertValidQuantity(quantity)

    const existing = cart.items.find((item) => item.productVariantId === productVariantId)
    if (existing) {
      existing.quantity = quantity
    } else {
      cart.items.push(
        cartItemSchema.parse({
          id: mintItemId(),
          productVariantId,
          productName: 'محصول انتخابی',
          variantLabel: 'متغیر پیش‌فرض',
          quantity,
          unitPrice: 250_000,
        }),
      )
    }
    recomputeSubTotal(cart)
    extendLease(cart)
    return toResponse(cart)
  },

  async updateItem(tenantId, itemId, quantity, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    // S42/B046: the `cart` rate-limit scenario rejects this mutation with the 429.
    assertNotRateLimited('cart')
    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'notFound') throw notFoundError()

    const cart = mutationCart(tenantId)
    assertValidQuantity(quantity)
    const item = cart.items.find((candidate) => candidate.id === itemId)
    if (!item) throw notFoundError()
    item.quantity = quantity
    recomputeSubTotal(cart)
    extendLease(cart)
    return toResponse(cart)
  },

  async removeItem(tenantId, itemId, signal) {
    await delay(WRITE_LATENCY_MS, signal)
    assertNotAborted(signal)
    // S42/B046: the `cart` rate-limit scenario rejects this mutation with the 429.
    assertNotRateLimited('cart')
    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'notFound') throw notFoundError()

    const cart = mutationCart(tenantId)
    const index = cart.items.findIndex((candidate) => candidate.id === itemId)
    if (index === -1) throw notFoundError()
    cart.items.splice(index, 1)
    recomputeSubTotal(cart)
    extendLease(cart)
    return toResponse(cart)
  },
}
