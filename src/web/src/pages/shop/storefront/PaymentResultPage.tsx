import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { BadgeCheck, CheckCircle2, Clock, Info, XCircle } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { delay } from '@/features/shop/clients/shopFetch'
import { PAYMENT_DEMO_ORDER_ID } from '@/features/shop/clients/mockShopPaymentsClient'
import type { PaymentResult } from '@/features/shop/contracts/paymentLifecycleContract'

/**
 * S40 (B044) — the payment result page (F052, mock phase).
 *
 * Resolves its state PURELY from the opaque `resultToken` in the URL — never
 * from session/draft state or a query "outcome" flag — so refreshing the page
 * after a redirect still shows the correct state (the token is the only
 * credential for the status lookup). It calls `payments.getStatus` with that
 * token; F062 wires this to the real B044 HTTP route.
 *
 * `PendingPayment` is a STATUS, not an error: it is the bounded-polling state.
 * A single abort-guarded effect polls `getStatus` a fixed number of times with
 * a delay between each (bounded, so it never polls forever), then stops and
 * leaves a manual "بررسی دوباره" control the user can click at any time —
 * even after the bounded polling has stopped.
 */

type ResultState =
  | { kind: 'loading' }
  | { kind: 'result'; result: PaymentResult }
  | { kind: 'pending'; result: PaymentResult; polling: boolean }
  | { kind: 'notFound' }
  | { kind: 'unavailable' }
  | { kind: 'error'; problem: ShopClientError }

/** The bounded number of additional polls after the first read. */
const MAX_PENDING_POLLS = 3
/** The delay between bounded polls. */
const PENDING_POLL_INTERVAL_MS = 1_200

export function PaymentResultPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const { payments } = useShopClients()

  // The token is the ONLY state source. `?order` is a mock-phase convenience
  // (the real flow carries it in the route or a signed state); a direct visit
  // without a token falls back to the demo order so the page is reviewable.
  const token = searchParams.get('token') ?? ''
  const orderId = searchParams.get('order') ?? PAYMENT_DEMO_ORDER_ID

  const [state, setState] = useState<ResultState>({ kind: 'loading' })
  const [generation, setGeneration] = useState(0)
  const lastResultRef = useRef<PaymentResult | null>(null)

  // Bounded, abort-guarded polling loop. `generation` restarts the whole
  // sequence: a manual "check again" aborts the current controller and starts
  // a fresh loop with a new poll budget. The effect's cleanup aborts on
  // unmount or when the token/order changes, so a superseded sequence never
  // lands stale state and an abort never surfaces as an error.
  useEffect(() => {
    const controller = new AbortController()
    let polls = 0
    setState((prev) =>
      prev.kind === 'pending' || prev.kind === 'result' ? prev : { kind: 'loading' },
    )

    ;(async () => {
      while (true) {
        try {
          const result = await payments.getStatus(tenantId, orderId, token, controller.signal)
          if (controller.signal.aborted) return
          lastResultRef.current = result

          if (result.status !== 'PendingPayment') {
            setState({ kind: 'result', result })
            return
          }

          if (polls >= MAX_PENDING_POLLS) {
            // Bounded polling exhausted: stop. The manual control remains.
            setState({ kind: 'pending', result, polling: false })
            return
          }
          setState({ kind: 'pending', result, polling: true })
          polls += 1
          // Abort-aware wait between polls.
          await delay(PENDING_POLL_INTERVAL_MS, controller.signal)
        } catch (error) {
          if (error instanceof DOMException && error.name === 'AbortError') return
          if (controller.signal.aborted) return
          if (error instanceof ApiUnavailableError) {
            setState({ kind: 'unavailable' })
            return
          }
          if (error instanceof ShopClientError) {
            // The one identical generic 404 (unknown order or mismatched token).
            if (error.problem.status === 404) {
              setState({ kind: 'notFound' })
              return
            }
            setState({ kind: 'error', problem: error })
            return
          }
          setState({ kind: 'unavailable' })
          return
        }
      }
    })()

    return () => {
      controller.abort()
    }
  }, [payments, tenantId, orderId, token, generation])

  const retry = useCallback(() => setGeneration((g) => g + 1), [])

  return (
    <section aria-label="نتیجه پرداخت" className="mx-auto max-w-md space-y-4">
      <h1 className="text-center text-2xl font-semibold">نتیجه پرداخت</h1>

      {/* Fixed-height shell: the page never jumps when a state resolves. */}
      <div className="min-h-[13rem]">
        {state.kind === 'loading' && (
          <div className="animate-pulse rounded-xl border border-border bg-surface p-6 shadow-soft" aria-hidden="true">
            <div className="mx-auto h-10 w-10 rounded-full bg-muted" />
            <div className="mx-auto mt-4 h-5 w-2/3 rounded bg-muted" />
            <div className="mx-auto mt-3 h-4 w-1/2 rounded bg-muted" />
          </div>
        )}

        {state.kind === 'result' && <ResultCard result={state.result} tenantId={tenantId} />}
        {state.kind === 'pending' && (
          <PendingCard result={state.result} polling={state.polling} onRetry={retry} />
        )}
        {state.kind === 'notFound' && <NotFoundPanel tenantId={tenantId} />}
        {state.kind === 'unavailable' && (
          <StatePanel
            icon={
              <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
                <Info aria-hidden="true" className="size-6" />
              </span>
            }
            title="اتصال برقرار نشد"
            description="در این لحظه نتیجهٔ پرداخت را نمی‌توان دریافت کرد. کمی بعد دوباره تلاش کنید."
            action={
              <div className="pt-3">
                <Button type="button" onClick={retry}>تلاش دوباره</Button>
              </div>
            }
          />
        )}
        {state.kind === 'error' && (
          <StatePanel
            icon={
              <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
                <XCircle aria-hidden="true" className="size-6" />
              </span>
            }
            title={state.problem.problem.title ?? 'دریافت نتیجه پرداخت ناموفق بود'}
            description={state.problem.problem.detail ?? 'لطفاً دوباره تلاش کنید.'}
            action={
              <div className="pt-3">
                <Button type="button" onClick={retry}>تلاش دوباره</Button>
              </div>
            }
          />
        )}
      </div>
    </section>
  )
}

