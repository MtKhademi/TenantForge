import { ShoppingCart } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useParams } from 'react-router-dom'
import { cartAdapter } from '@/features/shop/cartAdapter'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import type { PublicCategory } from '@/features/shop/contracts/categoryHierarchyContract'
import { cn } from '@/lib/utils'

/**
 * S26 storefront (F030) + S34 (F046): a minimal public layout, deliberately
 * separate from `DashboardShell` — no sidebar, no sign-out, no authenticated
 * chrome of any kind. A visitor is never signed in, so these routes live
 * outside `ProtectedLayout` in `App.tsx`. It reuses only the design tokens in
 * `index.css` and the shared `ui/` primitives.
 *
 * The cart control is a count badge fed by the real cart API (F033). Since
 * F033 replaced the in-memory mock store with `cartAdapter`, there is no
 * synchronous subscription anymore: the count is re-fetched on mount and
 * whenever the route changes (e.g. after adding to cart on the product
 * detail page), so it is always fresh on the page the visitor lands on.
 *
 * F046 adds the grouped category bar: `listPublic` (the B038 mock now, the
 * HTTP client after F056) returns only effective-activity roots, each with
 * its ordered children — so an inactive root (and its children) never
 * appears here at all. Child links render directly below their root. The
 * bar degrades gracefully: while it loads (or if it fails) the layout keeps
 * rendering, just without the category links.
 */
export function StorefrontLayout() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const location = useLocation()
  const { categories: categoryClient } = useShopClients()
  const [itemCount, setItemCount] = useState(0)
  const [categoryTree, setCategoryTree] = useState<PublicCategory[] | null>(null)

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

  // One abort controller per tenant; a superseded tree must never overwrite
  // a newer one, and an aborted load must not surface as an error.
  useEffect(() => {
    const controller = new AbortController()
    setCategoryTree(null)
    categoryClient
      .listPublic(tenantId, controller.signal)
      .then((tree) => {
        if (!controller.signal.aborted) setCategoryTree(tree)
      })
      .catch(() => {
        // Navigation is best-effort: an unreachable categories call leaves the
        // bar out (null) rather than breaking the storefront page.
        if (!controller.signal.aborted) setCategoryTree(null)
      })
    return () => {
      controller.abort()
    }
  }, [tenantId, categoryClient])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-4">
          <Link to={`/shop/${tenantId}`} className="text-lg font-semibold" aria-label="صفحه اصلی فروشگاه">
            فروشگاه
          </Link>
          <div className="flex items-center gap-2">
            <Link
              to={`/shop/${tenantId}/track-order`}
              className="hidden text-sm font-medium hover:underline md:inline"
            >
              پیگیری سفارش
            </Link>
            <Link
              to={`/shop/${tenantId}/cart`}
              aria-label={itemCount > 0 ? `سبد خرید، ${itemCount} قلم` : 'سبد خرید'}
              className="relative inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold hover:bg-muted"
            >
              <ShoppingCart aria-hidden="true" className="size-4" />
              <span className="hidden md:inline">سبد خرید</span>
              {itemCount > 0 && (
                <span className="absolute -top-2 -end-2 inline-flex size-5 items-center justify-center rounded-full bg-primary text-xs text-primary-foreground">
                  {itemCount}
                </span>
              )}
            </Link>
          </div>
        </div>
        {categoryTree !== null && categoryTree.length > 0 && (
          <StorefrontCategoryBar tenantId={tenantId} tree={categoryTree} />
        )}
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}

/**
 * F046 storefront category bar: each root link with its child links grouped
 * directly below it. `NavLink` gives the active-route styling for free; the
 * tree is at most two levels deep by construction (B038). On narrow screens
 * the children stack under their root; from `sm:` up they sit inline after
 * it, so the grouping holds at every viewport.
 */
function StorefrontCategoryBar({ tenantId, tree }: { tenantId: string; tree: PublicCategory[] }) {
  return (
    <nav aria-label="دسته‌بندی‌های فروشگاه" className="border-t border-border">
      <ul className="mx-auto flex max-w-6xl flex-col gap-y-0.5 px-4 py-2 sm:flex-row sm:flex-wrap sm:items-start sm:gap-x-2">
        {tree.map((root) => (
          <li key={root.id} className="flex flex-col sm:flex-row sm:items-center sm:gap-x-1">
            <CategoryLink
              to={`/shop/${tenantId}/categories/${root.slug}`}
              label={root.name}
            />
            {root.children.length > 0 && (
              <ul
                aria-label={`زیر‌دسته‌های ${root.name}`}
                className="flex flex-col gap-y-0.5 ps-5 sm:flex-row sm:flex-wrap sm:gap-x-1 sm:ps-0"
              >
                {root.children.map((child) => (
                  <li key={child.id}>
                    <CategoryLink
                      to={`/shop/${tenantId}/categories/${child.slug}`}
                      label={child.name}
                      child
                    />
                  </li>
                ))}
              </ul>
            )}
          </li>
        ))}
      </ul>
    </nav>
  )
}

function CategoryLink({ to, label, child }: { to: string; label: string; child?: boolean }) {
  return (
    <NavLink
      to={to}
      end
      className={({ isActive }) =>
        cn(
          'rounded-md px-2 py-1 text-sm transition-colors hover:bg-muted hover:text-foreground focus-visible:bg-muted focus-visible:text-foreground',
          child && 'text-muted-foreground',
          isActive && 'bg-muted font-semibold text-foreground',
        )
      }
    >
      {label}
    </NavLink>
  )
}
