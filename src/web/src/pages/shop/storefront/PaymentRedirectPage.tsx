import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import {
  assertAllowedPaymentRedirect,
  PAYMENT_GATEWAY_HOSTS,
  type PaymentRedirectCheck,
} from '@/features/shop/contracts/paymentLifecycleContract'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { savePaymentFlow } from '@/features/shop/paymentFlowToken'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { PAYMENT_DEMO_ORDER_ID } from '@/features/shop/clients/mockShopPaymentsClient'
import { ShieldAlert, TriangleAlert, WifiOff } from 'lucide-react'

/**
 * S40 (B044) — the gateway-neutral payment redirect page (F052, mock phase).
 *
 * The entry the checkout flow hands off to after an order is placed: it calls
 * `payments.initiate` with the route `tenantId`, an order id, and a stable
 * idempotency key, then — and ONLY after vetting the returned `redirectUrl` —
 * navigates the browser to the gateway. `F062` wires this to the real B044
 * HTTP routes; until then the `payments` slot is the deterministic mock.
 *
 * Redirect safety (step 10): the gateway's `redirectUrl` is untrusted input.
 * It is vetted by the one small `assertAllowedPaymentRedirect` helper (the
 * check `F062` will lift into a shared helper) before ANY navigation. A
 * Sandbox initiation returns a relative same-origin path; a real provider
 * returns an absolute https URL on an expected host. An absolute `http://`
 * URL, an absolute URL on an unexpected host, and a protocol-relative
 * `//host/path` all fail the check and render an error instead of navigating.
 *
 * The idempotency key is minted ONCE per page mount (a ref, so React
 * StrictMode's double-mount reuses it) — a reload or re-render never mints a
 * second key for the same logical initiation, which is what keeps repeated
 * initiations from creating duplicate attempts.
 */

type PageState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | {
      kind: 'redirecting'
      provider: string
      target: string
      sameOrigin: boolean
      initiate?: { tenantId: string; orderId: string }
    }
  | { kind: 'unsafe'; provider: string; rawUrl: string; check: Extract<PaymentRedirectCheck, { ok: false }> }
  | { kind: 'error'; problem: ShopClientError }
  | { kind: 'unavailable' }

/**
 * The reason the redirect check rejected the URL, rendered as a precise
 * Persian message. Each distinct reason gets its own wording so a reviewer can
 * tell the three unsafe shapes apart — they are NOT collapsed into one
 * "something went wrong".
 */
function unsafeReasonText(check: Extract<PaymentRedirectCheck, { ok: false }>): string {
  switch (check.reason) {
    case 'protocol-relative':
      return 'هدف هدایت یک آدرس نسبی از مبدأ دیگر (//…) بود که به‌عنوان مسیر امن تلقی نمی‌شود؛ برای جلوگیری از هدایت به سایت ناخواسته، ادامه داده نشد.'
    case 'insecure-scheme':
      return 'درگاه پرداخت آدرسی با پروتکل ناامن (http) برگرداند؛ به دلیل ریسک تغییر مسیر، ادامه داده نشد.'
    case 'unexpected-host':
      return 'درگاه پرداخت آدرسی با میز (host) غیرمجاز بازگرداند؛ برای جلوگیری از هدایت به سایت ناخواسته، ادامه داده نشد.'
    default:
      return 'هدف هدایت پرداخت با قوانین امنیتی مطابقت نداشت و ادامه داده نشد.'
  }
}

