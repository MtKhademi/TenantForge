import { z } from 'zod'
import { shopProblemSchema, type ShopProblem } from './shopContract'

/**
 * S36 / B040 wire contract: cart reservation lease.
 *
 * Mirrors the persistent Shop HTTP contract, section "S36 / B040 — cart
 * reservation lease" (docs/design/shop/http-contracts.md), field for field:
 * the existing cart read/mutation responses gain the server-owned
 * `expiresAtUtc`; successful add/update/delete item mutations extend the
 * lease while a plain cart GET never does; and cart, checkout-summary and
 * order-creation routes answer RFC 7807 `410` with `type: 'shop_cart_expired'`
 * when the reservation was released. No request accepts a client expiry
 * value, so no request schema carries one.
 *
 * `CartItem` is the app's single cart-item type: the former
 * `cartTypes.ts` shape moved here field for field and is re-exported as
 * `z.infer` of this schema, so no second cart-item type exists.
 * F058 binds an HTTP implementation of `ShopCartLeaseClient` to these
 * schemas; this file is the wire shape both must satisfy.
 */

/** The existing `CartItemResponse` (S27 cart) — field names unchanged. */
export const cartItemSchema = z.object({
  id: z.string(),
  productVariantId: z.string(),
  productName: z.string(),
  variantLabel: z.string(),
  quantity: z.number().int().positive(),
  unitPrice: z.number(),
})
export type CartItem = z.infer<typeof cartItemSchema>

/** Cart read and mutation response — `expiresAtUtc` is the B040 addition. */
export const cartResponseSchema = z.object({
  cartId: z.string(),
  items: z.array(cartItemSchema),
  subTotal: z.number(),
  expiresAtUtc: z.string(),
})
export type CartResponse = z.infer<typeof cartResponseSchema>

/**
 * B040's create-cart response. Defined for completeness of the contract;
 * cart creation in this mock phase is internal to the lease client (the UI
 * never asks for a fresh cart explicitly), so the current mock does not
 * surface it.
 */
export const createCartResponseSchema = z.object({
  cartId: z.string(),
  expiresAtUtc: z.string(),
})
export type CreateCartResponse = z.infer<typeof createCartResponseSchema>

/**
 * The B040 expiry problem: RFC 7807 status `410 Gone` with the exact type
 * discriminator `shop_cart_expired`. Any other 410 (or 404, or a network
 * failure) is NOT an expiry.
 */
export const cartExpiredProblemSchema = shopProblemSchema.extend({
  status: z.literal(410),
  type: z.literal('shop_cart_expired'),
})
export type CartExpiredProblem = z.infer<typeof cartExpiredProblemSchema>

/**
 * Thrown (or classified) when a cart, checkout-summary or order-creation
 * call receives the `410 shop_cart_expired` problem. The three storefront
 * pages branch on this one class to run the expiry recovery flow; every
 * other failure stays an ordinary error state.
 */
export class CartLeaseExpired extends Error {
  readonly problem: ShopProblem

  constructor(problem: ShopProblem) {
    super(
      problem.detail ??
        'رزرو سبد خرید شما به پایان رسیده است؛ اقلام آن‌تخلیه شده‌اند.',
    )
    this.name = 'CartLeaseExpired'
    this.problem = problem
  }
}
