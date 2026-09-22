import { Package, RefreshCw, ShoppingBag, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ProductMediaImage } from '@/components/shop/ProductMediaImage'
import { Button } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import type { PublicCategory } from '@/features/shop/contracts/categoryHierarchyContract'
import type { ProductImage } from '@/features/shop/contracts/mediaContract'
import { storefrontAdapter } from '@/features/shop/storefrontAdapter'
import type { StorefrontCategory, StorefrontProductSummary } from '@/features/shop/storefrontTypes'

/**
 * S26 storefront (F030, F031) + S34 (F046): the category grid (no
 * `categorySlug`) and, when a `categorySlug` route param is present, that
 * category's product-card grid. F031 loads real, anonymous product data from
 * B027 via `storefrontAdapter`; the breadcrumb now resolves through the
 * `categories` client (B038 mock, F056 HTTP) and renders at most two levels —
 * root, then child — because the data model has no third level.
 * The states below (loading, empty, retryable error) match the repository's
 * shared conventions and are driven by the client's failures.
 */
export function CategoryPage() {
  const { tenantId = '', categorySlug } = useParams<{ tenantId: string; categorySlug?: string }>()
  const { media, categories: categoryClient } = useShopClients()
  const [categories, setCategories] = useState<StorefrontCategory[] | null>(null)
  const [categoryError, setCategoryError] = useState(false)
  const [publicTree, setPublicTree] = useState<PublicCategory[] | null>(null)
  const [products, setProducts] = useState<StorefrontProductSummary[] | null>(null)
  const [productImages, setProductImages] = useState<Record<string, ProductImage | null>>({})
  const [productError, setProductError] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const retry = () => setReloadKey((key) => key + 1)

  // The public category tree feeds the breadcrumb (and the category-grid
  // heading name fallback). Aborted loads are ignored so a superseded tree
  // can never overwrite a newer one.
  useEffect(() => {
    const controller = new AbortController()
    setPublicTree(null)
    categoryClient
      .listPublic(tenantId, controller.signal)
      .then((tree) => {
        if (!controller.signal.aborted) setPublicTree(tree)
      })
      .catch(() => {
        // The breadcrumb is decorative: an unreachable call leaves it out
        // rather than breaking the product grid.
        if (!controller.signal.aborted) setPublicTree(null)
      })
    return () => {
      controller.abort()
    }
  }, [tenantId, categoryClient, reloadKey])

  // At most two levels: a root match, or a root + its child. The data model
  // never has a third level, so the lookup stops at the children of roots.
  // Derived during render (cheap two-level scan) so it can never be stale.
  let breadcrumb: { root: PublicCategory; child: PublicCategory | null } | null = null
  if (publicTree !== null && categorySlug !== undefined) {
    for (const root of publicTree) {
      if (root.slug === categorySlug) {
        breadcrumb = { root, child: null }
        break
      }
      const child = root.children.find((entry) => entry.slug === categorySlug)
      if (child) {
        breadcrumb = { root, child }
        break
      }
    }
  }

  // Categories drive both the grid and the product-page heading name, so they
  // are always loaded for a storefront tenant.
  useEffect(() => {
    let cancelled = false
    setCategories(null)
    setCategoryError(false)
    storefrontAdapter
      .listCategories(tenantId)
      .then((list) => {
        if (!cancelled) setCategories(list)
      })
      .catch(() => {
        if (!cancelled) setCategoryError(true)
      })
    return () => {
      cancelled = true
    }
  }, [tenantId, reloadKey])

  useEffect(() => {
    if (!categorySlug) {
      setProducts(null)
      return
    }
    let cancelled = false
    setProducts(null)
    setProductError(false)
    storefrontAdapter
      .listProducts(tenantId, categorySlug)
      .then((list) => {
        if (!cancelled) setProducts(list)
      })
      .catch(() => {
        if (!cancelled) setProductError(true)
      })
    return () => {
      cancelled = true
    }
  }, [tenantId, categorySlug, reloadKey])

  useEffect(() => {
    if (!products || products.length === 0) {
      setProductImages({})
      return
    }
    const controller = new AbortController()
    setProductImages({})
    void Promise.all(
      products.map(async (product) => {
        try {
          const withGallery = await media.getProduct(tenantId, product.id, controller.signal)
          return [product.id, withGallery.images[0] ?? null] as const
        } catch {
          return [product.id, null] as const
        }
      }),
    ).then((entries) => {
      if (!controller.signal.aborted) setProductImages(Object.fromEntries(entries))
    })
    return () => controller.abort()
  }, [media, products, tenantId])

  if (!categorySlug) {
    return (
      <section aria-label="دسته‌بندی‌ها" className="space-y-6">
        <header className="space-y-1">
          <h1 className="text-2xl font-semibold">دسته‌بندی‌ها</h1>
          <p className="text-sm text-muted-foreground">برای دیدن محصولات، یک دسته‌بندی انتخاب کنید.</p>
        </header>

        {categories === null && !categoryError && <CategoryGridSkeleton />}

        {categoryError && (
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title="فهرست دسته‌بندی‌ها در دسترس نیست"
            description="هم‌اکنون نمی‌توانیم دسته‌بندی‌ها را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید."
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={retry}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </Button>
            }
          />
        )}

        {categories !== null && categories.length === 0 && (
          <EmptyPanel
            icon={<Package aria-hidden="true" className="size-6" />}
            title="دسته‌بندی‌ای موجود نیست"
            description="این فروشگاه هنوز دسته‌بندی‌ای ندارد."
          />
        )}

        {categories !== null && categories.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
            {categories.map((category) => (
              <Link
                key={category.id}
                to={`/shop/${tenantId}/categories/${category.slug}`}
                className="group rounded-xl border border-border bg-surface p-6 shadow-soft transition-colors hover:bg-muted focus-visible:bg-muted"
              >
                <span className="inline-flex size-12 items-center justify-center rounded-lg bg-primary/10 text-primary">
                  <ShoppingBag aria-hidden="true" className="size-6" />
                </span>
                <span className="mt-4 block text-base font-semibold">{category.name}</span>
                <span className="mt-1 block text-sm text-muted-foreground">مشاهده محصولات</span>
              </Link>
            ))}
          </div>
        )}
      </section>
    )
  }

  // The breadcrumb name wins when the hierarchy knows the slug; the flat
  // category list stays as the fallback name source (e.g. if the tree call
  // fails but products loaded).
  const headingName =
    breadcrumb !== null
      ? breadcrumb.child?.name ?? breadcrumb.root.name
      : categories?.find((category) => category.slug === categorySlug)?.name ?? 'محصولات'

  return (
    <section aria-label="محصولات دسته‌بندی" className="space-y-6">
      <header className="space-y-2">
        {/* At most two category levels (root, then child) — the data model
            has no third. The home crumb is not a category level. When the
            tree cannot resolve the slug, the single crumb carries the
            fallback name and never a third entry. */}
        <Breadcrumb
          homeTo={`/shop/${tenantId}`}
          rootLabel={breadcrumb?.root.name ?? headingName}
          rootTo={
            breadcrumb !== null && breadcrumb.child !== null
              ? `/shop/${tenantId}/categories/${breadcrumb.root.slug}`
              : null
          }
          childLabel={breadcrumb?.child?.name ?? null}
        />
        <h1 className="text-2xl font-semibold">{headingName}</h1>
      </header>

      {products === null && !productError && <ProductGridSkeleton />}

      {productError && (
        <StatePanel
          icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
          title="فهرست محصولات در دسترس نیست"
          description="هم‌اکنون نمی‌توانیم محصولات این دسته‌بندی را بارگذاری کنیم. دوباره تلاش کنید."
          action={
            <Button type="button" className="mt-3 min-w-32" onClick={retry}>
              <RefreshCw aria-hidden="true" className="me-2 size-4" />
              تلاش دوباره
            </Button>
          }
        />
      )}

      {products !== null && products.length === 0 && (
        <EmptyPanel
          icon={<Package aria-hidden="true" className="size-6" />}
          title="محصولی در این دسته‌بندی موجود نیست"
          description="محصولی برای نمایش در این دسته‌بندی پیدا نشد."
        />
      )}

      {products !== null && products.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
          {products.map((product) => {
            const thumbnail = productImages[product.id]
            return (
            <Link
              key={product.id}
              to={`/shop/${tenantId}/products/${product.slug}`}
              className="group overflow-hidden rounded-xl border border-border bg-surface shadow-soft transition-colors hover:bg-muted focus-visible:bg-muted"
            >
              {thumbnail ? (
                <ProductMediaImage
                  image={thumbnail}
                  className="aspect-square"
                  showBrokenHint={false}
                />
              ) : (
                <div className="flex aspect-square items-center justify-center bg-muted text-muted-foreground" role="img" aria-label={`تصویر ${product.name}`}>
                  <Package aria-hidden="true" className="size-8" />
                </div>
              )}
              <div className="space-y-1 p-4">
                <p className="font-semibold">{product.name}</p>
                <div className="flex items-baseline gap-2">
                  <span className="text-sm font-medium">{product.effectivePrice.toLocaleString('fa-IR')} تومان</span>
                  {product.compareAtPrice !== null && (
                    <s className="text-sm text-muted-foreground">
                      {product.compareAtPrice.toLocaleString('fa-IR')}
                    </s>
                  )}
                </div>
              </div>
            </Link>
            )
          })}
        </div>
      )}
    </section>
  )
}

