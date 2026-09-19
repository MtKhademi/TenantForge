import { useEffect, useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { placeOrderAndInitiatePayment } from '@/features/shop/orderAdapter'
import { loadOrderDraft, savePlacedOrder, type OrderDraft } from '@/features/shop/orderDraftState'

/**
 * S29 order review (F039, connected): shows the shopper what they are about
 * to buy and pay for — the address/coupon inputs and computed summary the
 * checkout page collected (carried by `orderDraftState`). With no draft
 * (direct visit, stale link) it redirects back to checkout. The "place order"
 * action calls B031's order-creation endpoint (which revalidates shipping and
 * coupon, snapshots the cart and generates the order number / tracking code),
 * immediately initiates a B032 sandbox payment, stores the placed-order
 * carrier, and navigates to the sandbox bank page only after both calls
 * succeed. A stock-race failure (the cart consumed between the checkout
 * summary and order creation) surfaces as a clear error, not a broken page.
 */
export function OrderReviewPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const navigate = useNavigate()
  const [draft, setDraft] = useState<OrderDraft | null | undefined>(undefined)
  const [isPlacing, setIsPlacing] = useState(false)
  const [placeError, setPlaceError] = useState<string | null>(null)

  useEffect(() => {
    setDraft(loadOrderDraft())
  }, [])

  async function handlePlaceOrder() {
    if (!draft) return
    setIsPlacing(true)
    setPlaceError(null)
    try {
      const { order, payment } = await placeOrderAndInitiatePayment(tenantId, draft)
      savePlacedOrder({
        orderId: order.orderId,
        orderNumber: order.orderNumber,
        trackingCode: order.trackingCode,
        gatewayReference: payment.gatewayReference,
      })
      navigate(`/shop/${tenantId}/bank`)
    } catch (error) {
      setPlaceError(error instanceof Error ? error.message : 'خطایی رخ داد.')
    } finally {
      setIsPlacing(false)
    }
  }

  if (draft === undefined) return null
  if (draft === null) return <Navigate to={`/shop/${tenantId}/checkout`} replace />

  return (
    <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-6">
      <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>

      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="font-semibold">{draft.customerName}</p>
        <p className="text-sm text-muted-foreground">{draft.customerPhone}</p>
        <p className="mt-2 text-sm">
          {draft.shippingProvince}، {draft.shippingCity}، {draft.shippingAddressLine} — {draft.shippingPostalCode}
        </p>
      </div>

      <dl className="space-y-2 rounded-xl border border-border bg-surface p-5 text-sm shadow-soft">
        <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{draft.subTotal.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>تخفیف</dt><dd>{draft.discountAmount.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{draft.shippingCost.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{draft.grandTotal.toLocaleString('fa-IR')}</dd></div>
      </dl>

      {placeError && <p role="alert" className="text-sm font-semibold text-destructive">{placeError}</p>}

      <Button type="button" className="w-full" disabled={isPlacing} onClick={() => void handlePlaceOrder()}>
        {isPlacing ? 'در حال ثبت…' : 'ثبت سفارش و پرداخت'}
      </Button>
    </section>
  )
}
