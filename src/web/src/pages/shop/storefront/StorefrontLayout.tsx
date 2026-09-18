import { ShoppingCart } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, Outlet, useParams } from 'react-router-dom'
import { mockStorefrontCart } from '@/features/shop/mockStorefrontCartState'

/**
 * S26 storefront (F030): a minimal public layout, deliberately separate from
 * `DashboardShell` — no sidebar, no sign-out, no authenticated chrome of any
 * kind. A visitor is never signed in, so these routes live outside
 * `ProtectedLayout` in `App.tsx`. It reuses only the design tokens in
 * `index.css` and the shared `ui/` primitives.
 *
 * The cart control is a live count badge driven by `mockStorefrontCart` (F032
 * builds the cart page the link points to; F031 swaps the data source).
 */
export function StorefrontLayout() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [itemCount, setItemCount] = useState(mockStorefrontCart.itemCount())

  useEffect(() => {
    return mockStorefrontCart.subscribe(() => setItemCount(mockStorefrontCart.itemCount()))
  }, [])

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
