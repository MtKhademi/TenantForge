/**
 * S30 guest order lookup (F041): the response shape of B033's anonymous
 * `POST /api/shop/{tenantId}/orders/lookup`. Field-for-field with B033's
 * `OrderLookupResponse` (introduced with F040's mock, now the shared type
 * the real adapter in `orderLookupAdapter.ts` and the page both use).
 */
export type OrderLookupItem = {
  productNameSnapshot: string
  variantLabelSnapshot: string
  unitPrice: number
  quantity: number
}

export type OrderLookupResult = {
  orderNumber: string
  status: string
  createdAtUtc: string
  shippingProvince: string
  shippingCity: string
  shippingAddressLine: string
  shippingPostalCode: string
  subTotal: number
  shippingCost: number
  discountAmount: number
  grandTotal: number
  items: OrderLookupItem[]
}
