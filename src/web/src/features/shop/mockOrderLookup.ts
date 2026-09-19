/**
 * S30 guest order tracking (F040 mock): a stand-in for B033's
 * `POST /api/shop/{tenantId}/orders/lookup`. `OrderLookupResult` matches
 * B033's real `OrderLookupResponse` field-for-field, so F041's connection
 * swaps this module for a thin adapter without touching the page.
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

const MOCK_TRACKING_CODE = 'MOCKTRACK123'
const MOCK_PHONE = '09121234567'

export async function mockLookupOrder(trackingCode: string, customerPhone: string): Promise<OrderLookupResult | null> {
  if (trackingCode.trim() !== MOCK_TRACKING_CODE || customerPhone.trim() !== MOCK_PHONE) {
    return null
  }
  return {
    orderNumber: 'ORD-000001',
    status: 'Paid',
    createdAtUtc: new Date().toISOString(),
    shippingProvince: 'تهران',
    shippingCity: 'تهران',
    shippingAddressLine: 'خیابان ولیعصر',
    shippingPostalCode: '1234567890',
    subTotal: 890_000,
    shippingCost: 50_000,
    discountAmount: 89_000,
    grandTotal: 851_000,
    items: [{ productNameSnapshot: 'پیراهن کلاسیک', variantLabelSnapshot: 'سفید / M', unitPrice: 890_000, quantity: 1 }],
  }
}
