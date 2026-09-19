import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { loadPlacedOrder } from '@/features/shop/orderDraftState'

/**
 * S29 payment result (F039, connected): the terminal screen of the
 * checkout → review → bank → result flow, driven by the `outcome` query
 * param (which the sandbox bank page sets from the server's resolved
 * status). The displayed order number and tracking code are B031's real
 * values, read from the placed-order carrier the review page stored after
 * order creation — the F038 mock constants are gone.
 */
export function PaymentResultPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const approved = searchParams.get('outcome') === 'approved'
  const placedOrder = loadPlacedOrder()

  if (approved) {
    return (
      <section aria-label="نتیجه پرداخت" className="max-w-md space-y-4 text-center">
        <p className="text-lg font-semibold text-success">پرداخت با موفقیت انجام شد</p>
        <p>
          شماره سفارش: <span dir="ltr">{placedOrder?.orderNumber ?? '—'}</span>
        </p>
        <p>
          کد پیگیری: <span dir="ltr">{placedOrder?.trackingCode ?? '—'}</span>
        </p>
        <Link to={`/shop/${tenantId}`}>
          <Button type="button">بازگشت به فروشگاه</Button>
        </Link>
      </section>
    )
  }

  return (
    <section aria-label="نتیجه پرداخت" className="max-w-md space-y-4 text-center">
      <p className="text-lg font-semibold text-destructive">پرداخت ناموفق بود</p>
      <p className="text-sm text-muted-foreground">سفارش شما همچنان در انتظار پرداخت است.</p>
      <Link to={`/shop/${tenantId}/bank`}>
        <Button type="button">تلاش دوباره</Button>
      </Link>
    </section>
  )
}
