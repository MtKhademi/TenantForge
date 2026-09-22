import { Minus, Plus, Trash2 } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { useCartLease } from '@/features/shop/useCartLease'
import { CartLeaseCountdown, CartLeaseRecovery } from '@/features/shop/CartLeaseUi'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import type { CartItem, CartResponse } from '@/features/shop/contracts/cartLeaseContract'
import { cn } from '@/lib/utils'

/**
 * S27 storefront cart (F033) + S36 reservation lease (F048, mock phase).
 *
 * Data flows only through the `cartLease` client slot (the B040 mock now, the
 * HTTP client after F058) — no fixture imports, no direct fetch. F048 adds the
 * reservation UX on top of the existing cart interactions:
 *
 * - a display-only countdown of the server-returned `expiresAtUtc`. It never
 *   polls and never auto-extends — only a real add/update/remove pushes the
 *   lease out (B040), and the countdown resets from the fresh value each
 *   mutation returns;
 * - the 410 `shop_cart_expired` (thrown as `CartLeaseExpired`) — or the local
 *   countdown reaching zero — swaps the page to the shared recovery panel,
 *   clearing ONLY this tenant's cart id and order draft (other tenants'
 *   entries are untouched) and offering a return to the catalog; the cart is
 *   never silently rebuilt;
 * - a plain `404` (cart/item not found) and `400` (invalid quantity) render
 *   their own distinct messages and are never treated as expiry; a network
 *   failure renders the unavailable state with a retry control;
 * - every request is abort-aware: unmounting cancels the in-flight request,
 *   and an aborted request never surfaces as an error.
 *
 * Read state is shared with the checkout and review pages through
 * `useCartLease`, which owns the abort/recovery behavior so all three agree.
 */

export function CartPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { cartLease } = useShopClients()
  const { state, reload, applyMutation, markLocalExpiry, setBusy } = useCartLease(
    tenantId,
    cartLease,
  )

  const [mutationError, setMutationError] = useState<string | null>(null)
  const [busyItemId, setBusyItemId] = useState<string | null>(null)

  const runMutation = useCallback(
    (operation: (signal: AbortSignal) => Promise<CartResponse>, itemId: string) => {
      setMutationError(null)
      setBusyItemId(itemId)
      setBusy(true)
      const controller = new AbortController()
      operation(controller.signal)
        .then((updated) => {
          if (controller.signal.aborted) return
          applyMutation(updated)
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) return
          setMutationError(cartMutationMessage(error))
        })
        .finally(() => {
          setBusy(false)
          setBusyItemId((current) => (current === itemId ? null : current))
        })
    },
    [applyMutation, setBusy],
  )

  // ---- render ----

  if (state.kind === 'expired') {
    return (
      <section aria-label="سبد خرید" className="space-y-4">
        <h1 className="text-2xl font-semibold">سبد خرید</h1>
        <CartLeaseRecovery tenantId={tenantId} />
      </section>
    )
  }

  if (state.kind === 'loading') {
    return (
      <section aria-label="سبد خرید" className="space-y-6" aria-busy="true">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h1 className="text-2xl font-semibold">سبد خرید</h1>
          <span className="inline-block h-7 w-40 animate-pulse rounded-md bg-muted" aria-hidden="true" />
        </div>
        <div className="h-24 animate-pulse rounded-xl bg-muted" aria-hidden="true" />
        <div className="h-16 animate-pulse rounded-xl bg-muted" aria-hidden="true" />
        <p className="text-sm text-muted-foreground">در حال بارگذاری سبد خرید…</p>
      </section>
    )
  }

  if (state.kind === 'empty') {
    return (
      <section aria-label="سبد خرید" className="space-y-4 text-center">
        <p className="text-lg font-semibold">سبد خرید شما خالی است</p>
        <Link to={`/shop/${tenantId}`} className="inline-block">
          <Button type="button">بازگشت به فروشگاه</Button>
        </Link>
      </section>
    )
  }

  if (state.kind === 'notFound' || state.kind === 'unavailable') {
    return (
      <section aria-label="سبد خرید" className="space-y-4">
        <h1 className="text-2xl font-semibold">سبد خرید</h1>
        <LeaseErrorPanel state={state} onRetry={reload} tenantId={tenantId} />
      </section>
    )
  }

  // state.kind === 'ok'
  const cart = state.cart
  return (
    <section aria-label="سبد خرید" className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold">سبد خرید</h1>
        <CartLeaseCountdown expiresAtUtc={cart.expiresAtUtc} onExpiry={markLocalExpiry} />
      </div>

      {mutationError && (
        <p role="alert" className="text-sm font-semibold text-destructive">
          {mutationError}
        </p>
      )}

      <div className="divide-y divide-border rounded-xl border border-border bg-surface shadow-soft">
        {cart.items.map((item) => (
          <CartItemRow
            key={item.id}
            item={item}
            busy={busyItemId === item.id}
            onQuantityCommit={(quantity) =>
              runMutation((s) => cartLease.updateItem(tenantId, item.id, quantity, s), item.id)
            }
            onRemove={() => runMutation((s) => cartLease.removeItem(tenantId, item.id, s), item.id)}
          />
        ))}
      </div>

      <div className="flex items-center justify-between rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="font-semibold">جمع کل</p>
        <p className="font-semibold">{cart.subTotal.toLocaleString('fa-IR')} تومان</p>
      </div>

      <Link to={`/shop/${tenantId}/checkout`} className="block">
        <Button type="button" className="w-full">
          ادامه به تسویه حساب
        </Button>
      </Link>
    </section>
  )
}

