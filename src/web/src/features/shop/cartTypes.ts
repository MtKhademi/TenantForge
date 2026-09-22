/**
 * S27 cart types (F033) + S36 lease (F048).
 *
 * The wire types (`CartItem`, `CartResponse`) now live in
 * `contracts/cartLeaseContract.ts` as Zod schemas (F048) so the mock and the
 * later HTTP client (F058) share one runtime-validated shape. This file
 * re-exports the single `CartItem` type and keeps the adapter's stock error
 * so there is exactly one cart-item type in the app.
 */
export {
  type CartItem,
  type CartResponse,
} from './contracts/cartLeaseContract'

export class InsufficientStockError extends Error {
  constructor(message = 'موجودی کافی برای این تعداد وجود ندارد.') {
    super(message)
    this.name = 'InsufficientStockError'
  }
}