/**
 * Storefront breadcrumb: home + at most two category levels (root, then
 * child). The data model never has a third level, so this component cannot
 * render one — `childLabel` is the deepest entry it accepts.
 */
function Breadcrumb({
  homeTo,
  rootLabel,
  rootTo,
  childLabel,
}: {
  homeTo: string
  rootLabel: string
  rootTo: string | null
  childLabel: string | null
}) {
  return (
    <nav aria-label="مسیر دسته‌بندی">
      <ol className="flex flex-wrap items-center gap-1 text-sm" role="list">
        <li>
          <Link to={homeTo} className="font-medium text-primary hover:underline">
            فروشگاه
          </Link>
        </li>
        <li aria-hidden="true" className="text-muted-foreground">/</li>
        <li>
          {rootTo !== null ? (
            <Link to={rootTo} className="font-medium hover:underline">
              {rootLabel}
            </Link>
          ) : (
            <span aria-current="page" className="font-semibold">
              {rootLabel}
            </span>
          )}
        </li>
        {childLabel !== null && (
          <>
            <li aria-hidden="true" className="text-muted-foreground">/</li>
            <li>
              <span aria-current="page" className="font-semibold">
                {childLabel}
              </span>
            </li>
          </>
        )}
      </ol>
    </nav>
  )
}

