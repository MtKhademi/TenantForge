import { CircleCheck, Loader2 } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useWatch, useForm } from 'react-hook-form'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import {
  fetchCheckoutSummary,
  CheckoutValidationError,
  type CheckoutSummaryResponse,
} from '@/features/shop/checkoutAdapter'
import {
  COUPON_REASON_MESSAGES,
  evaluateCouponPreview,
  findCouponByCode,
} from '@/features/shop/contracts/couponRulesContract'
import { useCartLease } from '@/features/shop/useCartLease'
import { saveOrderDraft } from '@/features/shop/orderDraftState'
import { CartLeaseCountdown, CartLeaseRecovery } from '@/features/shop/CartLeaseUi'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { assertNotRateLimited } from '@/features/shop/rateLimitScenario'
import { useRateLimitCooldown } from '@/features/shop/useRateLimitCooldown'
import { RateLimitCountdown } from '@/features/shop/RateLimitCooldown'

/**
 * S28 checkout (F037) + S36 reservation lease (F048, mock phase).
 *
 * B040 makes the checkout summary verify the cart lease before pricing. In
 * this mock phase that verification runs through the shared `useCartLease`
 * hook (backed by the B040 mock now, the HTTP client after F058): the page
 * reads the stored cart on mount and the summary panel carries the
 * reservation countdown.
 *
 * - a `410 shop_cart_expired` (or the local countdown reaching zero) swaps the
 *   whole page to the shared recovery panel, clearing ONLY this tenant's cart
 *   id and order draft — other tenants' stored carts are untouched;
 * - a missing/empty cart is the neutral empty state and a network failure is
 *   the unavailable state with a retry — neither is an expiry;
 * - the countdown is display-only: no polling, no auto-extend.
 *
 * The address/coupon form and the debounced (300ms) recompute of the live
 * order summary (B030, real API via `checkoutAdapter`) are unchanged; the
 * "continue" action saves the checkout inputs and computed summary into
 * `orderDraftState` (now per-tenant) and navigates to `/order-review`.
 */
const checkoutSchema = z.object({
  customerName: z.string().min(1, 'نام الزامی است.'),
  customerPhone: z.string().min(1, 'شماره تماس الزامی است.'),
  shippingProvince: z.string().min(1, 'استان الزامی است.'),
  shippingCity: z.string().min(1, 'شهر الزامی است.'),
  shippingAddressLine: z.string().min(1, 'آدرس الزامی است.'),
  shippingPostalCode: z.string().min(1, 'کد پستی الزامی است.'),
  couponCode: z.string(),
})
type CheckoutFormValues = z.infer<typeof checkoutSchema>

/** The five visible states of the checkout coupon field (S37). */
type CouponPreviewState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'applied'; discountAmount: number; code: string }
  | { kind: 'rejected'; message: string }
  | { kind: 'unavailable' }

