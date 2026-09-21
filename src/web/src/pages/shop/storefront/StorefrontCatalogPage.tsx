import { RefreshCw, SearchX, TriangleAlert } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { ProductCard } from '@/components/shop/ProductCard'
import { StorefrontFilters } from '@/components/shop/StorefrontFilters'
import { Button } from '@/components/ui/Button'
import { PaginationControls } from '@/components/ui/PaginationControls'
import { StatePanel } from '@/components/ui/StatePanel'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import {
  discoverySortSchema,
  type DiscoverySort,
  type StorefrontProductList,
} from '@/features/shop/contracts/discoveryContract'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { DEFAULT_PAGE_SIZE, type PageSizeOption } from '@/features/pagination/paginationTypes'

/**
 * S33 storefront discovery (F045) — the all-products mock page.
 *
 * The URL is the single source of truth for filter state
 * (`q`, `category`, `sort`, `sale`, `page`), so a link can be refreshed and
 * back/forward-reproduced exactly. The search box debounces 300 ms before
 * committing to the URL; every filter change resets `page` to the first value.
 *
 * Data flows only through `useShopClients().discovery` (the F045 mock now, the
 * B037 HTTP client after F055). This component never imports fixtures and
 * never calls `fetch`. The returned page is displayed exactly as given — no
 * client-side re-sorting or re-filtering.
 */

const SEARCH_DEBOUNCE_MS = 300

/**
 * UI view model for the category filter's *options*. These slugs are what the
 * mock can resolve; the labels are Persian UI copy. (A real backend, F055/B038,
 * will supply categories through its own endpoint.)
 */
const CATEGORY_OPTIONS: ReadonlyArray<{ slug: string; label: string }> = [
  { slug: 'shoes', label: 'کفش' },
  { slug: 'bags', label: 'کیف' },
  { slug: 'accessories', label: 'لوازم جانبی' },
] as const

type ErrorKind = 'validation' | 'notFound' | 'unavailable'

type UIState =
  | { status: 'idle' | 'loading' }
  | { status: 'success'; data: StorefrontProductList }
  | { status: 'empty'; data: StorefrontProductList }
  | { status: 'error'; kind: ErrorKind }

function parseSort(value: string | null): DiscoverySort {
  if (value === null) return 'newest'
  const parsed = discoverySortSchema.safeParse(value)
  return parsed.success ? parsed.data : 'newest'
}

function parsePage(value: string | null): number {
  if (value === null) return 1
  const n = Number.parseInt(value, 10)
  return Number.isInteger(n) && n >= 1 ? n : 1
}

