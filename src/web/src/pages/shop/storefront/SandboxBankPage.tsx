import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { clearPaymentFlow, loadPaymentFlow } from '@/features/shop/paymentFlowToken'
import { loadOrderDraft } from '@/features/shop/orderDraftState'

/**
 * S40 (B044) — the in-app fake "bank page" (F052, mock phase).
 *
 * DEVELOPMENT-ONLY: this page is mapped behind the `bank` route in `App.tsx`,
 * which is gated on `import.meta.env.DEV`, so the route (and this component)
 * is not registered at all in a production build. It stands in for a real
 * gateway's hosted payment page for the Sandbox provider, and the visual
 * language is deliberately NOT the storefront's (flat, dashed, neutral,
 * clearly labeled) so nobody mistakes it for a real payment provider's UI.
 *
 * The page carries the gateway `authority` and the opaque `resultToken` in the
 * query string (the redirect page navigates here with both). Approve/Decline
 * call the `payments` client's `resolveSandbox` (the Development-only B044
 * route — the only browser-driven payment simulation), and the result is
 * driven by the SERVER's resolved `PaymentResult.status`, not merely by which
 * button was pressed, since the server can independently reject an approval.
 * After resolving it navigates to the result page carrying ONLY the token, so
 * the result page stays refresh-safe (it resolves purely from the token).
 *
 * F062 wires this to the real B044 HTTP routes; until then the `payments`
 * slot is the deterministic mock. The `resolveSandbox` method is optional on
 * the client port (it is Development-only), so this page only renders when it
 * exists — a production binding would never mount this page anyway.
 */

type BankState =
  | { kind: 'idle' }
  | { kind: 'processing' }
  | { kind: 'error'; message: string }

export function SandboxBankPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { payments } = useShopClients()

  const draft = loadOrderDraft(tenantId)
  const authority = searchParams.get('authority') ?? ''
  // The token + order are carried by the flow the redirect page stored (the
  // sandbox redirectUrl only carries the authority). They are never read as a
  // source of truth by the result page — only handed off.
  const flow = loadPaymentFlow(tenantId)
  const orderId = flow?.orderId ?? ''
  const resultToken = flow?.resultToken ?? ''

  const resolveSandbox = payments.resolveSandbox
  const [state, setState] = useState<BankState>({ kind: 'idle' })
  const controllerRef = useRef<AbortController | null>(null)

  // Abort any in-flight resolve on unmount (a superseded decision must not
  // land after the user has navigated on).
  useEffect(() => {
    return () => {
      controllerRef.current?.abort()
    }
  }, [])

  async function handleResolve(approved: boolean) {
    if (!resolveSandbox || !orderId || state.kind === 'processing') return
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setState({ kind: 'processing' })
    try {
      // The resolve is awaited for its side effect (the server stays the
      // authority over the outcome); the result page then re-reads the status
      // from the opaque token itself, so we navigate carrying only route
      // context + the token — never the outcome or any payment value.
      await resolveSandbox(tenantId, orderId, authority, approved, controller.signal)
      const next = new URLSearchParams()
      if (resultToken) next.set('token', resultToken)
      next.set('order', orderId)
      clearPaymentFlow(tenantId)
      navigate(`/shop/${tenantId}/payment-result?${next.toString()}`)
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (controller.signal.aborted) return
      if (error instanceof ApiUnavailableError) {
        setState({ kind: 'error', message: 'اتصال به فروشگاه برقرار نشد. کمی بعد دوباره تلاش کنید.' })
        return
      }
      if (error instanceof ShopClientError) {
        const problem = error.problem
        // A blank authority is a 400; a no-longer-payable order is a 409.
        const specific =
          problem.status === 409
            ? 'این سفارش دیگر قابل پرداخت نیست.'
            : problem.detail ?? problem.title ?? 'پردازش پرداخت ناموفق بود.'
        setState({ kind: 'error', message: specific })
        return
      }
      setState({ kind: 'error', message: 'پردازش پرداخت ناموفق بود.' })
    }
  }

  // The empty state: no attempt to approve (no authority carried, or the
  // resolve route is absent — the latter cannot happen in dev).
  const canResolve = resolveSandbox !== undefined && orderId.length > 0

  return (
    <div className="mx-auto max-w-md rounded-lg border-2 border-dashed border-neutral-400 bg-neutral-100 p-8 text-center text-neutral-900">
      <p className="text-xs font-bold uppercase tracking-widest text-neutral-500">
        Sandbox Bank — شبیه‌سازی پرداخت
      </p>
      <p className="mt-2 rounded-md bg-neutral-200 px-3 py-2 text-xs font-semibold text-neutral-700">
        فقط برای توسعه — این یک درگاه پرداخت واقعی نیست.
      </p>
      <p className="mt-4 text-sm">این صفحه در نسخهٔ انتشار در دسترس نیست.</p>

      {!canResolve ? (
        <>
          <p className="mt-6 text-sm">برای پرداخت، تلاش پرداختی فعالی وجود ندارد.</p>
          <Link
            to={`/shop/${tenantId}`}
            className="mt-6 inline-block text-sm font-semibold underline underline-offset-4"
          >
            بازگشت به فروشگاه
          </Link>
        </>
      ) : (
        <>
          <p className="mt-6 text-2xl font-bold">
            {draft?.grandTotal.toLocaleString('fa-IR') ?? '—'} تومان
          </p>

          {state.kind === 'error' && (
            <p role="alert" className="mt-4 text-sm font-semibold text-red-700">
              {state.message}
            </p>
          )}

          <div className="mt-6 flex gap-3">
            <Button
              type="button"
              className="flex-1 bg-neutral-800 text-white hover:bg-neutral-700"
              disabled={state.kind === 'processing'}
              onClick={() => void handleResolve(true)}
            >
              {state.kind === 'processing' ? 'در حال پردازش…' : 'Approve'}
            </Button>
            <Button
              type="button"
              className="flex-1 border border-neutral-400 bg-neutral-100 text-neutral-900 hover:bg-neutral-200"
              disabled={state.kind === 'processing'}
              onClick={() => void handleResolve(false)}
            >
              {state.kind === 'processing' ? 'در حال پردازش…' : 'Decline'}
            </Button>
          </div>
          <p className="mt-3 text-xs text-neutral-500">
            {state.kind === 'processing' ? 'در حال پردازش…' : 'به درگاه متصل شد'}
          </p>
        </>
      )}
    </div>
  )
}
