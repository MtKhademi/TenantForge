import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  paymentInitiationSchema,
  paymentResultSchema,
  resolveSandboxPaymentRequestSchema,
  type PaymentInitiation,
  type PaymentResult,
} from '../contracts/paymentLifecycleContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import type { ShopPaymentsClient } from './ShopPaymentsClient'

/**
 * S40 (B044) — deterministic mock implementation of `ShopPaymentsClient`
 * (F052). F062 swaps in `httpShopPaymentsClient` behind the same interface.
 * Scenario names live only in this file and the dev-only switcher; they never
 * reach product-facing markup.
 *
 * The mock is self-contained: it seeds its own small in-memory order/attempt
 * store for the F029 demo tenant and does NOT read F050's `SeedOrder[]`. Order
 * ids are opaque to the payment surface — a `PendingPayment` order id is all
 * the reviewer needs — so keeping the lifecycle in one file keeps the
 * attempt/token/idempotency state (the heart of B044) in a single place.
 *
 * Behavior mirrors B044 (docs/design/shop/http-contracts.md, "S40 / B044"):
 * - `initiate` is anonymous and no-body; the `Idempotency-Key` is a required
 *   UUID. The SAME key + same request replays the stored response byte-
 *   identically (no second attempt row); the SAME key + a different request is
 *   a `409 idempotency_key_conflict`; a fresh key for an order that already has
 *   a live attempt returns that attempt's stored response (one live attempt
 *   per order).
 * - only a `PendingPayment` order may start an attempt; any other status is a
 *   `409` ("order not awaiting payment"); an order past 10 attempts is a `409`
 *   `too_many_payment_attempts`.
 * - `redirectUrl` is the gateway target: the Sandbox returns a RELATIVE
 *   same-origin path `/shop/{tenantId}/bank?authority=…`; a real provider
 *   returns an ABSOLUTE https URL. The mock returns every shape B044 can
 *   return so the redirect-safety check is exercisable (see the redirect
 *   scenarios).
 * - `resultToken` is the raw callback token, returned exactly once per
 *   initiation (and re-derived, byte-identically, on a same-key replay).
 * - `getStatus` resolves purely from the opaque `token`. Every miss — blank,
 *   wrong, or for another order/tenant, or an order with no attempts — is the
 *   one identical generic `404`.
 * - `resolveSandbox` is the only browser-driven simulation (Development only)
 *   and returns the SAME `PaymentResult` shape.
 *
 * REFRESH-SAFE: the attempt/idempotency state is persisted to sessionStorage
 * (the same store the scenario key uses), so a reload of the result page in
 * the same tab still resolves the status from the opaque token — exactly what
 * "refreshing after the redirect still shows the correct state" requires.
 *
 * The mock is deterministic: the same input always yields the same output.
 * Latency is simulated through the shared abort-aware `delay` helper, so a
 * superseded/aborted request rejects with `AbortError` instead of resolving
 * into stale UI.
 */

const READ_LATENCY_MS = 450

/**
 * The only tenant the mock seeds for: the F029 demo boutique (the same id the
 * orders/discovery/profile mocks use). Any other tenant scope finds no order,
 * collapsing into the SAME non-leaking `404` as a malformed or missing id
 * (B044's tenant-isolation rule).
 */
export const PAYMENT_DEMO_TENANT_ID = '0RN590ZYXKNZ2'

/**
 * The demo order id the redirect page defaults to when `?order` is absent.
 * It is a single seed constant (not a data blob) so a reviewer can open the
 * redirect page directly; the URL still overrides it.
 */
export const PAYMENT_DEMO_ORDER_ID = '019c00a000001'

// Scenario definitions ---------------------------------------------------------

export type PaymentScenarioKey =
  | 'default'
  | 'zarinpal'
  | 'redirectHttp'
  | 'redirectUnexpectedHost'
  | 'redirectProtocolRelative'
  | 'conflict'
  | 'tooMany'
  | 'invalidKey'
  | 'notFound'
  | 'forbidden'
  | 'unavailable'
  | 'pending'
  | 'polling'
  | 'paid'
  | 'fulfilled'
  | 'cancelled'

export interface PaymentScenario {
  key: PaymentScenarioKey
  label: string
}

