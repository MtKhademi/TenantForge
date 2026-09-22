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
import { useCartLease } from '@/features/shop/useCartLease'
import { saveOrderDraft } from '@/features/shop/orderDraftState'
import { CartLeaseCountdown, CartLeaseRecovery } from '@/features/shop/CartLeaseUi'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'

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

export function CheckoutPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const navigate = useNavigate()
  const { cartLease } = useShopClients()
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
        {summary && (
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{summary.subTotal.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>تخفیف</dt><dd>{summary.discountAmount.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{summary.shippingCost.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{summary.grandTotal.toLocaleString('fa-IR')}</dd></div>
          </dl>
        )}
        <Button
          type="button"
          className="w-full"
          disabled={!summary}
          onClick={() => {
            if (!summary) return
            const values = getValues()
            saveOrderDraft(tenantId, {
              customerName: values.customerName,
              customerPhone: values.customerPhone,
              shippingProvince: values.shippingProvince,
              shippingCity: values.shippingCity,
              shippingAddressLine: values.shippingAddressLine,
              shippingPostalCode: values.shippingPostalCode,
              couponCode: values.couponCode || null,
              ...summary,
            })
            navigate(`/shop/${tenantId}/order-review`)
          }}
        >
          ادامه به بررسی سفارش
        </Button>
      </div>
    </section>
  )
}
