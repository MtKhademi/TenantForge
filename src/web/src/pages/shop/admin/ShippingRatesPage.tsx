import { CircleCheck, Loader2, RefreshCw, Truck } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { mockShippingAndCouponAdapter } from '@/features/shop/mockShippingAndCouponAdapter'
import type { ShippingRate } from '@/features/shop/shopCheckoutAdminTypes'
import { cn } from '@/lib/utils'

/**
 * S28 (F034): admin shipping-rate management, mocked (F035 connects it to
 * B029's real API without changing this page's structure). Follows
 * `CategoriesPage.tsx`'s pattern: header, toggle form, skeleton, empty
 * state, table.
 *
 * `setShippingRate` is an upsert by `provinceName` — there is no separate
 * province lookup table anywhere in this module, so submitting an existing
 * province's name updates its cost instead of creating a duplicate row.
 */

const rateSchema = z.object({
  provinceName: z.string().min(1, 'نام استان الزامی است.').max(120),
  cost: z.coerce.number().min(0, 'هزینه ارسال باید صفر یا بیشتر باشد.'),
})
type RateFormValues = z.infer<typeof rateSchema>

export function ShippingRatesPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [rates, setRates] = useState<ShippingRate[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const provinceInputRef = useRef<HTMLInputElement | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm({
    resolver: zodResolver(rateSchema),
    defaultValues: { provinceName: '', cost: 0 },
  })

  const loadRates = useCallback(() => {
    setIsBusy(true)
    return mockShippingAndCouponAdapter
      .listShippingRates(tenantId)
      .then(setRates)
      .finally(() => setIsBusy(false))
  }, [tenantId])

  useEffect(() => {
    void loadRates()
  }, [loadRates])

  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => provinceInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const closeForm = useCallback(() => {
    setFormOpen(false)
    reset()
    setSuccess(null)
    toggleButtonRef.current?.focus()
  }, [reset])

  const openForm = useCallback(() => {
    reset()
    setSuccess(null)
    setFormOpen(true)
  }, [reset])

  const onSubmit = useCallback(
    async (values: RateFormValues) => {
      setIsSubmitting(true)
      setSuccess(null)
      try {
        const saved = await mockShippingAndCouponAdapter.setShippingRate(tenantId, values)
        setSuccess(`نرخ ارسال استان ${saved.provinceName} ذخیره شد.`)
        reset()
        setFormOpen(false)
        void loadRates()
      } finally {
        setIsSubmitting(false)
      }
    },
    [tenantId, reset, loadRates],
  )

  const isLoading = rates === null && isBusy
  const isEmpty = rates !== null && rates.length === 0
  const provinceField = register('provinceName')

  return (
    <DashboardShell>
      <section aria-label="نرخ‌های ارسال" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">نرخ‌های ارسال</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              هزینه ارسال به هر استان را تعیین کنید. استانی که نرخی برایش ثبت نشود، ارسال ندارد.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {rates !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست نرخ‌های ارسال"
                className="px-3"
                disabled={isBusy}
                onClick={() => void loadRates()}
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
              aria-controls="shipping-rate-form"
              disabled={isSubmitting}
              onClick={() => (formOpen ? closeForm() : openForm())}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <Truck aria-hidden="true" className="me-2 size-4" />
                  تعیین نرخ ارسال
                </>
              )}
            </Button>
          </div>
        </div>

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
            id="shipping-rate-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleSubmit(onSubmit)(event)}
            noValidate
          >
            <h3 className="text-base font-semibold">تعیین نرخ ارسال استان</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              اگر استان قبلاً نرخ داشته باشد، هزینه آن به‌روزرسانی می‌شود.
            </p>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="rate-province">
                  استان
                </label>
                <TextInput
                  id="rate-province"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.provinceName)}
                  aria-describedby={errors.provinceName ? 'rate-province-error' : undefined}
                  {...provinceField}
                  ref={(node) => {
                    provinceField.ref(node)
                    provinceInputRef.current = node
                  }}
                />
                {errors.provinceName && (
                  <p className="mt-2 text-sm text-destructive" id="rate-province-error">
                    {errors.provinceName.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="rate-cost">
                  هزینه (تومان)
                </label>
                <TextInput
                  id="rate-cost"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  step={1000}
                  aria-invalid={Boolean(errors.cost)}
                  aria-describedby={errors.cost ? 'rate-cost-error' : undefined}
                  {...register('cost')}
                />
                {errors.cost && (
                  <p className="mt-2 text-sm text-destructive" id="rate-cost-error">
                    {errors.cost.message}
                  </p>
                )}
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
                  'ذخیره'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeForm} disabled={isSubmitting}>
                لغو
              </SecondaryButton>
            </div>
          </form>
        )}

        {isLoading && <ShippingRatesSkeleton />}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <Truck aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">هیچ نرخ ارسالی تعیین نشده است</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین نرخ ارسال را با دکمه «تعیین نرخ ارسال» ثبت کنید.
            </p>
          </div>
        )}

        {rates !== null && rates.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[24rem] text-sm">
                <caption className="sr-only">فهرست نرخ‌های ارسال</caption>
                <thead>
                  <tr className="border-b border-border text-start">
                    <th scope="col" className="px-4 py-3 text-start font-semibold">استان</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">هزینه</th>
                  </tr>
                </thead>
                <tbody>
                  {rates.map((rate) => (
                    <tr key={rate.id} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3">
                        <bdi className="font-medium">{rate.provinceName}</bdi>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">
                        {rate.cost.toLocaleString('fa-IR')} تومان
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

/** Loading placeholder with the table's footprint so data causes no layout shift. */
function ShippingRatesSkeleton() {
  return (
    <div
      aria-busy="true"
      className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft"
    >
      <p className="sr-only">
        در حال بارگذاری فهرست نرخ‌های ارسال
        <span aria-hidden="true">…</span>
      </p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-28 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-20 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}
