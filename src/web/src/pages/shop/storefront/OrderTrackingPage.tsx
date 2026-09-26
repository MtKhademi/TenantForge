import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { zodResolver } from '@hookform/resolvers/zod'
import { Button } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { assertNotRateLimited } from '@/features/shop/rateLimitScenario'
import { useRateLimitCooldown } from '@/features/shop/useRateLimitCooldown'
import { RateLimitCountdown } from '@/features/shop/RateLimitCooldown'
import { lookupOrder } from '@/features/shop/orderLookupAdapter'
import type { OrderLookupResult } from '@/features/shop/orderLookupTypes'

const trackingSchema = z.object({
  trackingCode: z.string().min(1, 'کد پیگیری الزامی است.'),
  customerPhone: z.string().min(1, 'شماره تماس الزامی است.'),
})
type TrackingFormValues = z.infer<typeof trackingSchema>

/**
 * S30 guest order tracking (F041, connected to B033) + S42 rate-limit (F053).
 *
 * Both fields are always required together, and a failed lookup always shows
 * one generic message — never branching copy on which field was wrong. B033
 * returns the same 404 for a wrong phone and a made-up code, so `null`
 * (not found) and a server failure are the only two non-success outcomes, and
 * the server-failure case is a distinct, honest message rather than pretending
 * the order simply doesn't exist.
 *
 * S42 (F053): the lookup is one of B046's rate-limited actions. On a 429 the
 * lookup button alone is disabled for `retryAfterSeconds` with a visible
 * countdown (never auto-submitted) and the already-entered code/phone are
 * preserved — the rest of the page keeps working. A valid and an invalid
 * lookup render the SAME result card shape (same header, same body frame; only
 * the content differs) so no information leaks through layout differences.
 */
export function OrderTrackingPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<TrackingFormValues>({
    resolver: zodResolver(trackingSchema),
    defaultValues: { trackingCode: '', customerPhone: '' },
  })
  const [result, setResult] = useState<OrderLookupResult | null | undefined>(undefined)
  const [lookupError, setLookupError] = useState<string | null>(null)
  const { coolingDown, remainingSeconds, start: startCooldown } = useRateLimitCooldown()

  const onSubmit = async (values: TrackingFormValues) => {
    setLookupError(null)
    try {
      // S42/B046: the dev throttle gate (dev-only; a no-op in production) throws
      // the one generic 429 for the `lookup` action so the cooldown is reviewable.
      assertNotRateLimited('lookup')
      setResult(await lookupOrder(tenantId, values.trackingCode, values.customerPhone))
    } catch (error) {
      if (error instanceof ShopClientError && isRateLimitedProblem(error.problem)) {
        startCooldown(error.problem.retryAfterSeconds)
        setLookupError(null)
        return
      }
      setResult(null)
      setLookupError('هم‌اکنون نمی‌توانیم سفارش شما را پیدا کنیم. لطفاً دوباره تلاش کنید.')
    }
  }

  return (
    <section aria-label="پیگیری سفارش" className="mx-auto max-w-md space-y-6">
      <h1 className="text-2xl font-semibold">پیگیری سفارش</h1>

      <form className="space-y-4" onSubmit={handleSubmit(onSubmit)} noValidate>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="tracking-code">کد پیگیری</label>
          <TextInput
            id="tracking-code"
            autoComplete="off"
            aria-invalid={Boolean(errors.trackingCode)}
            aria-describedby={errors.trackingCode ? 'tracking-code-error' : undefined}
            {...register('trackingCode')}
          />
          {errors.trackingCode && (
            <p className="mt-2 text-sm text-destructive" id="tracking-code-error">{errors.trackingCode.message}</p>
          )}
        </div>
        <div>
          <label className="mb-2 block text-sm font-semibold" htmlFor="tracking-phone">شماره تماس</label>
          <TextInput
            id="tracking-phone"
            inputMode="tel"
            autoComplete="off"
            aria-invalid={Boolean(errors.customerPhone)}
            aria-describedby={errors.customerPhone ? 'tracking-phone-error' : undefined}
            {...register('customerPhone')}
          />
          {errors.customerPhone && (
            <p className="mt-2 text-sm text-destructive" id="tracking-phone-error">{errors.customerPhone.message}</p>
          )}
        </div>
        <Button type="submit" className="w-full" disabled={isSubmitting || coolingDown}>
          پیگیری
        </Button>
      </form>

      {/* Visible per-action cooldown while the lookup is disabled by a 429. */}
      {coolingDown && <RateLimitCountdown remainingSeconds={remainingSeconds} actionLabel="پیگیری سفارش" />}

      {lookupError && (
        <p role="alert" className="text-sm font-semibold text-destructive">{lookupError}</p>
      )}

      {/* One result card for BOTH a found order and a not-found lookup: the same
          header + body frame, different content — no layout difference leaks
          whether the input "existed". */}
      {result !== undefined && (
        <div className="space-y-4 rounded-xl border border-border bg-surface p-5 shadow-soft">
          <p className="font-semibold">
            {result
              ? <>سفارش <bdi dir="ltr">{result.orderNumber}</bdi> — {result.status}</>
              : 'نتیجه پیگیری'}
          </p>
          {result ? (
            <>
              <ul className="space-y-1 text-sm">
                {result.items.map((item, index) => (
                  <li key={index}>
                    {item.productNameSnapshot} ({item.variantLabelSnapshot}) × {item.quantity}
                  </li>
                ))}
              </ul>
              <p className="text-sm font-semibold">مجموع نهایی: {result.grandTotal.toLocaleString('fa-IR')} تومان</p>
            </>
          ) : (
            <p className="text-sm text-muted-foreground" role="status">
              سفارشی با این کد پیگیری و شماره تماس یافت نشد.
            </p>
          )}
        </div>
      )}
    </section>
  )
}
