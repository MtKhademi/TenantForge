import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'

/**
 * S29 payment result (F038, mocked): the terminal screen of the checkout →
 * review → bank → result flow, driven by the `outcome` query param.
 * F039 replaces the mock order number / tracking code with B031's real
 * values and clears the order draft once a real order exists.
 */
const MOCK_ORDER_NUMBER = 'ORD-000001'
const MOCK_TRACKING_CODE = 'MOCKTRACK123'

export function PaymentResultPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const approved = searchParams.get('outcome') === 'approved'

  if (approved) {
    return (
      <section aria-label="نتیجه پرداخت" className="max-w-md space-y-4 text-center">
        <p className="text-lg font-semibold text-success">پرداخت با موفقیت انجام شد</p>
        <p>شماره سفارش: {MOCK_ORDER_NUMBER}</p>
        <p>کد پیگیری: {MOCK_TRACKING_CODE}</p>
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
