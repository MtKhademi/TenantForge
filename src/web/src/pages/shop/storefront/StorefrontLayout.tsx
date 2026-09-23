import { Camera, Phone, ShoppingCart, Store } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useParams } from 'react-router-dom'
import { getCartId } from '@/features/shop/cartStorage'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import type { PublicCategory } from '@/features/shop/contracts/categoryHierarchyContract'
import type { PublicShopProfile } from '@/features/shop/contracts/shopProfileContract'
import { cn } from '@/lib/utils'

/**
 * S26 storefront (F030) + S34 (F046) + S35 (F047): a minimal public layout,
 * deliberately separate from `DashboardShell` — no sidebar, no sign-out, no
 * authenticated chrome of any kind. A visitor is never signed in, so these
 * routes live outside `ProtectedLayout` in `App.tsx`. It reuses only the
 * design tokens in `index.css` and the shared `ui/` primitives.
 *
 * The cart control is a count badge fed by the `cartLease` client slot (the
 * B040 mock now, the HTTP client after F058). It is re-read on mount and
 * whenever the route changes (e.g. after adding to cart on the product
 * detail page), so it is always fresh on the page the visitor lands on.
 * F048 moved it off the real `cartAdapter` (F033): in the mock-first phase
 * the stored cart id is client-minted by the cart page's `useCartLease`, and
 * the real adapter's 404 handler would clear that id and race the mock.
 * The badge stays read-only — it reads the STORED cart id only, never mints
 * one, and a plain read never extends the lease (B040).
 *
 * F046 adds the grouped category bar: `listPublic` (the B038 mock now, the
 * HTTP client after F056) returns only effective-activity roots, each with
 * its ordered children — so an inactive root (and its children) never
 * appears here at all. Child links render directly below their root. The
 * bar degrades gracefully: while it loads (or if it fails) the layout keeps
 * rendering, just without the category links.
 *
 * F047 adds the store identity: the header brand block and the footer both
 * read the published profile through the `profile` client slot (the B039
 * mock now, the HTTP client after F057). The header shows `name` and
 * `tagline`; the footer shows `name`, `tagline`, `supportPhone` and
 * `instagramUrl` (when set) plus the five policy-page links. Publication
 * gates ONLY the profile/policy surfaces, never the catalog (B039): when the
 * profile is unpublished or missing, the header brand and the footer
 * identity collapse to one neutral "not yet open" fallback — the SAME state
 * the policy pages render — while the catalog, category bar and cart keep
 * working so existing storefront URLs stay functional during rollout.
 *
 * The profile load is best-effort like the category load: a transport
 * failure leaves the neutral fallback (never a broken storefront), and a
 * superseded request never overwrites a newer one.
 */

const POLICY_LINKS: ReadonlyArray<{ slug: string; label: string }> = [
  { slug: 'about', label: 'درباره ما' },
  { slug: 'shipping', label: 'شرایط ارسال' },
  { slug: 'payment', label: 'شرایط پرداخت' },
  { slug: 'returns', label: 'شرایط مرجوعی' },
  { slug: 'privacy', label: 'حریم خصوصی' },
]

export function StorefrontLayout() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const location = useLocation()
  const { categories: categoryClient, profile: profileClient, cartLease } = useShopClients()
  const [itemCount, setItemCount] = useState(0)
  const [categoryTree, setCategoryTree] = useState<PublicCategory[] | null>(null)
  // null = not loaded yet (or failed); a profile object = published identity.
  const [publicProfile, setPublicProfile] = useState<PublicShopProfile | null>(null)
  const [profileLoaded, setProfileLoaded] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const storedCartId = getCartId(tenantId)
    if (!storedCartId) {
      // No stored cart: nothing to count. Deliberately NOT minting one —
      // the badge is read-only, exactly as F033's adapter-based badge was.
      setItemCount(0)
      return undefined
    }
    let cancelled = false
    void cartLease
      .getCart(tenantId, storedCartId, controller.signal)
      .then((cart) => {
        if (!cancelled) setItemCount(cart.items.reduce((sum, item) => sum + item.quantity, 0))
      })
      .catch(() => {
        // The badge is decorative weight only; an expired lease, unknown id
        // or transport failure must not break storefront navigation, so the
        // count simply stays at 0 (the page's own recovery state owns UX).
        if (!cancelled) setItemCount(0)
      })
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [tenantId, location.pathname, cartLease])

  // One abort controller per tenant for the category tree; a superseded tree
  // must never overwrite a newer one, and an aborted load must not surface as
  // an error.
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

  // One abort controller per tenant for the published profile. Unpublished or
  // missing resolves to null (the neutral fallback), exactly like the
  // anonymous 404 the real route returns.
  useEffect(() => {
    const controller = new AbortController()
    setPublicProfile(null)
    setProfileLoaded(false)
    profileClient
      .getPublic(tenantId, controller.signal)
      .then((profile) => {
        if (!controller.signal.aborted) {
          setPublicProfile(profile)
          setProfileLoaded(true)
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          // Best-effort: the identity simply stays in its neutral state.
          setPublicProfile(null)
          setProfileLoaded(true)
        }
      })
    return () => {
      controller.abort()
    }
  }, [tenantId, profileClient])

  const headerIdentity = publicProfile ? (
    <span className="min-w-0">
      <span className="block truncate text-lg font-semibold">{publicProfile.name}</span>
      {publicProfile.tagline.length > 0 && (
        <span className="block truncate text-xs text-muted-foreground">{publicProfile.tagline}</span>
      )}
    </span>
  ) : profileLoaded ? (
    <span className="flex items-center gap-2 text-lg font-semibold">
      <Store aria-hidden="true" className="size-5 text-muted-foreground" />
      فروشگاه
    </span>
  ) : (
    // Fixed-size skeleton so the finished brand block does not shift the header.
    <span className="block h-10 w-40 animate-pulse rounded-md bg-muted" aria-hidden="true" />
  )

  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <header className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-4">
          <Link
            to={`/shop/${tenantId}`}
            className="inline-flex min-w-0 items-center gap-2"
            aria-label={publicProfile ? `صفحه اصلی ${publicProfile.name}` : 'صفحه اصلی فروشگاه'}
          >
            {headerIdentity}
          </Link>
          <div className="flex shrink-0 items-center gap-2">
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
      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8">
        <Outlet />
      </main>
      <StorefrontFooter tenantId={tenantId} profile={publicProfile} profileLoaded={profileLoaded} />
    </div>
  )
}