/** The terminal success/declined/fulfilled/cancelled states (each distinct). */
function ResultCard({ result, tenantId }: { result: PaymentResult; tenantId: string }) {
  if (result.status === 'Paid') {
    return (
      <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
        <BadgeCheck aria-hidden="true" className="mx-auto size-10 text-success" />
        <p className="mt-3 text-lg font-semibold text-success">پرداخت با موفقیت انجام شد</p>
        <p className="mt-2 text-sm text-muted-foreground">
          شماره سفارش: <span dir="ltr">{result.orderNumber}</span>
        </p>
        <BackToStore tenantId={tenantId} />
      </div>
    )
  }

  if (result.status === 'Fulfilled') {
    return (
      <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
        <CheckCircle2 aria-hidden="true" className="mx-auto size-10 text-success" />
        <p className="mt-3 text-lg font-semibold text-success">سفارش تحویل شده است</p>
        <p className="mt-2 text-sm text-muted-foreground">
          شماره سفارش: <span dir="ltr">{result.orderNumber}</span>
        </p>
        <BackToStore tenantId={tenantId} />
      </div>
    )
  }

  if (result.status === 'Cancelled') {
    return (
      <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
        <XCircle aria-hidden="true" className="mx-auto size-10 text-destructive" />
        <p className="mt-3 text-lg font-semibold text-destructive">سفارش لغو شده است</p>
        <p className="mt-2 text-sm text-muted-foreground">
          شماره سفارش: <span dir="ltr">{result.orderNumber}</span>
        </p>
        <BackToStore tenantId={tenantId} />
      </div>
    )
  }

  // Defensive fallback (PendingPayment is handled by PendingCard).
  return (
    <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
      <Clock aria-hidden="true" className="mx-auto size-10 text-muted-foreground" />
      <p className="mt-3 text-lg font-semibold">پرداخت در انتظار است</p>
      <BackToStore tenantId={tenantId} />
    </div>
  )
}

/** The PendingPayment status: honest in-progress state + manual retry. */
function PendingCard({
  result,
  polling,
  onRetry,
}: {
  result: PaymentResult
  polling: boolean
  onRetry: () => void
}) {
  return (
    <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
      <Clock aria-hidden="true" className="mx-auto size-10 text-muted-foreground" />
      <p className="mt-3 text-lg font-semibold">پرداخت در حال بررسی است</p>
      <p className="mt-2 text-sm text-muted-foreground">
        شماره سفارش: <span dir="ltr">{result.orderNumber}</span>
      </p>
      <p className="mt-3 text-sm text-muted-foreground" aria-live="polite">
        {polling
          ? 'در حال بررسی وضعیت پرداخت…'
          : 'بررسی خودکار متوقف شد. می‌توانید وضعیت را دوباره بررسی کنید.'}
      </p>
      <div className="pt-4">
        <Button type="button" onClick={onRetry} disabled={polling}>
          {polling ? 'در حال بررسی…' : 'بررسی دوباره'}
        </Button>
      </div>
    </div>
  )
}

/** The one identical, generic not-found state (unknown order or mismatched token). */
function NotFoundPanel({ tenantId }: { tenantId: string }) {
  return (
    <StatePanel
      icon={
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <XCircle aria-hidden="true" className="size-6" />
        </span>
      }
      title="پرداخت یافت نشد"
      description="نتیجهٔ پرداخت برای این آدرس در دسترس نیست. اگر همین حالا پرداخت کردید، کمی بعد دوباره تلاش کنید."
      action={
        <div className="pt-3">
          <Link to={`/shop/${tenantId}`} className="inline-block">
            <Button type="button">بازگشت به فروشگاه</Button>
          </Link>
        </div>
      }
    />
  )
}

function BackToStore({ tenantId }: { tenantId: string }) {
  return (
    <div className="pt-4">
      <Link to={`/shop/${tenantId}`} className="inline-block">
        <Button type="button">بازگشت به فروشگاه</Button>
      </Link>
    </div>
  )
}