export function StorefrontCatalogPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { discovery } = useShopClients()
  const [searchParams, setSearchParams] = useSearchParams()

  const q = searchParams.get('q') ?? ''
  const category = searchParams.get('category') ?? ''
  const sort = parseSort(searchParams.get('sort'))
  const saleOnly = searchParams.get('sale') === '1'
  const page = parsePage(searchParams.get('page'))

  const [pageSize, setPageSize] = useState<PageSizeOption>(DEFAULT_PAGE_SIZE)
  const [draft, setDraft] = useState(q)
  const [reloadKey, setReloadKey] = useState(0)
  const [state, setState] = useState<UIState>({ status: 'idle' })

  // The last successful page, kept so a refetch can show a dimmed stale grid
  // instead of a skeleton — that is what prevents layout shift. The ref only
  // ever mutates immediately before a setState, so it is consistent with the
  // state that triggers each render.
  const lastGoodRef = useRef<StorefrontProductList | null>(null)
  const hasPreviousData = lastGoodRef.current !== null

  // Keep the search box in sync with the URL (covers back/forward, scenario
  // reload and the initial mount). When our own commit lands, `q` equals
  // `draft` and this is a no-op, so it never clobbers an in-progress draft.
  useEffect(() => {
    setDraft(q)
  }, [q])

  // 300 ms debounce: commit the draft to the URL only after typing pauses.
  useEffect(() => {
    if (draft === q) return
    const timer = window.setTimeout(() => {
      writeParams((p) => {
        if (draft.trim().length > 0) p.set('q', draft.trim())
        else p.delete('q')
        p.delete('page')
      })
    }, SEARCH_DEBOUNCE_MS)
    return () => window.clearTimeout(timer)
  }, [draft, q, searchParams])

  // The single data request. A new run aborts the previous one, so a
  // superseded response can never overwrite the newer state, and an aborted
  // call never surfaces as an error.
  const controllerRef = useRef<AbortController | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    controllerRef.current = controller
    setState({ status: 'loading' })

    const query = {
      q,
      categorySlug: category.length > 0 ? category : null,
      sort,
      saleOnly,
      pageNumber: page,
      pageSize,
    }

    discovery
      .listProducts(tenantId, query, controller.signal)
      .then((result) => {
        if (controller.signal.aborted) return
        lastGoodRef.current = result
        setState(result.products.length === 0 ? { status: 'empty', data: result } : { status: 'success', data: result })
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        lastGoodRef.current = null
        if (error instanceof DOMException && error.name === 'AbortError') return
        if (error instanceof ApiUnavailableError) {
          setState({ status: 'error', kind: 'unavailable' })
          return
        }
        if (error instanceof ShopClientError) {
          setState({ status: 'error', kind: error.problem.status === 404 ? 'notFound' : 'validation' })
          return
        }
        setState({ status: 'error', kind: 'unavailable' })
      })

    return () => controller.abort()
  }, [discovery, tenantId, q, category, sort, saleOnly, page, pageSize, reloadKey])

  function writeParams(mutate: (params: URLSearchParams) => void) {
    const next = new URLSearchParams(searchParams)
    mutate(next)
    setSearchParams(next, { replace: true })
  }

  // Filter changes reset the page; the search debounce uses the same path.
  function setFilter(key: 'category' | 'sort' | 'sale', value: string | number | boolean) {
    writeParams((p) => {
      if (key === 'sale') {
        if (value === true) p.set('sale', '1')
        else p.delete('sale')
      } else if (key === 'sort') {
        p.set('sort', String(value))
      } else {
        if (String(value).length > 0) p.set('category', String(value))
        else p.delete('category')
      }
      p.delete('page')
    })
  }

  function setPage(n: number) {
    writeParams((p) => {
      if (n > 1) p.set('page', String(n))
      else p.delete('page')
    })
  }

  function handlePageSizeChange(size: PageSizeOption) {
    setPageSize(size)
    writeParams((p) => p.delete('page'))
  }

  function clearFilters() {
    setDraft('')
    writeParams((p) => {
      p.delete('q')
      p.delete('category')
      p.delete('sort')
      p.delete('sale')
      p.delete('page')
    })
  }

  function retry() {
    setReloadKey((key) => key + 1)
  }

  const loading = state.status === 'loading' || state.status === 'idle'
  // While we already have a page on screen, keep it dimmed during a refetch
  // (no layout shift). The first load — or one with no data — shows a skeleton.
  const showSkeleton = loading && !hasPreviousData

  return (
    <section aria-label="فهرست محصولات فروشگاه" className="space-y-6">
      <header className="space-y-1">
        <h1 className="text-2xl font-semibold">همه محصولات</h1>
        <p className="text-sm text-muted-foreground">جست‌وجو، فیلتر دسته‌بندی، تخفیف و مرتب‌سازی.</p>
      </header>

      <StorefrontFilters
        searchValue={draft}
        onSearchChange={setDraft}
        categorySlug={category}
        onCategoryChange={(slug) => setFilter('category', slug)}
        sort={sort}
        onSortChange={(value) => setFilter('sort', value)}
        saleOnly={saleOnly}
        onSaleOnlyChange={(value) => setFilter('sale', value)}
        categories={CATEGORY_OPTIONS}
        disabled={state.status === 'loading'}
      />

      {state.status === 'error' && (
        <ErrorState
          kind={state.kind}
          onClearFilters={clearFilters}
          onRetry={retry}
        />
      )}

      {state.status !== 'error' && (
        <div
          aria-busy={loading}
          aria-live="polite"
          className={loading && hasPreviousData ? 'opacity-60 transition-opacity' : 'transition-opacity'}
        >
          {state.status === 'success' && <ProductGrid tenantId={tenantId} list={state.data} />}
          {state.status === 'empty' && <EmptyResults onClearFilters={clearFilters} />}
          {showSkeleton && <ProductGridSkeleton />}
          {loading && hasPreviousData && lastGoodRef.current !== null && (
            <ProductGrid tenantId={tenantId} list={lastGoodRef.current} />
          )}
        </div>
      )}

      {state.status === 'success' && (
        <PaginationControls
          meta={state.data.pagination}
          disabled={loading}
          onPageChange={setPage}
          onPageSizeChange={handlePageSizeChange}
          label="محصولات"
        />
      )}
    </section>
  )
}