/** Shared empty-result panel for the storefront (neutral, non-error tone). */
function EmptyPanel({ icon, title, description }: { icon: ReactNode; title: string; description: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
      <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        {icon}
      </span>
      <p className="mt-3 text-sm font-semibold">{title}</p>
      <p className="mt-1 text-sm text-muted-foreground">{description}</p>
    </div>
  )
}

/** Loading placeholder matching the grid's footprint (no layout shift). */
function CategoryGridSkeleton() {
  return (
    <div aria-busy="true" className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
      <p className="sr-only">
        در حال بارگذاری دسته‌بندی‌ها
        <span aria-hidden="true">…</span>
      </p>
      {[0, 1, 2].map((index) => (
        <div key={index} className="rounded-xl border border-border bg-surface p-6 shadow-soft">
          <div className="size-12 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />
          <div className="mt-4 h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="mt-2 h-3 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}

function ProductGridSkeleton() {
  return (
    <div aria-busy="true" className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
      <p className="sr-only">
        در حال بارگذاری محصولات
        <span aria-hidden="true">…</span>
      </p>
      {[0, 1, 2].map((index) => (
        <div key={index} className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
          <div className="aspect-square animate-pulse bg-muted motion-reduce:animate-none" />
          <div className="space-y-2 p-4">
            <div className="h-4 w-36 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="h-3 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          </div>
        </div>
      ))}
    </div>
  )
}
