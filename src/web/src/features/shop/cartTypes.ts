export type CartItemResponse = {
  id: string
  productVariantId: string
  productName: string
  variantLabel: string
  quantity: number
  unitPrice: number
}

export type CartResponse = {
  cartId: string
  items: CartItemResponse[]
  subTotal: number
}

export class InsufficientStockError extends Error {
  constructor(message = 'موجودی کافی برای این تعداد وجود ندارد.') {
    super(message)
    this.name = 'InsufficientStockError'
  }
}
