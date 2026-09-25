import { ApiUnavailableError } from '@/features/auth/authTypes'
import { adminOrderDetailSchema, type AdminOrderDetail } from '../contracts/adminOrdersContract'
import type { OrderStatusAction } from '../contracts/orderOperationsContract'
import { ShopClientError } from '../contracts/shopContract'
import { delay } from './shopFetch'
import { findSeedOrder, getShopOrdersScenarioKey, type SeedOrder } from './mockShopOrdersClient'
import type { ShopOrderOperationsClient } from './ShopOrderOperationsClient'

/**
 * S39 (B043) — deterministic mock implementation of `ShopOrderOperationsClient`
 * (F051). F061 swaps in `httpShopOrderOperationsClient` behind the same
 * interface. Scenario names live only in this file and the dev-only switcher;
 * they never reach product-facing markup.
 *
 * Reads and writes the SAME in-memory order records F050's
 * `mockShopOrdersClient` seeds (via `findSeedOrder`) — a successful transition
 * mutates the record in place, so the new status is visible the next time
 * `orders.get` reads it. There is no second, separate order data source, and
 * the `orders` slot is untouched.
 *
 * Behavior mirrors B043 (docs/design/shop/http-contracts.md, "S39 / B043"):
 * - only two legal transitions exist: `Paid -> Fulfilled` (Fulfill) and
 *   `PendingPayment -> Cancelled` (Cancel); a success stamps the status, bumps
 *   `version` and returns the committed `AdminOrderDetail` (B042's shape);
 * - a malformed, missing or other-tenant order id is the SAME non-leaking `404`;
 * - a member without `Shop.Orders.Manage` is a `403`;
 * - a valid transition whose `expectedVersion` no longer matches the stored
 *   version is a `409` `stale_version`; any other requested transition (e.g.
 *   fulfilling an already-cancelled order) is a `409` `invalid_order_transition`;
 * - the `Idempotency-Key` tag is per-attempt: re-sending the SAME key with the
 *   SAME action replays the stored response (a `200`, no second mutation);
 *   re-sending the SAME key with a DIFFERENT action is a `409`
 *   `idempotency_key_conflict` (neither action is performed).
 *
 * Read-coherent transport/permission states: the mock honors the ORDERS
 * scenario key (F050) so the shared `unavailable` scenario throws
 * `ApiUnavailableError` (retryable) and the `forbidden` scenario throws a
 * manage-specific `403` — the connection/user state is shared by every Shop
 * read AND write. The mutation-specific outcomes (stale version, invalid
 * transition) are driven by this mock's own scenario key.
 *
 * The mock is deterministic: the same input always yields the same output.
 * Latency is simulated through the shared abort-aware `delay` helper, so a
 * superseded/aborted mutation rejects with `AbortError` instead of landing
 * stale state.
 */

const MUTATION_LATENCY_MS = 500

// Scenario definitions ---------------------------------------------------------

export type OrderOperationsScenarioKey = 'default' | 'staleVersion' | 'invalidTransition'

export interface OrderOperationsScenario {
  key: OrderOperationsScenarioKey
  label: string
}

export const ORDER_OPERATIONS_SCENARIOS: readonly OrderOperationsScenario[] = [
  { key: 'default', label: 'عملیات موفق (پیش‌فرض)' },
  { key: 'staleVersion', label: 'نسخهٔ منقضی‌شده (409)' },
  { key: 'invalidTransition', label: 'گذار نامعتبر (409)' },
] as const

const OP_SCENARIO_STORAGE_KEY = 'tfOrderOperationsScenario'

function readStoredOpScenarioKey(): OrderOperationsScenarioKey {
  try {
    const stored = window.sessionStorage.getItem(OP_SCENARIO_STORAGE_KEY)
    if (ORDER_OPERATIONS_SCENARIOS.some((entry) => entry.key === stored)) {
      return stored as OrderOperationsScenarioKey
    }
  } catch {
    // Storage can be unavailable; the default scenario wins.
  }
  return ORDER_OPERATIONS_SCENARIOS[0].key
}

let activeOpScenarioKey: OrderOperationsScenarioKey = readStoredOpScenarioKey()

export function getOrderOperationsScenarioKey(): OrderOperationsScenarioKey {
  return activeOpScenarioKey
}

export function setOrderOperationsScenario(key: OrderOperationsScenarioKey): void {
  if (!ORDER_OPERATIONS_SCENARIOS.some((entry) => entry.key === key)) {
    throw new Error(`Unknown order operations scenario: ${key}`)
  }
  activeOpScenarioKey = key
  try {
    window.sessionStorage.setItem(OP_SCENARIO_STORAGE_KEY, key)
  } catch {
    // Non-fatal: the scenario still applies for this page lifetime.
  }
}

// Idempotency store -------------------------------------------------------------