function cartMutationMessage(error: unknown): string {
  if (error instanceof Error && error.message.length > 0) return error.message
  return 'عملیات روی سبد خرید انجام نشد.'
}

function LeaseErrorPanel({
  state,
  onRetry,
  tenantId,
}: {
  state: { kind: 'notFound'; message: string } | { kind: 'unavailable' }
  onRetry: () => void
  tenantId: string
}) {
  const isUnavailable = state.kind === 'unavailable'
  return (
    <div role="alert" className="rounded-xl border border-destructive/40 bg-destructive/10 p-5">
      <div className="space-y-1">
        <p className="text-sm font-semibold">{isUnavailable ? 'اتصال برقرار نشد' : 'مشکلی پیش آمد'}</p>
        <p className="text-sm leading-6 text-muted-foreground">
          {isUnavailable
            ? 'در این لحظه به فروشگاه دسترسی نداریم. کمی بعد دوباره تلاش کنید.'
            : state.kind === 'notFound'
              ? state.message
              : 'مشکلی پیش آمد.'}
        </p>
        <div className="pt-1">
          {isUnavailable ? (
            <Button type="button" onClick={() => onRetry()}>
              تلاش دوباره
            </Button>
          ) : (
            <Link to={`/shop/${tenantId}`}>
              <Button type="button">بازگشت به فروشگاه</Button>
            </Link>
          )}
        </div>
      </div>
    </div>
  )
}

/**
 * One cart row. The quantity field commits on blur/Enter and via the stepper;
 * a plain `400` (e.g. an out-of-range quantity) is reported back to the page
 * as a mutation error, never as expiry.
 */
function CartItemRow({
  item,
  busy,
  onQuantityCommit,
  onRemove,
}: {
  item: CartItem
  busy: boolean
  onQuantityCommit: (quantity: number) => void
  onRemove: () => void
}) {
  const [quantity, setQuantity] = useState(item.quantity)

  // Keep the field in sync with the server-committed quantity.
  useEffect(() => {
    setQuantity(item.quantity)
  }, [item.quantity])

  const clamp = (value: number) => Math.min(99, Math.max(1, Math.round(value) || 1))

  function commit(raw: number) {
    const next = clamp(raw)
    setQuantity(next)
    if (next === item.quantity) return
    onQuantityCommit(next)
  }

  return (
    <div className="flex flex-wrap items-center gap-4 p-4 sm:flex-nowrap">
      <div className="size-16 shrink-0 rounded-md bg-muted" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="truncate font-semibold">{item.productName}</p>
        <p className="text-sm text-muted-foreground">{item.variantLabel}</p>
      </div>

      <div className="flex shrink-0 items-center gap-1" aria-label={`تعداد ${item.productName}`}>
        <SecondaryButton
          type="button"
          aria-label={`کاهش تعداد ${item.productName}`}
          className="px-2.5"
          disabled={busy || quantity <= 1}
          onClick={() => commit(quantity - 1)}
        >
          <Minus aria-hidden="true" className="size-3.5" />
        </SecondaryButton>
        <input
          type="number"
          min={1}
          max={99}
          value={quantity}
          disabled={busy}
          aria-label={`تعداد ${item.productName}`}
          onChange={(event) => {
            const value = Number(event.target.value)
            if (Number.isFinite(value)) setQuantity(clamp(value))
          }}
          onBlur={() => commit(quantity)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') (event.target as HTMLInputElement).blur()
          }}
          className="w-14 rounded-md border border-input bg-surface px-2 py-1 text-center text-sm tabular-nums"
        />
        <SecondaryButton
          type="button"
          aria-label={`افزایش تعداد ${item.productName}`}
          className="px-2.5"
          disabled={busy || quantity >= 99}
          onClick={() => commit(quantity + 1)}
        >
          <Plus aria-hidden="true" className="size-3.5" />
        </SecondaryButton>
      </div>

      <p className={cn('w-28 text-end text-sm font-semibold tabular-nums', busy && 'opacity-60')}>
        {(item.unitPrice * item.quantity).toLocaleString('fa-IR')} تومان
      </p>

      <SecondaryButton
        type="button"
        aria-label={`حذف ${item.productName}`}
        disabled={busy}
        onClick={() => onRemove()}
      >
        <Trash2 aria-hidden="true" className="size-4" />
      </SecondaryButton>
    </div>
  )
}
