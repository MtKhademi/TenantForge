import { ShoppingCart } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, Outlet, useLocation, useParams } from 'react-router-dom'
import { cartAdapter } from '@/features/shop/cartAdapter'

/**
 * S26 storefront (F030): a minimal public layout, deliberately separate from
 * `DashboardShell` — no sidebar, no sign-out, no authenticated chrome of any
 * kind. A visitor is never signed in, so these routes live outside
 * `ProtectedLayout` in `App.tsx`. It reuses only the design tokens in
 * `index.css` and the shared `ui/` primitives.
 *
 * The cart control is a count badge fed by the real cart API (F033). Since
 * F033 replaced the in-memory mock store with `cartAdapter`, there is no
 * synchronous subscription anymore: the count is re-fetched on mount and
 * whenever the route changes (e.g. after adding to cart on the product
 * detail page), so it is always fresh on the page the visitor lands on.
 */
export function StorefrontLayout() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const location = useLocation()
  const [itemCount, setItemCount] = useState(0)

  useEffect(() => {
    let cancelled = false
    void cartAdapter
      .getCart(tenantId)
      .then((cart) => {
        if (!cancelled) setItemCount(cart ? cart.items.reduce((sum, item) => sum + item.quantity, 0) : 0)
      })
      .catch(() => {
        // The badge is decorative weight only; an unreachable API must not
        // break storefront navigation, so the count simply stays at 0.
        if (!cancelled) setItemCount(0)
      })
    return () => {
      cancelled = true
    }
  }, [tenantId, location.pathname])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-4">
          <Link to={`/shop/${tenantId}`} className="text-lg font-semibold" aria-label="صفحه اصلی فروشگاه">
            فروشگاه
          </Link>
          <Link
            to={`/shop/${tenantId}/cart`}
            aria-label={itemCount > 0 ? `سبد خرید، ${itemCount} قلم` : 'سبد خرید'}
            className="relative inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold hover:bg-muted"
          >
            <ShoppingCart aria-hidden="true" className="size-4" />
            <span className="sr-only">سبد خرید</span>
            {itemCount > 0 && (
              <span className="absolute -top-2 -end-2 inline-flex size-5 items-center justify-center rounded-full bg-primary text-xs text-primary-foreground">
                {itemCount}
              </span>
            )}
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