export const PAYMENT_SCENARIOS: readonly PaymentScenario[] = [
  { key: 'default', label: 'Sandbox approval (پیش‌فرض)' },
  { key: 'zarinpal', label: 'ZarinPal (https)' },
  { key: 'redirectHttp', label: 'redirect: http (نامعتبر)' },
  { key: 'redirectUnexpectedHost', label: 'redirect: host غیرمجاز (نامعتبر)' },
  { key: 'redirectProtocolRelative', label: 'redirect: //host (نامعتبر)' },
  { key: 'conflict', label: 'در انتظار پرداخت نیست (409)' },
  { key: 'tooMany', label: 'بیش از حد تلاش (409)' },
  { key: 'invalidKey', label: 'کلید نامعتبر (400)' },
  { key: 'notFound', label: 'سفارش/توکن یافت نشد (404)' },
  { key: 'forbidden', label: 'بدون مجوز (403)' },
  { key: 'unavailable', label: 'اتصال در دسترس نیست' },
  { key: 'pending', label: 'نتیجه: در انتظار' },
  { key: 'polling', label: 'نتیجه: انتظار سپس پرداخت' },
  { key: 'paid', label: 'نتیجه: پرداخت‌شده' },
  { key: 'fulfilled', label: 'نتیجه: تحویل‌شده' },
  { key: 'cancelled', label: 'نتیجه: لغو‌شده' },
] as const

const SCENARIO_STORAGE_KEY = 'tfPaymentsScenario'

function readStoredScenarioKey(): PaymentScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(SCENARIO_STORAGE_KEY)
    if (PAYMENT_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as PaymentScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return PAYMENT_SCENARIOS[0].key
}

let activeScenarioKey: PaymentScenarioKey = readStoredScenarioKey()

export function getPaymentsScenarioKey(): PaymentScenarioKey {
  return activeScenarioKey
}

