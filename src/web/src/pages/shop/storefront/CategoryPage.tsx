import { Package, RefreshCw, ShoppingBag, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { mockStorefrontCatalog } from '@/features/shop/mockStorefrontCatalog'
import type { StorefrontCategory, StorefrontProductSummary } from '@/features/shop/storefrontTypes'

/**
 * S26 storefront (F030): the category grid (no `categorySlug`) and, when a
 * `categorySlug` route param is present, that category's product-card grid.
 * Data is mocked in `mockStorefrontCatalog`; the states below (loading, empty,
 * retryable error) match the repository's shared conventions. F031 swaps the
 * data source only.
 */
export function CategoryPage() {
  const { tenantId = '', categorySlug } = useParams<{ tenantId: string; categorySlug?: string }>()
  const [categories, setCategories] = useState<StorefrontCategory[] | null>(null)
  const [categoryError, setCategoryError] = useState(false)
  const [products, setProducts] = useState<StorefrontProductSummary[] | null>(null)
  const [productError, setProductError] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const retry = () => setReloadKey((key) => key + 1)

  // Categories drive both the grid and the product-page heading name, so they
  // are always loaded for a storefront tenant.
  useEffect(() => {
    let cancelled = false
    setCategories(null)
    setCategoryError(false)
    mockStorefrontCatalog
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
    mockStorefrontCatalog
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

  const headingName =
    categories?.find((category) => category.slug === categorySlug)?.name ?? 'محصولات'

  return (
    <section aria-label="محصولات دسته‌بندی" className="space-y-6">
      <header className="space-y-1">
        <Link
          to={`/shop/${tenantId}`}
          className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
        >
          بازگشت به دسته‌بندی‌ها
        </Link>
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
          {products.map((product) => (
            <Link
              key={product.id}
              to={`/shop/${tenantId}/products/${product.slug}`}
              className="group overflow-hidden rounded-xl border border-border bg-surface shadow-soft transition-colors hover:bg-muted focus-visible:bg-muted"
            >
              <div className="aspect-square bg-muted" aria-hidden="true" />
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
          ))}
        </div>
      )}
    </section>
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
