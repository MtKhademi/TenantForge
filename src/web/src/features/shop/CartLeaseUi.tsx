import { Clock3, Timer } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'

/**
 * S36 cart reservation lease — shared display pieces (F048).
 *
 * `CartLeaseCountdown` is DISPLAY-ONLY: it renders a per-second tick of a
 * server-returned `expiresAtUtc` and never fires a client call — no polling,
 * no auto-extend (B040: only a successful add/update/remove extends the
 * lease). It also calls `onExpiry` exactly once when the local clock passes
 * the lease end, so a page can flip to the recovery state promptly even when
 * no request is in flight. The server remains the authority: the page's
 * handler re-checks the latest lease and only recovers if it is genuinely
 * past, and the next real call answers 410.
 *
 * `CartLeaseRecovery` is the 410 `shop_cart_expired` recovery panel the
 * cart, checkout and order-review pages all render — the same panel, so a
 * shopper sees one consistent "your cart expired" state on every surface.
 */

function formatCountdown(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${minutes}:${String(seconds).padStart(2, '0')}`
}

/**
 * Per-second tick of the remaining time until `expiresAtUtc`. When the local
 * clock reaches the lease end, `onExpiry` is invoked exactly once for that
 * lease window (the flag resets whenever `expiresAtUtc` changes, i.e. after a
 * mutation extends the lease). The component makes no client call for it.
 */
export function CartLeaseCountdown({
  expiresAtUtc,
  onExpiry,
}: {
  expiresAtUtc: string
  /** Fired once per lease window when the local countdown reaches zero. */
  onExpiry?: () => void
}) {
  const [now, setNow] = useState(() => Date.now())
  const reportedRef = useRef(false)

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  // A new lease (from a mutation) starts a fresh window: reset the once-flag.
  useEffect(() => {
    reportedRef.current = false
  }, [expiresAtUtc])

  const msLeft = Date.parse(expiresAtUtc) - now
  const expired = msLeft <= 0

  useEffect(() => {
    if (expired && !reportedRef.current) {
      reportedRef.current = true
      onExpiry?.()
    }
  }, [expired, onExpiry])

  if (expired) return null

  const secondsLeft = Math.ceil(msLeft / 1000)
  return (
    <p
      aria-label="زمان باقی‌مانده‌ی رزرو سبد خرید"
      className={cn(
        'inline-flex items-center gap-1.5 rounded-md border px-2 py-1 text-xs font-semibold tabular-nums',
        secondsLeft <= 30
          ? 'border-destructive/40 bg-destructive/10 text-destructive'
          : 'border-border bg-surface text-muted-foreground',
      )}
    >
      <Timer aria-hidden="true" className="size-3.5" />
      رزرو سبد خرید تا {formatCountdown(secondsLeft)}
    </p>
  )
}

/**
 * The shared 410 `shop_cart_expired` recovery state. Rendered by cart,
 * checkout and review after the current tenant's cart id and order draft
 * have been cleared — the only action is back to the catalog; the cart is
 * never silently rebuilt.
 */
export function CartLeaseRecovery({ tenantId }: { tenantId: string }) {
  return (
    <div role="alert" className="rounded-xl border border-destructive/40 bg-destructive/10 p-5">
      <div className="flex items-start gap-3">
        <span className="flex size-10 shrink-0 items-center justify-center rounded-md bg-destructive/15">
          <Clock3 aria-hidden="true" className="size-5 text-destructive" />
        </span>
        <div className="space-y-1">
          <p className="text-sm font-semibold">رزرو سبد خرید منقضی شد</p>
          <p className="text-sm leading-6 text-muted-foreground">
            زمان رزرو سبد خرید شما به پایان رسیده است و موجودی رها شده است. برای ادامه، دوباره به
            فروشگاه بروید و محصول موردنظرتان را انتخاب کنید.
          </p>
          <div className="pt-1">
            <Link to={`/shop/${tenantId}`}>
              <Button type="button">بازگشت به فروشگاه</Button>
            </Link>
          </div>
        </div>
      </div>
    </div>
  )
}
