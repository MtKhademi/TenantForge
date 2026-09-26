import type { PaymentInitiation, PaymentResult } from '../contracts/paymentLifecycleContract'

/**
 * S40 (B044): the gateway-neutral payment client port.
 *
 * F052 binds the deterministic mock; F062 replaces exactly this binding with
 * an HTTP implementation of the SAME interface — the pages never change again.
 * Every method is abort-aware: an in-flight request is cancelled when the user
 * navigates away or a newer request starts, and an aborted request must never
 * surface as an error or land stale state.
 *
 * Wire semantics each method carries (per the B044 contract, S40 section of
 * `docs/design/shop/http-contracts.md`):
 *
 * - `initiate` — `POST /api/shop/{tenantId}/orders/{orderId}/payments/initiate`
 *   with a required UUID `Idempotency-Key` header (the page supplies it); no
 *   body. `200` → `PaymentInitiation`. Errors: `400` naming `Idempotency-Key`
 *   for a missing/malformed key; a non-leaking `404` for a malformed, missing
 *   or other-tenant order id; `409` for an order that is no longer
 *   `PendingPayment` or that has hit the 10-attempt cap; a same-key/same-request
 *   replay is a `200` (byte-identical), a same-key/different-request is a `409`
 *   `idempotency_key_conflict`.
 * - `getStatus` — `GET /api/shop/{tenantId}/orders/{orderId}/payments/status?token=…`.
 *   The `token` is the raw callback token the initiation returned — the ONLY
 *   credential for the lookup, returned exactly once. `200` →
 *   `PaymentResult`. Every miss — blank, wrong, or a token for another
 *   order/tenant, or an order with no attempts — is the one identical generic
 *   `404`.
 * - `resolveSandbox` (optional) — `POST …/orders/{orderId}/payments/sandbox/resolve`,
 *   mapped ONLY in Development. It is the single browser-driven payment
 *   simulation. `200` → `PaymentResult`; a blank `authority` is a `400` naming
 *   `authority`. Absent on a production binding.
 */
export interface ShopPaymentsClient {
  /**
   * Starts one payment attempt for a `PendingPayment` order. Idempotent on
   * `idempotencyKey`: a retry of the same key re-sends the stored response
   * rather than minting a second attempt.
   */
  initiate(
    tenantId: string,
    orderId: string,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<PaymentInitiation>

  /**
   * Reads the order's current payment status from the opaque `resultToken`
   * alone. The page resolves purely from this — never from session, draft or a
   * query "outcome" flag — so a refresh after the redirect still resolves.
   */
  getStatus(
    tenantId: string,
    orderId: string,
    token: string,
    signal?: AbortSignal,
  ): Promise<PaymentResult>

  /**
   * Resolves the in-app Sandbox attempt (Development only). `approved` is the
   * customer's simulated decision; the server stays authoritative over the
   * outcome.
   */
  resolveSandbox?(
    tenantId: string,
    orderId: string,
    authority: string,
    approved: boolean,
    signal?: AbortSignal,
  ): Promise<PaymentResult>
}