export function setPaymentsScenario(key: PaymentScenarioKey): void {
  if (!PAYMENT_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown payments scenario: ${key}`)
  }
  activeScenarioKey = key
  try {
    window.sessionStorage.setItem(SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Deterministic fixture --------------------------------------------------------

/** A seed order the payment mock owns (not a wire type). */
interface SeedPaymentOrder {
  id: string
  orderNumber: string
  status: PaymentResult['status']
}

const SEED_ORDERS: readonly SeedPaymentOrder[] = [
  { id: '019c00a000001', orderNumber: 'TF-2026-2001', status: 'PendingPayment' },
  { id: '019c00a000002', orderNumber: 'TF-2026-2002', status: 'Paid' },
  { id: '019c00a000003', orderNumber: 'TF-2026-2003', status: 'Cancelled' },
  { id: '019c00a000004', orderNumber: 'TF-2026-2004', status: 'Fulfilled' },
]

/** The order the `conflict` scenario targets: an already-Paid, not-payable order. */
const CONFLICT_ORDER_ID = '019c00a000002'

// Persistent in-memory store (survives a same-tab reload) --------------------

interface Attempt {
  orderNumber: string
  status: PaymentResult['status']
  providerReference: string | null
  tokenHash: string
  /** The idempotency key that minted this attempt. */
  key: string
  fingerprint: string
  /** The initiation response to replay on a same-key/fresh-key re-initiation. */
  initiation: PaymentInitiation
}

interface MockStore {
  attempts: Record<string, Attempt>
  /** Per-order getStatus call counter so the `polling` scenario can converge. */
  pollCounts: Record<string, number>
}

const STORE_KEY = 'tfPaymentsMockStore'

function defaultStore(): MockStore {
  return { attempts: {}, pollCounts: {} }
}

function loadStore(): MockStore {
  try {
    const raw = window.sessionStorage.getItem(STORE_KEY)
    if (!raw) return defaultStore()
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null) return defaultStore()
    const rec = parsed as { attempts?: unknown; pollCounts?: unknown }
    return {
      attempts:
        typeof rec.attempts === 'object' && rec.attempts !== null
          ? (rec.attempts as Record<string, Attempt>)
          : {},
      pollCounts:
        typeof rec.pollCounts === 'object' && rec.pollCounts !== null
          ? (rec.pollCounts as Record<string, number>)
          : {},
    }
  } catch {
    return defaultStore()
  }
}

let store: MockStore = loadStore()

function saveStore(): void {
  try {
    window.sessionStorage.setItem(STORE_KEY, JSON.stringify(store))
  } catch {
    // Best-effort: the in-memory store still drives this page lifetime.
  }
}

function orderKey(tenantId: string, orderId: string): string {
  return `${tenantId}:${orderId}`
}

function findSeedOrder(tenantId: string, orderId: string): SeedPaymentOrder | undefined {
  if (tenantId !== PAYMENT_DEMO_TENANT_ID) return undefined
  if (orderId.length !== 13) return undefined
  return SEED_ORDERS.find((order) => order.id === orderId)
}

// Deterministic token / fingerprint -------------------------------------------

/**
 * Deterministic token derived from its seed parts (never stored raw; only its
 * hash is). The same inputs always produce the same token, so a same-key
 * replay re-derives it byte-identically — exactly B044's rule.
 */
function deriveToken(tenantId: string, orderId: string, key: string): string {
  let h1 = 0x811c9dc5
  let h2 = 0x01000193
  const input = `${tenantId}|${orderId}|${key}`
  for (let i = 0; i < input.length; i += 1) {
    const c = input.charCodeAt(i)
    h1 = Math.imul(h1 ^ c, 0x01000193)
    h2 = (Math.imul(h2, 0x5bd1e995) + c) | 0
  }
  let a = h1 >>> 0
  let b = h2 >>> 0
  let out = ''
  while (out.length < 40) {
    a = Math.imul(a ^ (a >>> 15), 0x2c1b3c6d)
    b = Math.imul(b ^ (b >>> 13), 0x297a2d39)
    out += ((a ^ b) & 0x1f).toString(16)
  }
  return out.slice(0, 40)
}

function hashToken(value: string): string {
  let h = 0x811c9dc5
  for (let i = 0; i < value.length; i += 1) {
    h = Math.imul(h ^ value.charCodeAt(i), 0x01000193)
  }
  return (h >>> 0).toString(16)
}

/** B044's canonical request fingerprint: the tenant + order ids. */
function fingerprint(tenantId: string, orderId: string): string {
  return hashToken(`${tenantId}|${orderId}`)
}

/**
 * The B044 result status a forced result scenario reports (`undefined` when the
 * scenario is not a forced result). Mapping to the real enum values — not the
 * scenario key — keeps the `paymentResultSchema` parse valid.
 */
function forcedStatusFor(key: PaymentScenarioKey): PaymentResult['status'] | undefined {
  switch (key) {
    case 'pending':
      return 'PendingPayment'
    case 'paid':
      return 'Paid'
    case 'fulfilled':
      return 'Fulfilled'
    case 'cancelled':
      return 'Cancelled'
    default:
      return undefined
  }
}

// Error helpers — every error uses the shared RFC 7807 `ShopClientError`.

function notFoundError(): ShopClientError {
  // The ONE non-leaking B044 detail: malformed/missing/foreign/wrong-token all
  // 404 alike. Identical for an unknown order and a mismatched token.
  return new ShopClientError({ status: 404, title: 'پرداخت یافت نشد.' })
}

function forbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'پرداخت مجاز نیست.',
    detail: 'حساب فعلی اجازهٔ پرداخت این سفارش را ندارد.',
  })
}

function idempotencyKeyValidationError(): ShopClientError {
  return new ShopClientError({
    status: 400,
    title: 'مقادیر ارسالی معتبر نیست.',
    detail: 'کلید توان‌مندی لازم است.',
    errors: { 'Idempotency-Key': ['The Idempotency-Key header is required.'] },
  })
}

function notAwaitingPaymentError(status: string): ShopClientError {
  return new ShopClientError({
    status: 409,
    title: 'سفارش در انتظار پرداخت نیست.',
    detail: `وضعیت فعلی سفارش «${status}» است و دیگر قابل پرداخت نیست.`,
  })
}

function tooManyAttemptsError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'too_many_payment_attempts',
    title: 'بیش از حد تلاش پرداخت',
    detail: 'این سفارش به حد مجاز تلاش پرداخت رسیده است.',
  })
}

function idempotencyConflictError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'idempotency_key_conflict',
    title: 'تعارض کلید توان‌مندی',
    detail: 'این کلید توان‌مندی قبلاً برای درخواستی دیگر استفاده شده است.',
  })
}

function orderNotPayableError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    title: 'سفارش قبلا‌ حل‌شده است.',
    detail: 'این سفارش دیگر قابل پرداخت نیست.',
  })
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

// Redirect construction (every B044 shape) ------------------------------------

/**
 * Builds the `PaymentInitiation` for a scenario. The Sandbox returns a RELATIVE
 * same-origin path; ZarinPal returns an absolute https URL. The three unsafe
 * scenarios return the malformed shapes the redirect-safety check must reject —
 * an absolute `http://` URL, an absolute URL on an unexpected host, and a
 * protocol-relative `//host/path`.
 */
function buildInitiation(
  tenantId: string,
  orderId: string,
  key: string,
  scenario: PaymentScenarioKey,
): PaymentInitiation {
  const token = deriveToken(tenantId, orderId, key)
  const authority = `auth-${hashToken(token).slice(0, 12)}`

  let provider: PaymentInitiation['provider'] = 'Sandbox'
  let redirectUrl = `/shop/${tenantId}/bank?authority=${authority}`

  switch (scenario) {
    case 'zarinpal':
      provider = 'ZarinPal'
      redirectUrl = `https://checkout.zarinpal.com/payment?ref=${authority}`
      break
    case 'redirectHttp':
      redirectUrl = `http://checkout.zarinpal.com/payment?ref=${authority}`
      break
    case 'redirectUnexpectedHost':
      provider = 'ZarinPal'
      redirectUrl = `https://evil.example.com/pay?ref=${authority}`
      break
    case 'redirectProtocolRelative':
      redirectUrl = `//evil.example.com/pay?ref=${authority}`
      break
    default:
      break
  }

  return paymentInitiationSchema.parse({ provider, redirectUrl, resultToken: token })
}

// Client -----------------------------------------------------------------------

export const mockShopPaymentsClient: ShopPaymentsClient = {
  async initiate(tenantId, orderId, idempotencyKey, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    // The `notFound` scenario forces the non-leaking 404 (unknown order) on
    // initiate, mirroring the same generic 404 getStatus returns for a miss.
    if (activeScenarioKey === 'notFound') throw notFoundError()

    // B044: the Idempotency-Key is required. The `invalidKey` scenario (or a
    // genuinely missing key) is a `400` naming the header, before any state.
    if (activeScenarioKey === 'invalidKey' || idempotencyKey.trim().length === 0) {
      throw idempotencyKeyValidationError()
    }

    // B044's status-gate: only a PendingPayment order may start an attempt.
    // The `conflict` scenario targets an already-Paid order to force it.
    const order =
      activeScenarioKey === 'conflict'
        ? SEED_ORDERS.find((o) => o.id === CONFLICT_ORDER_ID)
        : findSeedOrder(tenantId, orderId)
    if (order === undefined) throw notFoundError()
    if (activeScenarioKey === 'conflict') throw notAwaitingPaymentError(order.status)

    const oKey = orderKey(tenantId, orderId)
    const fp = fingerprint(tenantId, orderId)
    const existing = store.attempts[oKey]

    // Idempotency, checked before any attempt is minted.
    if (existing !== undefined) {
      if (existing.key === idempotencyKey) {
        if (existing.fingerprint !== fp) throw idempotencyConflictError()
        return existing.initiation // same key + same request → replay (200)
      }
      // A fresh key for an order with a live attempt → that attempt's response.
      return existing.initiation
    }

    // The 10-attempt cap: forced by the `tooMany` scenario in the mock.
    if (activeScenarioKey === 'tooMany') throw tooManyAttemptsError()

    const initiation = buildInitiation(tenantId, orderId, idempotencyKey, activeScenarioKey)
    store.attempts[oKey] = {
      orderNumber: order.orderNumber,
      status: 'PendingPayment',
      providerReference: null,
      tokenHash: hashToken(initiation.resultToken),
      key: idempotencyKey,
      fingerprint: fp,
      initiation,
    }
    saveStore()
    return initiation
  },

  async getStatus(tenantId, orderId, token, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()
    if (activeScenarioKey === 'forbidden') throw forbiddenError()
    // Read the active scenario once (full union type) so the checks below are
    // not narrowed by control-flow analysis of the shared module-level variable.
    const scenario: PaymentScenarioKey = activeScenarioKey
    // Unknown order (malformed / missing / foreign-tenant) is always a 404.
    const order = findSeedOrder(tenantId, orderId)
    if (order === undefined) throw notFoundError()

    // Forced result scenarios: the scenario IS the server's answer, independent
    // of the token — so a reviewer can open the result page directly (without a
    // token in the URL) and still see the state. The status is the B044 enum
    // value (not the scenario key), so the schema parse succeeds.
    const forcedStatus = forcedStatusFor(scenario)
    if (forcedStatus !== undefined) {
      const reference = scenario === 'paid' ? 'ZAR-1000' : scenario === 'fulfilled' ? 'ZAR-1001' : null
      return paymentResultSchema.parse({
        orderNumber: order.orderNumber,
        status: forcedStatus,
        providerReference: reference,
      })
    }
    // `notFound` forces the generic 404. The "empty" case (an order with no
    // recorded attempts) is NOT a separate scenario: under the default scenario
    // it naturally produces the SAME non-leaking 404, which is exactly B044's
    // behavior (the status route has no "successful empty" shape).
    if (scenario === 'notFound') throw notFoundError()

    // `polling` and realistic resolution both require a real token. A blank
    // token is the identical generic 404 (B044's non-leaking rule).
    if (token.trim().length === 0) throw notFoundError()

    if (scenario === 'polling') {
      const oKey = orderKey(tenantId, orderId)
      const calls = (store.pollCounts[oKey] ?? 0) + 1
      store.pollCounts[oKey] = calls
      saveStore()
      return paymentResultSchema.parse({
        orderNumber: order.orderNumber,
        status: calls <= 2 ? 'PendingPayment' : 'Paid',
        providerReference: calls <= 2 ? null : `ZAR-${calls}`,
      })
    }

    // Realistic (default): resolve from the persisted attempt by token hash. A
    // wrong token (or an order with no recorded attempt) is the same 404.
    const attempt = store.attempts[orderKey(tenantId, orderId)]
    if (attempt === undefined || hashToken(token) !== attempt.tokenHash) throw notFoundError()

    return paymentResultSchema.parse({
      orderNumber: attempt.orderNumber,
      status: attempt.status,
      providerReference: attempt.providerReference,
    })
  },

  async resolveSandbox(tenantId, orderId, authority, approved, signal) {
    await delay(READ_LATENCY_MS, signal)
    assertNotAborted(signal)

    if (activeScenarioKey === 'unavailable') throw new ApiUnavailableError()

    // B044: a blank authority is a `400` naming the field.
    const body = resolveSandboxPaymentRequestSchema.parse({ authority, approved })
    if (body.authority === null || body.authority.trim().length === 0) {
      throw new ShopClientError({
        status: 400,
        title: 'مقادیر ارسالی معتبر نیست.',
        detail: 'معرّف پرداخت (authority) لازم است.',
        errors: { authority: ['authority is required.'] },
      })
    }

    const order = findSeedOrder(tenantId, orderId)
    if (order === undefined) throw notFoundError()

    // A success for an order that is no longer payable is a 409.
    if (activeScenarioKey === 'conflict') throw orderNotPayableError()
    if (activeScenarioKey === 'tooMany') throw tooManyAttemptsError()

    const oKey = orderKey(tenantId, orderId)
    const attempt = store.attempts[oKey]
    if (attempt === undefined) throw notFoundError()

    // Approved moves the attempt to Paid (a provider reference is minted);
    // declined leaves it PendingPayment with no reference. The server stays
    // authoritative: `approved` is a hint, never a command.
    const nextStatus: PaymentResult['status'] = approved ? 'Paid' : 'PendingPayment'
    const nextReference = approved ? `ZAR-${hashToken(body.authority).slice(0, 6)}` : null
    store.attempts[oKey] = { ...attempt, status: nextStatus, providerReference: nextReference }
    saveStore()

    return paymentResultSchema.parse({
      orderNumber: attempt.orderNumber,
      status: nextStatus,
      providerReference: nextReference,
    })
  },
}
