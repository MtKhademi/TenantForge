import { useCallback, useEffect, useState } from 'react'
import { useWatch, useForm } from 'react-hook-form'
import { Link, useParams } from 'react-router-dom'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { computeMockCheckoutSummary, type CheckoutSummary } from '@/features/shop/mockCheckoutSummary'

/**
 * S28 checkout (F036): the public storefront checkout page, mocked. An
 * address form (province, city, address line, postal code), a coupon-code
 * field and a live order summary that recomputes as the shopper edits the
 * form — against `mockCheckoutSummary`'s hardcoded data. The summary mirrors
 * B030's `CheckoutSummaryResponse` field-for-field, and the two named errors
 * (unshippable province, invalid coupon) match what the real endpoint will
 * throw, so F037 can swap the mock for `cartAdapter`-style real code without
 * touching the UI. No order is created; the "continue" link is a forward
 * reference to `/order-review` (F038), mirroring the cart→checkout link.
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
  const { register, control, formState: { errors } } = useForm<CheckoutFormValues>({
    resolver: zodResolver(checkoutSchema),
    defaultValues: {
      customerName: '', customerPhone: '', shippingProvince: '', shippingCity: '',
      shippingAddressLine: '', shippingPostalCode: '', couponCode: '',
    },
  })
  const [summary, setSummary] = useState<CheckoutSummary | null>(null)
  const [summaryError, setSummaryError] = useState<string | null>(null)
  // `useWatch` (not the render-phase `watch` helper) is the project idiom
  // (see `ProductsPage`): it reads the same react-hook-form store but is
  // React-Compiler-memoizable, so editing a field recomputes the summary
  // without a fresh `incompatible-library` lint warning.
  const province = useWatch({ control, name: 'shippingProvince' })
  const couponCode = useWatch({ control, name: 'couponCode' })

  const recompute = useCallback(async () => {
    if (!province) {
      setSummary(null)
      setSummaryError(null)
      return
    }
    try {
      setSummary(await computeMockCheckoutSummary(province, couponCode))
      setSummaryError(null)
    } catch (error) {
      setSummary(null)
      setSummaryError(error instanceof Error ? error.message : 'خطایی رخ داد.')
    }
  }, [province, couponCode])

  useEffect(() => {
    void recompute()
  }, [recompute])

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
        <h2 className="text-lg font-semibold">خلاصه سفارش</h2>
        {summaryError && <p role="alert" className="text-sm font-semibold text-destructive">{summaryError}</p>}
        {summary && (
          <dl className="space-y-2 text-sm">
            <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{summary.subTotal.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>تخفیف</dt><dd>{summary.discountAmount.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{summary.shippingCost.toLocaleString('fa-IR')}</dd></div>
            <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{summary.grandTotal.toLocaleString('fa-IR')}</dd></div>
          </dl>
        )}
        <Link to={`/shop/${tenantId}/order-review`} className="block">
          <Button type="button" className="w-full" disabled={!summary}>ادامه به بررسی سفارش</Button>
        </Link>
      </div>
    </section>
  )
}