export function CheckoutPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const navigate = useNavigate()
  const { cartLease, coupons: couponsClient } = useShopClients()
  const { state, reload, markLocalExpiry } = useCartLease(tenantId, cartLease)
  const { register, control, getValues, formState: { errors } } = useForm<CheckoutFormValues>({
    resolver: zodResolver(checkoutSchema),
    defaultValues: {
      customerName: '', customerPhone: '', shippingProvince: '', shippingCity: '',
      shippingAddressLine: '', shippingPostalCode: '', couponCode: '',
    },
  })

  const [summary, setSummary] = useState<CheckoutSummaryResponse | null>(null)
  const [summaryError, setSummaryError] = useState<string | null>(null)
  // S42/B046: the "continue to order review" action is the `checkout` action.
  // On a 429 it is disabled for `retryAfterSeconds` with a visible countdown and
  // is never auto-submitted; the address/coupon form fields are all preserved.
  const { coolingDown, remainingSeconds, start: startCooldown } = useRateLimitCooldown()

  // S37 coupon preview (F049, mock phase): the typed code is evaluated against
  // the mock `coupons` list and the live cart subtotal using B041's pure
  // `evaluateCouponPreview`. It is a read-only preview — it NEVER writes
  // `redeemedCount`, so the displayed usage can only stay the same or rise,
  // never decrease (a real redemption is the only thing that changes it, and
  // this mock does not simulate that). Each B041 reason code maps to its own
  // distinct message via `COUPON_REASON_MESSAGES`.
  const [couponPreview, setCouponPreview] = useState<CouponPreviewState>({ kind: 'idle' })
  const [couponReloadKey, setCouponReloadKey] = useState(0)
  const couponAbortRef = useRef<AbortController | null>(null)
  const couponDebounceRef = useRef<number | null>(null)
  const subTotal = state.kind === 'ok' ? state.cart.subTotal : null
  // `useWatch` (not the render-phase `watch` helper) is the project idiom
  // (see `ProductsPage`): it reads the same react-hook-form store but is
  // React-Compiler-memoizable, so editing a field recomputes the summary
  // without a fresh `incompatible-library` lint warning. Every field that
  // can change the price (province drives shipping, coupon drives the
  // discount, the rest ride along so one request always carries the whole
  // address) re-triggers the debounced recompute.
  const province = useWatch({ control, name: 'shippingProvince' })
  const city = useWatch({ control, name: 'shippingCity' })
  const addressLine = useWatch({ control, name: 'shippingAddressLine' })
  const postalCode = useWatch({ control, name: 'shippingPostalCode' })
  const couponCode = useWatch({ control, name: 'couponCode' })

  // Debounce: the summary is a network round-trip now (it was a local mock),
  // so rapid typing must not fire one request per keystroke — each edit
  // resets the 300ms timer; only the settled value is sent.
  const debounceRef = useRef<number | null>(null)

  const recompute = useCallback(() => {
    if (debounceRef.current) window.clearTimeout(debounceRef.current)
    debounceRef.current = window.setTimeout(async () => {
      const values = getValues()
      // In this mock phase the live coupon verdict (the mock `coupons` client
      // evaluated against the cart subtotal) is the coupon authority. While a
      // code is typed we let it own the coupon line and skip the real summary
      // call (which 404s the mock-minted cart) so the two never read as one
      // confusing message. F059 restores the single server-side summary.
      if (values.couponCode.trim() !== '') {
        setSummary(null)
        setSummaryError(null)
        return
      }
      if (!values.shippingProvince) {
        setSummary(null)
        setSummaryError(null)
        return
      }
      try {
        const result = await fetchCheckoutSummary(tenantId, {
          shippingProvince: values.shippingProvince,
          shippingCity: values.shippingCity,
          shippingAddressLine: values.shippingAddressLine,
          shippingPostalCode: values.shippingPostalCode,
          couponCode: values.couponCode || null,
        })
        setSummary(result)
        setSummaryError(null)
      } catch (error) {
        setSummary(null)
        if (error instanceof CheckoutValidationError) {
          // The API's distinct, field-keyed messages (e.g. "This tenant does
          // not ship to the selected province." / "This coupon code is not
          // valid.") — surfaced verbatim so an unshippable province and a dead
          // coupon read as different problems, per the task acceptance.
          setSummaryError(Object.values(error.fieldErrors).join(' '))
        } else {
          setSummaryError(error instanceof Error ? error.message : 'خطایی رخ داد.')
        }
      }
    }, 300)
  }, [tenantId, getValues])

  useEffect(() => {
    void recompute()
  }, [recompute, province, city, addressLine, postalCode, couponCode])

  // S37: evaluate the typed coupon code against the live cart subtotal (mock).
  const typedCode = (couponCode ?? '').trim()
  const couponActive = state.kind === 'ok' && typedCode !== ''
  useEffect(() => {
    if (couponDebounceRef.current) window.clearTimeout(couponDebounceRef.current)
    couponAbortRef.current?.abort()
    if (!couponActive || subTotal === null) {
      setCouponPreview({ kind: 'idle' })
      return
    }
    setCouponPreview({ kind: 'loading' })
    const controller = new AbortController()
    couponAbortRef.current = controller
    const subtotalNow = subTotal
    couponDebounceRef.current = window.setTimeout(async () => {
      try {
        const list = await couponsClient.list(tenantId, { pageNumber: 1, pageSize: 100 }, controller.signal)
        if (controller.signal.aborted) return
        const coupon = findCouponByCode(list.coupons, typedCode)
        if (coupon === null) {
          setCouponPreview({ kind: 'rejected', message: COUPON_REASON_MESSAGES.coupon_not_found })
          return
        }
        const result = evaluateCouponPreview(coupon, subtotalNow, Date.now())
        if (result.status === 'applied') {
          setCouponPreview({ kind: 'applied', discountAmount: result.discountAmount, code: coupon.code })
        } else {
          setCouponPreview({ kind: 'rejected', message: COUPON_REASON_MESSAGES[result.reasonCode] })
        }
      } catch {
        if (controller.signal.aborted) return
        // Any failure to fetch the list is the unavailable-with-retry state.
        setCouponPreview({ kind: 'unavailable' })
      }
    }, 300)
    return () => {
      if (couponDebounceRef.current) window.clearTimeout(couponDebounceRef.current)
    }
  }, [couponActive, subTotal, typedCode, couponsClient, tenantId, couponReloadKey])
  useEffect(() => () => couponAbortRef.current?.abort(), [])

  // A coupon verdict (applied or a distinct rejection) supersedes a stale
  // summary error so the two never read as one confusing message.
  useEffect(() => {
    if (couponPreview.kind === 'applied' || couponPreview.kind === 'rejected') {
      setSummaryError(null)
    }
  }, [couponPreview])

  // The "continue to order review" action. S42/B046: a 429 (from the dev gate,
  // or a real one from the shared parser once the summary is wired) cools down
  // only THIS action — the entered form fields are never cleared.
  const handleContinue = useCallback(() => {
    // The continue button is only enabled on the `ok` lease state; this guard
    // keeps the callback type-safe (it is defined before the render guards).
    if (state.kind !== 'ok') return
    const values = getValues()
    const couponCodeValue = values.couponCode || null
    if (couponPreview.kind === 'applied') {
      const sub = state.cart.subTotal
      saveOrderDraft(tenantId, {
        customerName: values.customerName,
        customerPhone: values.customerPhone,
        shippingProvince: values.shippingProvince,
        shippingCity: values.shippingCity,
        shippingAddressLine: values.shippingAddressLine,
        shippingPostalCode: values.shippingPostalCode,
        couponCode: couponCodeValue,
        subTotal: sub,
        discountAmount: couponPreview.discountAmount,
        shippingCost: 0,
        grandTotal: Math.max(0, sub - couponPreview.discountAmount),
      })
      navigate(`/shop/${tenantId}/order-review`)
      return
    }
    const summaryNow = summary
    if (!summaryNow) return
    saveOrderDraft(tenantId, {
      customerName: values.customerName,
      customerPhone: values.customerPhone,
      shippingProvince: values.shippingProvince,
      shippingCity: values.shippingCity,
      shippingAddressLine: values.shippingAddressLine,
      shippingPostalCode: values.shippingPostalCode,
      couponCode: couponCodeValue,
      ...summaryNow,
    })
    navigate(`/shop/${tenantId}/order-review`)
  }, [getValues, couponPreview, state, tenantId, navigate, summary])

  // ---- lease-gated render ----

  if (state.kind === 'expired') {
    return (
      <section aria-label="تسویه حساب" className="space-y-4">
        <h1 className="text-2xl font-semibold">تسویه حساب</h1>
        <CartLeaseRecovery tenantId={tenantId} />
      </section>
    )
  }

  if (state.kind === 'empty' || state.kind === 'notFound') {
    return (
      <section aria-label="تسویه حساب" className="space-y-4 text-center">
        <h1 className="text-2xl font-semibold">تسویه حساب</h1>
        <p className="text-lg font-semibold">سبد خرید شما خالی است</p>
        <p className="text-sm text-muted-foreground">برای ادامه تسویه حساب، ابتدا محصولی به سبد اضافه کنید.</p>
        <div className="pt-1">
          <Link to={`/shop/${tenantId}`}>
            <Button type="button">بازگشت به فروشگاه</Button>
          </Link>
        </div>
      </section>
    )
  }

  if (state.kind === 'loading' || state.kind === 'unavailable') {
    return (
      <section aria-label="تسویه حساب" className="space-y-4" aria-busy={state.kind === 'loading'}>
        <h1 className="text-2xl font-semibold">تسویه حساب</h1>
        {state.kind === 'unavailable' ? (
          <div role="alert" className="rounded-xl border border-destructive/40 bg-destructive/10 p-5">
            <div className="space-y-1">
              <p className="text-sm font-semibold">اتصال برقرار نشد</p>
              <p className="text-sm leading-6 text-muted-foreground">
                در این لحظه به فروشگاه دسترسی نداریم. کمی بعد دوباره تلاش کنید.
              </p>
              <div className="pt-1">
                <Button type="button" onClick={() => reload()}>
                  تلاش دوباره
                </Button>
              </div>
            </div>
          </div>
        ) : (
          <div className="space-y-4" aria-hidden="true">
            <div className="h-8 w-56 animate-pulse rounded-md bg-muted" />
            <div className="h-64 animate-pulse rounded-xl bg-muted" />
          </div>
        )}
      </section>
    )
  }

  // state.kind === 'ok'
  return (
    <section aria-label="تسویه حساب" className="grid gap-8 md:grid-cols-2">
      <form className="space-y-4" noValidate>
        <h1 className="text-2xl font-semibold">اطلاعات ارسال</h1>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-name">نام و نام خانوادگی</label>
          <TextInput id="checkout-name" {...register('customerName')} />
          {errors.customerName && <p className="mt-2 text-sm text-destructive">{errors.customerName.message}</p>}
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-phone">شماره تماس</label>
          <TextInput id="checkout-phone" {...register('customerPhone')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-province">استان</label>
          <TextInput id="checkout-province" {...register('shippingProvince')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-city">شهر</label>
          <TextInput id="checkout-city" {...register('shippingCity')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-address">آدرس</label>
          <TextInput id="checkout-address" {...register('shippingAddressLine')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-postal">کد پستی</label>
          <TextInput id="checkout-postal" {...register('shippingPostalCode')} />
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="checkout-coupon">کد تخفیف</label>
          <TextInput id="checkout-coupon" {...register('couponCode')} />
        </div>
      </form>

      <div className="space-y-4 rounded-xl border border-border bg-surface p-6 shadow-soft">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-semibold">خلاصه سفارش</h2>
          <CartLeaseCountdown expiresAtUtc={state.cart.expiresAtUtc} onExpiry={markLocalExpiry} />
        </div>
        {summaryError && <p role="alert" className="text-sm font-semibold text-destructive">{summaryError}</p>}

        <CouponVerdict preview={couponPreview} onRetry={() => setCouponReloadKey((k) => k + 1)} />

        {/* Mock-phase coupon preview: with a code typed, the mock evaluates the
            discount against the live cart subtotal, so we show the goods
            subtotal, the applied discount and the resulting total (shipping is
            deferred to F059's single server-side summary). */}
        {couponPreview.kind === 'applied' && (
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{state.cart.subTotal.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>تخفیف <bdi dir="ltr" className="text-muted-foreground">({couponPreview.code})</bdi></dt><dd className="text-success">-{couponPreview.discountAmount.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{Math.max(0, state.cart.subTotal - couponPreview.discountAmount).toLocaleString('fa-IR')}</dd></div>
          </dl>
        )}
        {summary && (
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{summary.subTotal.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>تخفیف</dt><dd>{summary.discountAmount.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{summary.shippingCost.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{summary.grandTotal.toLocaleString('fa-IR')}</dd></div>
          </dl>
        )}
        {/* S42/B046: visible per-action cooldown while the continue action is disabled. */}
        {coolingDown && <RateLimitCountdown remainingSeconds={remainingSeconds} actionLabel="ادامه تسویه" />}
        <Button
          type="button"
          className="w-full"
          disabled={!summary && couponPreview.kind !== 'applied' || coolingDown}
          onClick={() => {
            try {
              // S42/B046: the dev throttle gate (dev-only; a no-op in production)
              // throws the one generic 429 for the `checkout` action so the
              // cooldown is reviewable — before the draft is saved/navigated.
              assertNotRateLimited('checkout')
              handleContinue()
            } catch (error) {
              if (error instanceof ShopClientError && isRateLimitedProblem(error.problem)) {
                startCooldown(error.problem.retryAfterSeconds)
              }
            }
          }}
        >
          ادامه به بررسی سفارش
        </Button>
      </div>
    </section>
  )
}

/**
 * The coupon field's verdict, with a stable footprint (no layout shift between
 * states). `loading` shows an in-line spinner; `applied` is an honest
 * success tint; `rejected` is a distinct destructive message (one per B041
 * reason code); `unavailable` offers a retry. `idle` renders nothing.
 */
function CouponVerdict({ preview, onRetry }: { preview: CouponPreviewState; onRetry: () => void }) {
  if (preview.kind === 'idle') return null
  if (preview.kind === 'loading') {
    return (
      <p role="status" className="flex items-center gap-2 text-sm text-muted-foreground" aria-live="polite">
        <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
        در حال بررسی کد تخفیف…
      </p>
    )
  }
  if (preview.kind === 'applied') {
    return (
      <p role="status" className="flex items-center gap-2 rounded-md bg-success/10 px-3 py-2 text-sm font-medium text-success" aria-live="polite">
        <CircleCheck aria-hidden="true" className="size-4 shrink-0" />
        کد تخفیف اعمال شد.
      </p>
    )
  }
  if (preview.kind === 'rejected') {
    return <p role="alert" className="text-sm font-semibold text-destructive">{preview.message}</p>
  }
  return (
    <div role="alert" className="flex items-center justify-between gap-2 rounded-md bg-destructive/10 px-3 py-2 text-sm font-medium text-destructive">
      <span>هم‌اکنون نمی‌توانیم کد تخفیف را بررسی کنیم.</span>
      <Button type="button" className="px-2 py-1 text-xs" onClick={onRetry}>تلاش دوباره</Button>
    </div>
  )
}
