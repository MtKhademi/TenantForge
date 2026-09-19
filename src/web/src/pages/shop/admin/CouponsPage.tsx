import { BadgePercent, CircleCheck, Loader2, RefreshCw, TriangleAlert } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import {
  createShippingAndCouponAdapter,
  ShopForbiddenError,
  ShopValidationError,
} from '@/features/shop/shippingAndCouponAdapter'
import { CouponConflictError, type Coupon } from '@/features/shop/shopCheckoutAdminTypes'
import { cn } from '@/lib/utils'

/**
 * S28 (F035): admin coupon management, connected to B029's real API.
 * Follows `ShippingRatesPage.tsx`'s pattern exactly.
 *
 * There is no edit/reactivate action on this page — only create and
 * deactivate — matching B029's real scope exactly.
 */

const couponSchema = z.object({
  code: z.string().min(1, 'کد تخفیف الزامی است.').max(40),
  discountType: z.enum(['Percentage', 'FixedAmount']),
  discountValue: z.coerce.number().positive('مقدار تخفیف باید بزرگ‌تر از صفر باشد.'),
  expiresAtUtc: z.string().optional(),
})
type CouponFormValues = z.infer<typeof couponSchema>

export function CouponsPage() {
  const { session, signOut } = useAuth()
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [coupons, setCoupons] = useState<Coupon[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [listFailure, setListFailure] = useState<'unavailable' | 'forbidden' | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [deactivatingId, setDeactivatingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const codeInputRef = useRef<HTMLInputElement | null>(null)
  // Fresh refs so the (stable) callbacks below always read the current token
  // and sign-out action without re-creating the data source per render.
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)
  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  // One bound data-source instance per render, carrying the current token.
  // Event handlers read `adapter` directly; the effect-driven load callback
  // reads `adapterRef` so it stays referentially stable.
  const adapter = createShippingAndCouponAdapter(session?.accessToken ?? '')
  const adapterRef = useRef(adapter)
  useEffect(() => {
    adapterRef.current = adapter
  }, [adapter])

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm({
    resolver: zodResolver(couponSchema),
    defaultValues: { code: '', discountType: 'Percentage' as const, discountValue: 0, expiresAtUtc: '' },
  })

  const handleListFailure = useCallback((error: unknown) => {
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    setListFailure(error instanceof ShopForbiddenError ? 'forbidden' : 'unavailable')
  }, [])

  const loadCoupons = useCallback(() => {
    setIsBusy(true)
    setListFailure(null)
    setActionError(null)
    return adapterRef.current
      .listCoupons(tenantId)
      .then(setCoupons)
      .catch((error) => handleListFailure(error))
      .finally(() => setIsBusy(false))
  }, [tenantId, handleListFailure])

  useEffect(() => {
    void loadCoupons()
  }, [loadCoupons])

  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => codeInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const closeForm = useCallback(() => {
    setFormOpen(false)
    reset()
    clearErrors()
    setSuccess(null)
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  const openForm = useCallback(() => {
    reset()
    clearErrors()
    setSuccess(null)
    setActionError(null)
    setFormOpen(true)
  }, [reset, clearErrors])

  const onSubmit = useCallback(
    async (values: CouponFormValues) => {
      setIsSubmitting(true)
      setSuccess(null)
      setActionError(null)
      try {
        const created = await adapter.createCoupon(tenantId, {
          code: values.code,
          discountType: values.discountType,
          discountValue: values.discountValue,
          expiresAtUtc: values.expiresAtUtc ? new Date(values.expiresAtUtc).toISOString() : null,
        })
        setSuccess(`کد تخفیف ${created.code} ایجاد شد.`)
        reset()
        setFormOpen(false)
        void loadCoupons()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof CouponConflictError) {
          setError('code', { message: error.message })
          codeInputRef.current?.focus()
        } else if (error instanceof ShopValidationError) {
          for (const [field, message] of Object.entries(error.fieldErrors)) {
            if (field === 'code' || field === 'discountType' || field === 'discountValue') {
              setError(field, { message })
            }
          }
          codeInputRef.current?.focus()
        } else if (error instanceof ShopForbiddenError) {
          setError('code', { message: error.message })
          codeInputRef.current?.focus()
        }
      } finally {
        setIsSubmitting(false)
      }
    },
    [tenantId, adapter, reset, setError, loadCoupons],
  )

  const onDeactivate = useCallback(
    async (couponId: string) => {
      setDeactivatingId(couponId)
      setSuccess(null)
      setActionError(null)
      try {
        const updated = await adapter.deactivateCoupon(tenantId, couponId)
        setSuccess(`کد تخفیف ${updated.code} غیرفعال شد.`)
        void loadCoupons()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setActionError(
          error instanceof ShopForbiddenError
            ? error.message
            : 'هم‌اکنون نمی‌توانیم کد تخفیف را غیرفعال کنیم. دوباره تلاش کنید.',
        )
      } finally {
        setDeactivatingId(null)
      }
    },
    [tenantId, adapter, loadCoupons],
  )

  const isLoading = coupons === null && isBusy
  const listError = coupons === null && !isBusy && listFailure !== null
  const isEmpty = coupons !== null && coupons.length === 0
  const codeField = register('code')

  return (
    <DashboardShell>
      <section aria-label="کدهای تخفیف" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">کدهای تخفیف</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              کدهای تخفیف فروشگاه را ایجاد و در صورت نیاز غیرفعال کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {coupons !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست کدهای تخفیف"
                className="px-3"
                disabled={isBusy}
                onClick={() => void loadCoupons()}
              >
                <RefreshCw
                  aria-hidden="true"
                  className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')}
                />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            )}
            <Button
              type="button"
              ref={toggleButtonRef}
              className={cn(formOpen && 'bg-primary hover:bg-primary')}
              aria-expanded={formOpen}
              aria-controls="coupon-form"
              disabled={isSubmitting}
              onClick={() => (formOpen ? closeForm() : openForm())}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <BadgePercent aria-hidden="true" className="me-2 size-4" />
                  ایجاد کد تخفیف
                </>
              )}
            </Button>
          </div>
        </div>

        {actionError && (
          <div
            role="alert"
            className="flex items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-2 text-sm font-medium text-destructive"
          >
            <TriangleAlert aria-hidden="true" className="size-4" />
            {actionError}
          </div>
        )}

        {success && (
          <div
            role="status"
            className="flex items-center gap-2 rounded-lg border border-success/40 bg-success/10 px-4 py-2 text-sm font-medium text-success"
          >
            <CircleCheck aria-hidden="true" className="size-4" />
            {success}
          </div>
        )}

        {formOpen && (
          <form
            id="coupon-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleSubmit(onSubmit)(event)}
            noValidate
          >
            <h3 className="text-base font-semibold">ایجاد کد تخفیف جدید</h3>
            <p className="mt-1 text-sm text-muted-foreground">کد تخفیف با وضعیت «فعال» ایجاد می‌شود.</p>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-code">
                  کد
                </label>
                <TextInput
                  id="coupon-code"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.code)}
                  aria-describedby={errors.code ? 'coupon-code-error' : undefined}
                  {...codeField}
                  ref={(node) => {
                    codeField.ref(node)
                    codeInputRef.current = node
                  }}
                />
                {errors.code && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-code-error">
                    {errors.code.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-discount-type">
                  نوع تخفیف
                </label>
                <select
                  id="coupon-discount-type"
                  {...register('discountType')}
                  className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-sm text-foreground focus-visible:border-ring"
                >
                  <option value="Percentage">درصدی</option>
                  <option value="FixedAmount">مقدار ثابت</option>
                </select>
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-discount-value">
                  مقدار تخفیف
                </label>
                <TextInput
                  id="coupon-discount-value"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  step={1}
                  aria-invalid={Boolean(errors.discountValue)}
                  aria-describedby={errors.discountValue ? 'coupon-discount-value-error' : undefined}
                  {...register('discountValue')}
                />
                {errors.discountValue && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-discount-value-error">
                    {errors.discountValue.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-expires">
                  تاریخ انقضا (اختیاری)
                </label>
                <TextInput id="coupon-expires" type="date" {...register('expiresAtUtc')} />
              </div>
            </div>

            <div className="mt-5 flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ذخیره
                  </>
                ) : (
                  'ایجاد کد تخفیف'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeForm} disabled={isSubmitting}>
                لغو
              </SecondaryButton>
            </div>
          </form>
        )}

        {isLoading && <CouponsSkeleton />}

        {listError && (
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title={listFailure === 'forbidden' ? 'دسترسی مدیریت فروشگاه مجاز نیست' : 'فهرست کدهای تخفیف در دسترس نیست'}
            description={
              listFailure === 'forbidden'
                ? 'حساب فعلی مجوز مدیریت فروشگاه این مستأجر را ندارد.'
                : 'هم‌اکنون نمی‌توانیم کدهای تخفیف را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.'
            }
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={() => void loadCoupons()}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </Button>
            }
          />
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <BadgePercent aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">کد تخفیفی موجود نیست</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین کد تخفیف را با دکمه «ایجاد کد تخفیف» بسازید.
            </p>
          </div>
        )}

        {coupons !== null && coupons.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[40rem] text-sm">
                <caption className="sr-only">فهرست کدهای تخفیف</caption>
                <thead>
                  <tr className="border-b border-border text-start">
                    <th scope="col" className="px-4 py-3 text-start font-semibold">کد</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">نوع تخفیف</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">مقدار</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">انقضا</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                  </tr>
                </thead>
                <tbody>
                  {coupons.map((coupon) => (
                    <tr key={coupon.id} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3">
                        <bdi dir="ltr" className="font-medium">{coupon.code}</bdi>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {coupon.discountType === 'Percentage' ? 'درصدی' : 'مقدار ثابت'}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {coupon.discountType === 'Percentage'
                          ? `${coupon.discountValue.toLocaleString('fa-IR')}٪`
                          : `${coupon.discountValue.toLocaleString('fa-IR')} تومان`}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {coupon.expiresAtUtc ? (
                          <bdi dir="ltr">{new Date(coupon.expiresAtUtc).toLocaleDateString('fa-IR')}</bdi>
                        ) : (
                          'بدون انقضا'
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <StatusBadge active={coupon.isActive} />
                      </td>
                      <td className="px-4 py-3">
                        <SecondaryButton
                          type="button"
                          disabled={!coupon.isActive || deactivatingId === coupon.id}
                          onClick={() => void onDeactivate(coupon.id)}
                        >
                          {deactivatingId === coupon.id ? (
                            <>
                              <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                              در حال غیرفعال‌سازی
                            </>
                          ) : (
                            'غیرفعال‌سازی'
                          )}
                        </SecondaryButton>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function StatusBadge({ active }: { active: boolean }) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        active ? 'bg-success/10 text-success' : 'bg-muted text-muted-foreground',
      )}
    >
      <span
        aria-hidden="true"
        className={cn('size-1.5 rounded-full', active ? 'bg-success' : 'bg-muted-foreground')}
      />
      {active ? 'فعال' : 'غیرفعال'}
    </span>
  )
}

/** Loading placeholder with the table's footprint so data causes no layout shift. */
function CouponsSkeleton() {
  return (
    <div
      aria-busy="true"
      className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft"
    >
      <p className="sr-only">
        در حال بارگذاری فهرست کدهای تخفیف
        <span aria-hidden="true">…</span>
      </p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-20 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-16 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}