export function PaymentRedirectPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [searchParams] = useSearchParams()
  const { payments } = useShopClients()

  const isDev = import.meta.env.DEV
  // A dev-only flag lets a reviewer stay on the page (and see the vetted
  // redirect + the unsafe-rejection states) instead of being navigated away.
  const dryRun = isDev && searchParams.get('dryRun') === '1'
  // The order to pay. The real flow passes it; in the mock phase the page
  // falls back to the demo order id so it is reviewable on its own.
  const orderId = searchParams.get('order') ?? PAYMENT_DEMO_ORDER_ID

  const [state, setState] = useState<PageState>({ kind: 'idle' })

  // One stable idempotency key per logical initiation (per mount).
  const idempotencyKeyRef = useRef<string>(crypto.randomUUID())
  // One abort controller per in-flight initiation. A NEW run aborts the
  // previous one (supersede), and unmount aborts whatever is pending — so an
  // older request's result can never overwrite a newer one, and an abort never
  // surfaces as an error.
  const controllerRef = useRef<AbortController | null>(null)

  const run = useCallback(async () => {
    // Supersede any in-flight request before starting a new one.
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setState({ kind: 'loading' })
    try {
      const initiation = await payments.initiate(
        tenantId,
        orderId,
        idempotencyKeyRef.current,
        controller.signal,
      )
      const check = assertAllowedPaymentRedirect(
        initiation.provider,
        initiation.redirectUrl,
        PAYMENT_GATEWAY_HOSTS,
      )
      if (!check.ok) {
        setState({ kind: 'unsafe', provider: initiation.provider, rawUrl: initiation.redirectUrl, check })
        return
      }

      // Store the opaque token + order for this tenant: the Sandbox redirect
      // URL carries only the authority, but the bank page needs the token so it
      // can hand it to the result page (which resolves purely from it).
      savePaymentFlow(tenantId, { orderId, resultToken: initiation.resultToken })

      const sameOrigin = check.url.startsWith('/')
      setState({
        kind: 'redirecting',
        provider: initiation.provider,
        target: check.url,
        sameOrigin,
        initiate: sameOrigin ? { tenantId, orderId } : undefined,
      })
      // Same-origin targets navigate in the same tab after a short beat. In
      // dev, an absolute gateway URL is not reachable, so we stay on the page
      // and show the vetted target instead of navigating away; in production a
      // real absolute gateway navigates the same tab too.
      if (dryRun || (isDev && !sameOrigin)) return
      if (sameOrigin || !isDev) {
        window.setTimeout(() => {
          window.location.assign(check.url)
        }, 350)
      }
    } catch (error) {
      // An abort (supersede or unmount) is NOT an error: the newer request owns
      // the page, so the older one silently drops out.
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (controller.signal.aborted) return
      if (error instanceof ApiUnavailableError) {
        setState({ kind: 'unavailable' })
        return
      }
      if (error instanceof ShopClientError) {
        setState({ kind: 'error', problem: error })
        return
      }
      setState({ kind: 'error', problem: new ShopClientError({ status: 500, title: 'خطا' }) })
    }
  }, [tenantId, orderId, payments, dryRun])

  // Run once on mount; abort whatever is in flight on unmount.
  useEffect(() => {
    void run()
    return () => {
      controllerRef.current?.abort()
    }
  }, [run])

  return (
    <section aria-label="هدایت به درگاه پرداخت" className="mx-auto max-w-md space-y-4">
      <h1 className="text-center text-2xl font-semibold">در حال آماده‌سازی پرداخت</h1>

      {state.kind === 'idle' && (
        <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft">
          <p className="text-sm text-muted-foreground">در حال اتصال به درگاه پرداخت…</p>
        </div>
      )}

      {/* Fixed-height skeleton: the page must not jump when loading finishes. */}
      {state.kind === 'loading' && (
        <div className="min-h-[9rem] animate-pulse rounded-xl border border-border bg-surface p-6 shadow-soft" aria-hidden="true">
          <div className="mx-auto h-5 w-3/4 rounded bg-muted" />
          <div className="mx-auto mt-4 h-4 w-1/2 rounded bg-muted" />
          <div className="mx-auto mt-6 h-10 w-full rounded-md bg-muted" />
        </div>
      )}

      {state.kind === 'redirecting' && (
        <div className="rounded-xl border border-border bg-surface p-6 text-center shadow-soft" aria-live="polite">
          <p className="text-sm font-semibold">
            {state.sameOrigin
              ? 'به درگاه «' + state.provider + '» هدایت می‌شوید…'
              : 'درگاه «' + state.provider + '» اعتبارسنجی شد'}
          </p>
          <p dir="ltr" className="mt-2 break-all text-xs text-muted-foreground">{state.target}</p>
          {(dryRun || (isDev && !state.sameOrigin)) && (
            <p className="mt-3 rounded-md bg-muted px-3 py-2 text-xs text-muted-foreground">
              (حالت آزمایشی: از هدایت خودداری شد تا نتیجهٔ بررسی نمایش داده شود)
            </p>
          )}
        </div>
      )}

      {state.kind === 'unsafe' && (
        <StatePanel
          icon={
            <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
              <ShieldAlert aria-hidden="true" className="size-6" />
            </span>
          }
          title="هدف هدایت پرداخت امن نیست"
          description={
            <span className="block space-y-2">
              <span>{unsafeReasonText(state.check)}</span>
              <span dir="ltr" className="block break-all text-xs text-muted-foreground">{state.rawUrl}</span>
            </span>
          }
          action={
            <div className="pt-3">
              <Link to={`/shop/${tenantId}/payment-result`} className="inline-block">
                <Button type="button">بازگشت به فروشگاه</Button>
              </Link>
            </div>
          }
        />
      )}

      {state.kind === 'error' && (
        <StatePanel
          icon={
            <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
              <TriangleAlert aria-hidden="true" className="size-6" />
            </span>
          }
          title={state.problem.problem.title ?? 'پرداخت امکان‌پذیر نبود'}
          description={
            <span className="block">
              {state.problem.problem.detail ?? 'درخواست پرداخت انجام نشد. لطفاً دوباره تلاش کنید.'}
              {state.problem.problem.errors && Object.keys(state.problem.problem.errors).length > 0 && (
                <span dir="ltr" className="mt-1 block break-all text-xs text-muted-foreground">
                  {Object.entries(state.problem.problem.errors)
                    .map(([field, msgs]) => `${field}: ${msgs.join(' ')}`)
                    .join(' | ')}
                </span>
              )}
            </span>
          }
          action={
            <div className="pt-3">
              <Button type="button" onClick={() => void run()}>تلاش دوباره</Button>
            </div>
          }
        />
      )}

      {state.kind === 'unavailable' && (
        <StatePanel
          icon={
            <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
              <WifiOff aria-hidden="true" className="size-6" />
            </span>
          }
          title="اتصال به درگاه پرداخت برقرار نشد"
          description="در این لحظه به فروشگاه دسترسی نداریم. کمی بعد دوباره تلاش کنید."
          action={
            <div className="pt-3">
              <Button type="button" onClick={() => void run()}>تلاش دوباره</Button>
            </div>
          }
        />
      )}
    </section>
  )
}