/**
 * F047 storefront footer: the store's published identity (name, tagline,
 * support phone, Instagram when set) plus the five policy-page links. When
 * the profile is unpublished or missing it renders the SAME neutral "not yet
 * open" fallback as the header brand and the policy pages — the policy links
 * are never shown alongside real content on some surfaces and a fallback on
 * others. While the profile is still loading the footer keeps a stable
 * height so the page does not shift.
 */
/** Derive an `@handle`-style label from a published instagram URL. */
function instagramLabelFromUrl(instagramUrl: string | null): string | null {
  if (!instagramUrl) return null
  try {
    const url = new URL(instagramUrl)
    const parts = url.pathname.split('/').filter(Boolean)
    return parts.length > 0 ? `@${parts[0]}` : 'اینستاگرام'
  } catch {
    return 'اینستاگرام'
  }
}

function StorefrontFooter({
  tenantId,
  profile,
  profileLoaded,
}: {
  tenantId: string
  profile: PublicShopProfile | null
  profileLoaded: boolean
}) {
  const instagramLabel = profile ? instagramLabelFromUrl(profile.instagramUrl) : null

  if (!profileLoaded) {
    return (
      <footer className="border-t border-border bg-surface" aria-label="پایه فروشگاه">
        <div className="mx-auto h-16 max-w-6xl animate-pulse px-4 py-4" aria-hidden="true">
          <div className="h-6 w-40 rounded-md bg-muted" />
        </div>
      </footer>
    )
  }

  if (profile === null) {
    return (
      <footer className="border-t border-border bg-surface" aria-label="پایه فروشگاه">
        <div className="mx-auto flex max-w-6xl items-center gap-2 px-4 py-5 text-sm text-muted-foreground">
          <Store aria-hidden="true" className="size-4 shrink-0" />
          این فروشگاه هنوز آماده‌سازی نشده است.
        </div>
      </footer>
    )
  }

  return (
    <footer className="border-t border-border bg-surface" aria-label="پایه فروشگاه">
      <div className="mx-auto grid max-w-6xl gap-6 px-4 py-6 sm:grid-cols-2">
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">{profile.name}</p>
          {profile.tagline.length > 0 && (
            <p className="text-sm text-muted-foreground">{profile.tagline}</p>
          )}
          <ul className="mt-2 space-y-1.5 text-sm text-muted-foreground">
            {profile.supportPhone.length > 0 && (
              <li className="flex items-center gap-1.5">
                <Phone aria-hidden="true" className="size-3.5 shrink-0" />
                <span dir="ltr">{profile.supportPhone}</span>
              </li>
            )}
            {profile.instagramUrl !== null && (
              <li className="flex items-center gap-1.5">
                <Camera aria-hidden="true" className="size-3.5 shrink-0" />
                <a
                  href={profile.instagramUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  dir="ltr"
                  className="underline underline-offset-2 hover:text-foreground"
                >
                  {instagramLabel}
                </a>
              </li>
            )}
          </ul>
        </div>
        <nav aria-label="صفحه‌های اطلاعات" className="sm:justify-self-end">
          <ul className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-sm">
            {POLICY_LINKS.map((link) => (
              <li key={link.slug}>
                <Link
                  to={`/shop/${tenantId}/${link.slug}`}
                  className="text-muted-foreground hover:text-foreground hover:underline"
                >
                  {link.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </footer>
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
            <CategoryLink to={`/shop/${tenantId}/categories/${root.slug}`} label={root.name} />
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