/** One persisted operation row, keyed by (tenant, idempotency-key). */
interface StoredOperation {
  action: OrderStatusAction
  response: AdminOrderDetail
}

/**
 * The mock's stand-in for the backend's `OrderOperations` table. A key's first
 * successful call stores its action and response snapshot; a later same-key /
 * same-action call replays that snapshot, and a same-key / different-action
 * call is a conflict. Unique per (tenant, key).
 */
const operationLog = new Map<string, StoredOperation>()

function operationLogKey(tenantId: string, idempotencyKey: string): string {
  return `${tenantId}:${idempotencyKey}`
}

// Wire + error helpers ----------------------------------------------------------

/** Builds the committed B042 admin-order detail from a (mutated) seed record. */
function buildDetail(order: SeedOrder): AdminOrderDetail {
  return adminOrderDetailSchema.parse({
    id: order.id,
    orderNumber: order.orderNumber,
    trackingCode: order.trackingCode,
    status: order.status,
    customer: order.detail.customer,
    totals: order.detail.totals,
    items: order.detail.items,
    paymentAttempts: order.detail.paymentAttempts,
    version: order.detail.version,
    createdAtUtc: order.createdAtUtc,
  })
}

function notFoundError(): ShopClientError {
  // The same non-leaking B042 detail: malformed/missing/foreign all 404 alike.
  return new ShopClientError({
    status: 404,
    title: 'سفارش یافت نشد.',
  })
}

function manageForbiddenError(): ShopClientError {
  return new ShopClientError({
    status: 403,
    title: 'تغییر وضعیت سفارش مجاز نیست.',
    detail: 'حساب فعلی مجوز مدیریت وضعیت سفارش‌های این مستأجر را ندارد.',
  })
}

function staleVersionError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'stale_version',
    title: 'تعارض نسخهٔ سفارش',
    detail: 'پیش از اعمال این درخواست، سفارش تغییر کرده است. صفحه را به‌روزرسانی کنید و دوباره تلاش کنید.',
  })
}

function invalidTransitionError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'invalid_order_transition',
    title: 'گذار نامعتبر سفارش',
    detail: 'این سفارش نمی‌تواند از وضعیت فعلی‌اش با عملیات درخواستی تغییر کند.',
  })
}

function idempotencyConflictError(): ShopClientError {
  return new ShopClientError({
    status: 409,
    type: 'idempotency_key_conflict',
    title: 'تعارض کلید توان‌مندی',
    detail: 'این کلید توان‌مندی قبلاً برای عملیاتی دیگر استفاده شده است.',
  })
}

function assertNotAborted(signal: AbortSignal | undefined): void {
  if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
}

// Client -------------------------------------------------------------------------

export const mockShopOrderOperationsClient: ShopOrderOperationsClient = {
  async changeStatus(tenantId, orderId, body, idempotencyKey, signal) {
    await delay(MUTATION_LATENCY_MS, signal)
    assertNotAborted(signal)

    // Read-coherent transport / permission states, shared with the orders reads.
    const readScenario = getShopOrdersScenarioKey()
    if (readScenario === 'unavailable') throw new ApiUnavailableError()
    if (readScenario === 'forbidden') throw manageForbiddenError()

    // Non-leaking 404: a malformed, missing or other-tenant order id (the
    // tenant-isolated store returns undefined for all three).
    const order = findSeedOrder(tenantId, orderId)
    if (order === undefined) throw notFoundError()

    const storedVersion = order.detail.version

    // Idempotency, checked before any transition runs. Same key + same action
    // replays the stored response (a 200, no re-mutation); same key + a
    // different action is a conflict and neither action is performed.
    const logKey = operationLogKey(tenantId, idempotencyKey)
    const existing = operationLog.get(logKey)
    if (existing !== undefined) {
      if (existing.action !== body.action) throw idempotencyConflictError()
      return existing.response
    }

    // Deterministic conflict scenarios (the two 409 reasons this capability names).
    if (activeOpScenarioKey === 'staleVersion') throw staleVersionError()
    if (activeOpScenarioKey === 'invalidTransition') throw invalidTransitionError()

    // The real, expectedVersion-gated transition — exactly two legal moves,
    // nothing out of either terminal state.
    const legalTransition =
      (body.action === 'Fulfill' && order.status === 'Paid' && storedVersion === body.expectedVersion) ||
      (body.action === 'Cancel' && order.status === 'PendingPayment' && storedVersion === body.expectedVersion)
    if (!legalTransition) {
      // A valid action on the wrong status is an invalid transition; a status
      // that matches but whose version drifted is a stale-version conflict.
      throw storedVersion === body.expectedVersion ? invalidTransitionError() : staleVersionError()
    }

    order.status = body.action === 'Fulfill' ? 'Fulfilled' : 'Cancelled'
    order.detail.version = storedVersion + 1
    const response = buildDetail(order)
    operationLog.set(logKey, { action: body.action, response })
    return response
  },
}
