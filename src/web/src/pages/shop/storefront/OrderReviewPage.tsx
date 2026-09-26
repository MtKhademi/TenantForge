import { useCallback, useEffect, useState } from 'react'
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { placeOrderAndInitiatePayment } from '@/features/shop/orderAdapter'
import {
  loadOrderDraft,
  savePlacedOrder,
  type OrderDraft,
} from '@/features/shop/orderDraftState'
import { useCartLease } from '@/features/shop/useCartLease'
import { CartLeaseCountdown, CartLeaseRecovery } from '@/features/shop/CartLeaseUi'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { CartLeaseExpired } from '@/features/shop/contracts/cartLeaseContract'
import { ShopClientError, isRateLimitedProblem } from '@/features/shop/contracts/shopContract'
import { assertNotRateLimited } from '@/features/shop/rateLimitScenario'
import { useRateLimitCooldown } from '@/features/shop/useRateLimitCooldown'
import { RateLimitCountdown } from '@/features/shop/RateLimitCooldown'

/**
 * S29 order review (F039, connected) + S36 reservation lease (F048, mock
 * phase).
 *
 * B040 makes order creation verify the cart lease first. In this mock phase
 * the page reads the stored cart through the shared `useCartLease` hook
 * (backed by the B040 mock now, the HTTP client after F058) and surfaces the
 * reservation countdown next to the review header:
 *
 * - a `410 shop_cart_expired` (thrown as `CartLeaseExpired`) — on the read,
 *   or when order creation itself rejects — swaps the page to the shared
 *   recovery panel, clearing ONLY this tenant's cart id and order draft;
 * - a missing/empty cart is the neutral empty state and a network failure is
 *   the unavailable state with a retry — neither is an expiry;
 * - the countdown is display-only: no polling, no auto-extend.
 *
 * The review content itself (the address/coupon inputs and computed summary
 * the checkout page collected, carried by `orderDraftState` — now per-tenant)
 * and the "place order" action (B031 order creation + B032 sandbox payment)
 * are unchanged: with no draft (direct visit, stale link) it redirects back
 * to checkout; a stock-race or other failure surfaces as a clear error.
 */
export function OrderReviewPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const navigate = useNavigate()
  const { cartLease } = useShopClients()
  const { state, reload, markLocalExpiry, forceExpired } = useCartLease(tenantId, cartLease)

  const [draft, setDraft] = useState<OrderDraft | null | undefined>(undefined)
  const [isPlacing, setIsPlacing] = useState(false)
  const [placeError, setPlaceError] = useState<string | null>(null)
  // S42/B046: a 429 on order creation disables ONLY the place-order action for
  // `retryAfterSeconds` with a visible countdown; the review content (address,
  // totals, the stored draft) is preserved and never auto-submitted.
  const { coolingDown, remainingSeconds, start: startCooldown } = useRateLimitCooldown()

  useEffect(() => {
    setDraft(loadOrderDraft(tenantId))
  }, [tenantId])

  const handlePlaceOrder = useCallback(async () => {
    if (!draft) return
    setIsPlacing(true)
    setPlaceError(null)
    try {
      // S42/B046: the dev throttle gate (dev-only; a no-op in production) throws
      // the one generic 429 for the `order` action so the cooldown is reviewable.
      assertNotRateLimited('order')
      const { order, payment } = await placeOrderAndInitiatePayment(tenantId, draft)
      savePlacedOrder(tenantId, {
        orderId: order.orderId,
        orderNumber: order.orderNumber,
        trackingCode: order.trackingCode,
        gatewayReference: payment.gatewayReference,
      })
      navigate(`/shop/${tenantId}/bank`)
    } catch (error) {
      // S42/B046: a real (or simulated) 429 cools down only this action.
      if (error instanceof ShopClientError && isRateLimitedProblem(error.problem)) {
        startCooldown(error.problem.retryAfterSeconds)
        return
      }
      if (error instanceof CartLeaseExpired) {
        // The lease died between the review load and order creation: the
        // server is authoritative here, so flip to recovery unconditionally
        // (clears only this tenant's cart id and order draft).
        forceExpired()
        return
      }
      setPlaceError(error instanceof Error ? error.message : 'خطایی رخ داد.')
    } finally {
      setIsPlacing(false)
    }
  }, [draft, tenantId, navigate, forceExpired, startCooldown])

  // ---- draft / lease gated render ----

  if (draft === undefined) return null
  if (draft === null) return <Navigate to={`/shop/${tenantId}/checkout`} replace />

  if (state.kind === 'expired') {
    return (
      <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-4">
        <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>
        <CartLeaseRecovery tenantId={tenantId} />
      </section>
    )
  }

  if (state.kind === 'empty' || state.kind === 'notFound') {
    return (
      <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-4 text-center">
        <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>
        <p className="text-lg font-semibold">سبد خرید شما خالی است</p>
        <p className="text-sm text-muted-foreground">برای ادامه سفارش، ابتدا محصولی به سبد اضافه کنید.</p>
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
      <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-4" aria-busy={state.kind === 'loading'}>
        <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>
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
            <div className="h-24 animate-pulse rounded-xl bg-muted" />
            <div className="h-32 animate-pulse rounded-xl bg-muted" />
          </div>
        )}
      </section>
    )
  }

  // state.kind === 'ok'
  return (
    <section aria-label="بررسی نهایی سفارش" className="max-w-xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">بررسی نهایی سفارش</h1>
        <CartLeaseCountdown expiresAtUtc={state.cart.expiresAtUtc} onExpiry={markLocalExpiry} />
      </div>

      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="font-semibold">{draft.customerName}</p>
        <p className="text-sm text-muted-foreground">{draft.customerPhone}</p>
        <p className="mt-2 text-sm">
          {draft.shippingProvince}، {draft.shippingCity}، {draft.shippingAddressLine} — {draft.shippingPostalCode}
        </p>
      </div>

      <dl className="space-y-2 rounded-xl border border-border bg-surface p-5 text-sm shadow-soft">
        <div className="flex justify-between"><dt>جمع کل محصولات</dt><dd>{draft.subTotal.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>تخفیف</dt><dd>{draft.discountAmount.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between"><dt>هزینه ارسال</dt><dd>{draft.shippingCost.toLocaleString('fa-IR')}</dd></div>
        <div className="flex justify-between border-t border-border pt-2 font-semibold"><dt>مجموع نهایی</dt><dd>{draft.grandTotal.toLocaleString('fa-IR')}</dd></div>
      </dl>

      {placeError && <p role="alert" className="text-sm font-semibold text-destructive">{placeError}</p>}

      {/* S42/B046: visible per-action cooldown while order creation is disabled. */}
      {coolingDown && <RateLimitCountdown remainingSeconds={remainingSeconds} actionLabel="ثبت سفارش" />}

      <Button
        type="button"
        className="w-full"
        disabled={isPlacing || coolingDown}
        onClick={() => void handlePlaceOrder()}
      >
        {isPlacing ? 'در حال ثبت…' : 'ثبت سفارش و پرداخت'}
      </Button>
    </section>
  )
}
