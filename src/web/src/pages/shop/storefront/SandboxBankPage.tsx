import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { initiatePayment, submitPaymentCallback, type InitiatePaymentResponse } from '@/features/shop/orderAdapter'
import { loadOrderDraft, loadPlacedOrder } from '@/features/shop/orderDraftState'

/**
 * S29 sandbox bank page (F039, connected): the in-app fake "bank page" the
 * Sandbox payment provider "redirects" to — it stands in for a real gateway's
 * hosted payment page. The visual language is deliberately NOT the
 * storefront's (flat, dashed, neutral, clearly labeled) so nobody mistakes it
 * for a real payment provider's UI.
 *
 * Approve/Decline now call B032's real callback endpoint, and the outcome
 * shown on the result page is driven by the server's resolved
 * `PaymentCallbackResponse.status` (`"Paid"` → approved, anything else →
 * declined) — not merely by which button was pressed, since B032 can
 * independently reject an approval attempt.
 *
 * Because B032 resolves each payment attempt exactly once (`TryResolve`), a
 * declined reference can never be re-callback'd. So this page mints a fresh
 * attempt on every visit ("arriving at the bank page again"), which is what
 * makes the decline → result → "تلاش دوباره" retry path actually re-payable.
 * A ref-based once-guard would NOT be safe here: React StrictMode (dev) runs
 * the mount effect → cleanup → effect again, and a ref would let the second
 * run skip while the cleanup of the first run discards its state update,
 * leaving the page stuck in "connecting". The plain cancelled-flag effect
 * instead simply abandons the first initiation under StrictMode (one extra,
 * never-resolved `Initiated` attempt row, invisible to the user); in a
 * production single-mount render exactly one initiation happens.
 */
export function SandboxBankPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const navigate = useNavigate()
  const draft = loadOrderDraft()
  const [placedOrder] = useState(loadPlacedOrder)
  const [gateway, setGateway] = useState<InitiatePaymentResponse | null>(null)
  const [payError, setPayError] = useState<string | null>(null)
  const [isProcessing, setIsProcessing] = useState(false)

  useEffect(() => {
    if (!placedOrder) return
    let cancelled = false
    void (async () => {
      try {
        const payment = await initiatePayment(tenantId, placedOrder.orderId)
        if (!cancelled) setGateway(payment)
      } catch (error) {
        if (!cancelled) setPayError(error instanceof Error ? error.message : 'خطایی رخ داد.')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [tenantId, placedOrder])

  async function handleCallback(approved: boolean) {
    if (!placedOrder || !gateway || isProcessing) return
    setIsProcessing(true)
    setPayError(null)
    try {
      const callback = await submitPaymentCallback(
        tenantId,
        placedOrder.orderId,
        gateway.gatewayReference,
        approved,
      )
      const outcome = callback.status === 'Paid' ? 'approved' : 'declined'
      navigate(`/shop/${tenantId}/payment-result?outcome=${outcome}`)
    } catch (error) {
      setPayError(error instanceof Error ? error.message : 'خطایی رخ داد.')
      setIsProcessing(false)
    }
  }

  if (!placedOrder) {
    return (
      <div className="mx-auto max-w-md rounded-lg border-2 border-dashed border-neutral-400 bg-neutral-100 p-8 text-center text-neutral-900">
        <p className="text-xs font-bold uppercase tracking-widest text-neutral-500">Sandbox Bank — شبیه‌سازی پرداخت</p>
        <p className="mt-4 text-sm">سفارشی برای پرداخت یافت نشد.</p>
        <Link to={`/shop/${tenantId}`} className="mt-6 inline-block text-sm font-semibold underline underline-offset-4">
          بازگشت به فروشگاه
        </Link>
      </div>
    )
  }

  return (
    <div className="mx-auto max-w-md rounded-lg border-2 border-dashed border-neutral-400 bg-neutral-100 p-8 text-center text-neutral-900">
      <p className="text-xs font-bold uppercase tracking-widest text-neutral-500">Sandbox Bank — شبیه‌سازی پرداخت</p>
      <p className="mt-4 text-sm">این یک درگاه پرداخت واقعی نیست.</p>
      <p className="mt-2 text-2xl font-bold">{draft?.grandTotal.toLocaleString('fa-IR') ?? '—'} تومان</p>

      {payError && (
        <p role="alert" className="mt-4 text-sm font-semibold text-red-700">
          {payError}
        </p>
      )}

      <div className="mt-6 flex gap-3">
        <Button
          type="button"
          className="flex-1 bg-neutral-800 text-white hover:bg-neutral-700"
          disabled={!gateway || isProcessing}
          onClick={() => void handleCallback(true)}
        >
          {isProcessing ? 'در حال پردازش…' : 'Approve'}
        </Button>
        <Button
          type="button"
          className="flex-1 border border-neutral-400 bg-neutral-100 text-neutral-900 hover:bg-neutral-200"
          disabled={!gateway || isProcessing}
          onClick={() => void handleCallback(false)}
        >
          {isProcessing ? 'در حال پردازش…' : 'Decline'}
        </Button>
      </div>
      <p className="mt-3 text-xs text-neutral-500">
        {gateway ? 'درگاه متصل شد' : 'در حال اتصال به درگاه…'}
      </p>
    </div>
  )
}