function ProductGrid({ tenantId, list }: { tenantId: string; list: StorefrontProductList }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
      {list.products.map((product) => (
        <ProductCard key={product.id} tenantId={tenantId} product={product} />
      ))}
    </div>
  )
}

function EmptyResults({ onClearFilters }: { onClearFilters: () => void }) {
  return (
    <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
      <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        <SearchX aria-hidden="true" className="size-6" />
      </span>
      <p className="mt-3 text-sm font-semibold">محصولی پیدا نشد</p>
      <p className="mt-1 text-sm text-muted-foreground">بر اساس جست‌وجو و فیلترهای فعلی نتیجه‌ای وجود ندارد.</p>
      <Button type="button" className="mt-4" onClick={onClearFilters}>
        پاک کردن فیلترها
      </Button>
    </div>
  )
}

function ErrorState({
  kind,
  onClearFilters,
  onRetry,
}: {
  kind: ErrorKind
  onClearFilters: () => void
  onRetry: () => void
}) {
  let icon: ReactNode
  let title: string
  let description: string
  let action: ReactNode

  if (kind === 'unavailable') {
    icon = <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
    title = 'فهرست محصولات در دسترس نیست'
    description = 'هم‌اکنون نمی‌توانیم محصولات را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.'
    action = (
      <Button type="button" className="mt-3 min-w-32" onClick={onRetry}>
        <RefreshCw aria-hidden="true" className="me-2 size-4" />
        تلاش دوباره
      </Button>
    )
  } else if (kind === 'notFound') {
    icon = <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
    title = 'این دسته‌بندی پیدا نشد'
    description = 'دسته‌بندی انتخاب‌شده در این فروشگاه موجود نیست یا دیگر در دسترس نیست.'
    action = (
      <Button type="button" className="mt-3 min-w-32" onClick={onClearFilters}>
        پاک کردن فیلترها
      </Button>
    )
  } else {
    icon = <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
    title = 'مقادیر جست‌وجو معتبر نیست'
    description = 'یکی از مقادیر جست‌وجو یا مرتب‌سازی درست نیست. فیلترها را بازنویسی کنید و دوباره امتحان کنید.'
    action = (
      <Button type="button" className="mt-3 min-w-32" onClick={onClearFilters}>
        پاک کردن فیلترها
      </Button>
    )
  }

  return (
    <StatePanel
      icon={icon}
      title={title}
      description={description}
      action={action}
      density="prominent"
    />
  )
}

/** Loading placeholder with the same footprint as the product grid (no layout shift). */
function ProductGridSkeleton() {
  return (
    <div aria-busy="true" className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
      <p className="sr-only">
        در حال بارگذاری محصولات
        <span aria-hidden="true">…</span>
      </p>
      {Array.from({ length: 6 }, (_, index) => (
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
