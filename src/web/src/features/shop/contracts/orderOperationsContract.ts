import { z } from 'zod'
import { adminOrderDetailSchema, type AdminOrderDetail } from './adminOrdersContract'

/**
 * S39 (B043): the operator order-status action wire shapes — the exact request
 * the backend accepts for
 * `PATCH /api/tenants/{tenantId}/shop/orders/{orderId}/status`, plus the
 * problem codes that route can return. The success body is B042's
 * `AdminOrderDetailResponse`, reused verbatim from `adminOrdersContract.ts`
 * (never redefined here).
 *
 * Field-for-field with `docs/design/shop/http-contracts.md` "S39 / B043" and
 * the backend `AdminOrderContracts.cs` / `AdminOrdersFeature.cs`: same member
 * names, casing, nullability and enum strings. No UI-only members are added to
 * any wire type.
 *
 * Contract facts this file pins:
 * - `action` is exactly `Fulfill` or `Cancel`; anything else is a `400` naming
 *   the `action` field.
 * - `expectedVersion` is the optimistic-concurrency token the client last saw
 *   for the order's `version`; it must be `>= 1` (the backend rejects `< 1`
 *   with a `400` naming the `expectedVersion` field).
 * - The `Idempotency-Key` header is required and a UUID (a `400` naming
 *   `Idempotency-Key` otherwise). It is a per-request tag, not part of the
 *   JSON body, so it is modelled as the client method's argument, not a wire
 *   member here.
 * - Three DISTINCT `409` reasons, each with its own stable problem `type` and
 *   message: `stale_version`, `invalid_order_transition`,
 *   `idempotency_key_conflict`. A replay of the same key + same action is a
 *   `200` (byte-identical to the original), not an error.
 */

/** The two legal operator actions (case-sensitive, exactly as the backend enum). */
export const orderStatusActionSchema = z.enum(['Fulfill', 'Cancel'])
export type OrderStatusAction = z.infer<typeof orderStatusActionSchema>

/**
 * The request body for `PATCH …/orders/{orderId}/status`. On the wire the
 * backend binds `action` as nullable (to distinguish absent from
 * unknown); this is the client-side shape we always SEND — a complete, valid
 * body — so `action` is the non-nullable enum and `expectedVersion` is a
 * positive int.
 */
export const changeOrderStatusRequestSchema = z.object({
  action: orderStatusActionSchema,
  expectedVersion: z.number().int().min(1),
})
export type ChangeOrderStatusRequest = z.infer<typeof changeOrderStatusRequestSchema>

/**
 * The three distinct conflict reasons B043 returns as an RFC 7807 `409`.
 * The UI renders one message per value — never one generic "something went
 * wrong".
 */
export const orderStatusConflictTypeSchema = z.enum([
  'stale_version',
  'invalid_order_transition',
  'idempotency_key_conflict',
])
export type OrderStatusConflictType = z.infer<typeof orderStatusConflictTypeSchema>

/**
 * The field-error names the route's `400` can name (an invalid or absent
 * `action`, an `expectedVersion` below 1, or a missing / non-UUID
 * `Idempotency-Key` header).
 */
export const orderStatusValidationErrorFieldSchema = z.enum(['action', 'expectedVersion', 'Idempotency-Key'])
export type OrderStatusValidationErrorField = z.infer<typeof orderStatusValidationErrorFieldSchema>

/**
 * The success response of the status action is B042's admin order detail — the
 * order's new, already-committed state (the `Fulfilled`/`Cancelled` status and
 * the bumped `version`). Reused, not redefined.
 */
export const changeOrderStatusResponseSchema = adminOrderDetailSchema
export type ChangeOrderStatusResponse = AdminOrderDetail

/**
 * An action the UI has fired but not yet confirmed as succeeded. Carries the
 * idempotency key so a retry of the SAME unchanged action reuses that key
 * (the server answers from the stored response, not a second mutation), while
 * a different action is always issued a fresh key — a key is never reused
 * across two different actions.
 */
export type PendingOrderAction = {
  idempotencyKey: string
  orderId: string
  body: ChangeOrderStatusRequest
}
