import { useEffect, useState } from 'react'
import { Link, Navigate, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { loadOrderDraft, type OrderDraft } from '@/features/shop/orderDraftState'

/**
 * S29 order review (F038, mocked): shows the shopper what they are about to
 * buy and pay for — the address/coupon inputs and computed summary the
 * checkout page collected (carried by `orderDraftState`). With no draft
 * (direct visit, stale link) it redirects back to checkout. F039 replaces
 * the static display with B031's real order-creation flow.
 */
export function OrderReviewPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [draft, setDraft] = useState<OrderDraft | null | undefined>(undefined)

  useEffect(() => {
    setDraft(loadOrderDraft())
  }, [])

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

      <Link to={`/shop/${tenantId}/bank`} className="block">
        <Button type="button" className="w-full">ثبت سفارش و پرداخت</Button>
      </Link>
    </section>
  )
}
