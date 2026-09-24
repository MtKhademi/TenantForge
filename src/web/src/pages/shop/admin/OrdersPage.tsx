import {
  ArrowRight,
  ClipboardList,
  Lock,
  RefreshCw,
  SearchX,
  TriangleAlert,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { PaginationControls } from '@/components/ui/PaginationControls'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import {
  adminOrderStatusSchema,
  type AdminOrderStatus,
  type AdminOrderSummary,
  type AdminOrderListResponse,
} from '@/features/shop/contracts/adminOrdersContract'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import {
  DEFAULT_PAGE_SIZE,
  type PageSizeOption,
} from '@/features/pagination/paginationTypes'
import { SHOP_ORDERS_VIEW_KEY } from '@/features/roles/roleTypes'
import { useTenantPermissions } from '@/features/roles/tenantPermissions'
import { cn } from '@/lib/utils'
import { OrderStatusBadge } from './OrderStatusBadge'

/**
 * S38 (F050): the permission-gated admin order list. Consumes the `orders`
 * client slot from `useShopClients()` (the deterministic mock now, the B042
 * HTTP client after F060) — it never imports fixtures and never calls `fetch`.
 *
 * The URL is the single source of truth for filter state (`q`, `status`,
 * `fromUtc`, `toUtc`) plus `page`/`size`, so a filtered view can be reloaded
 * and reproduced by back/forward. The search box debounces 300 ms before
 * committing to the URL; every filter change resets to page 1. Data is shown
 * exactly as the (mock) server gives it — no client-side re-sort/filtering.
 *
 * Permissions: both the nav entry and this page branch on `Shop.Orders.View`
 * (`useTenantPermissions`). A user without the key sees a permission-denied
 * panel instead of the content (hiding is presentation; the server is the
 * authority and answers `403` — surfaced here through the same state).
 *
 * States: idle/loading (fixed-footprint skeleton, no layout shift), success
 * (dimmed previous page on refetch), empty (a non-error), `400` invalid
 * filter (field errors + clear), `403`/permission-denied, unavailable
 * (retryable). A superseded request never lands: a newer request aborts the
 * previous one and its result is dropped.
 */

const SEARCH_DEBOUNCE_MS = 300

/** Convert a `YYYY-MM-DD` picker value to a UTC-midnight ISO string. */
function dateToUtcIso(value: string): string | undefined {
  if (!value) return undefined
  const parsed = new Date(`${value}T00:00:00Z`)
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString()
}

function parseStatus(value: string | null): AdminOrderStatus | undefined {
  if (value === null) return undefined
  const parsed = adminOrderStatusSchema.safeParse(value)
  return parsed.success ? parsed.data : undefined
}

function parsePageSize(raw: string | null): PageSizeOption {
  if (raw === null) return DEFAULT_PAGE_SIZE
  const parsed = Number.parseInt(raw, 10)
  return [10, 20, 50, 100].includes(parsed) ? (parsed as PageSizeOption) : DEFAULT_PAGE_SIZE
}

export function OrdersPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { orders } = useShopClients()
  const { session, signOut } = useAuth()
  const { permissions, isResolving } = useTenantPermissions(tenantId || undefined)
  const canView = permissions?.has(SHOP_ORDERS_VIEW_KEY) ?? false

  const [searchParams, setSearchParams] = useSearchParams()
  const q = searchParams.get('q') ?? ''
  const status = parseStatus(searchParams.get('status'))
  const fromUtc = dateToUtcIso(searchParams.get('fromUtc') ?? '')
  const toUtc = dateToUtcIso(searchParams.get('toUtc') ?? '')
  const pageNumber = Math.max(1, Number.parseInt(searchParams.get('page') ?? '1', 10) || 1)
  const pageSize = parsePageSize(searchParams.get('size'))

  const [draft, setDraft] = useState(q)
  const [reloadKey, setReloadKey] = useState(0)
  const [list, setList] = useState<AdminOrderSummary[] | null>(null)
  const [pagination, setPagination] = useState<AdminOrderListResponse['pagination'] | null>(null)
  const [failure, setFailure] = useState<'validation' | 'forbidden' | 'unavailable' | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [isBusy, setIsBusy] = useState(true)

  const lastGoodRef = useRef<AdminOrderListResponse | null>(null)
  const hasPreviousData = lastGoodRef.current !== null
  const controllerRef = useRef<AbortController | null>(null)

  const signOutRef = useRef(signOut)
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])
  void session

  // Keep the search box in sync with the URL (reload, back/forward, scenario
  // reload). When our own commit lands, `draft` equals `q` and this is a no-op.
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

  const load = useCallback(() => {
    // Superseded-request safety: a newer request aborts the previous one, so a
    // stale result never overwrites the newer state and an abort is silent.
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setIsBusy(true)
    setFailure(null)
    setFieldErrors({})

    const filters = {
      pageNumber,
      pageSize,
      q: q.trim().length > 0 ? q.trim() : undefined,
      status,
      fromUtc,
      toUtc,
    }

    orders
      .list(tenantId, filters, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return
        lastGoodRef.current = res
        setList(res.orders)
        setPagination(res.pagination)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        if (error instanceof DOMException && error.name === 'AbortError') return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        lastGoodRef.current = null
        if (error instanceof ApiUnavailableError) {
          setFailure('unavailable')
        } else if (error instanceof ShopClientError && error.problem.status === 400) {
          setFailure('validation')
          const fields: Record<string, string> = {}
          for (const [key, messages] of Object.entries(error.problem.errors ?? {})) {
            fields[key] = messages[0] ?? 'مقدار معتبر نیست.'
          }
          setFieldErrors(fields)
        } else if (error instanceof ShopClientError && error.problem.status === 403) {
          setFailure('forbidden')
        } else {
          setFailure('unavailable')
        }
        setList(null)
        setPagination(null)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsBusy(false)
      })
  }, [orders, tenantId, pageNumber, pageSize, q, status, fromUtc, toUtc])

  // The single data request. Runs only once the permission set is known and
  // the user holds `Shop.Orders.View`; otherwise the denied state is shown
  // (hiding is presentation — the server is the authority).
  const shouldFetch = !isResolving && canView
  useEffect(() => {
    if (!shouldFetch) return
    load()
    return () => controllerRef.current?.abort()
  }, [shouldFetch, load, reloadKey])

  function writeParams(mutate: (params: URLSearchParams) => void) {
    const next = new URLSearchParams(searchParams)
    mutate(next)
    // Push (not replace) so each distinct filter state is its own history
    // entry: browser back/forward moves between filter states, and reload
    // re-reads the params from the URL. The search box is debounced (300 ms)
    // and the other changes are discrete clicks, so history does not fill up.
    setSearchParams(next)
  }

  function setFilterParam(key: 'status' | 'fromUtc' | 'toUtc', value: string) {
    writeParams((p) => {
      if (value.length > 0) p.set(key, value)
      else p.delete(key)
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
    writeParams((p) => {
      p.set('size', String(size))
      p.delete('page')
    })
  }

  function clearFilters() {
    setDraft('')
    writeParams((p) => {
      p.delete('q')
      p.delete('status')
      p.delete('fromUtc')
      p.delete('toUtc')
      p.delete('page')
    })
  }

  function retry() {
    setReloadKey((key) => key + 1)
  }

  const hasActiveFilter =
    q !== '' || status !== undefined || searchParams.get('fromUtc') !== null || searchParams.get('toUtc') !== null

  if (isResolving) return <DeniedResolving />

  if (!canView) return <DeniedPanel />

  return (
    <DashboardShell>
      <section aria-label="سفارش‌های فروشگاه" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">سفارش‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              فهرست سفارش‌ها با فیلتر وضعیت، جست‌وجو و بازهٔ تاریخ. روی هر سفارش برای مشاهدهٔ جزئیات،
              اسنپ‌شات اقلام، جمع‌ها و تاریخچهٔ پرداخت کلیک کنید.
            </p>
          </div>
          {list !== null && failure === null && (
            <div className="flex shrink-0 items-center gap-2">
              <SecondaryButton type="button" className="px-3" onClick={retry} disabled={isBusy}>
                <RefreshCw aria-hidden="true" className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')} />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            </div>
          )}
        </div>

        {failure === 'unavailable' && <UnavailablePanel onRetry={retry} />}

        {failure === 'validation' && (
          <StatePanel
            density="prominent"
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title="فیلترها معتبر نیست"
            description="یکی از فیلترهای اعمال‌شده درست نیست. مقادیر مشخص‌شده را اصلاح یا پاک کنید."
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={clearFilters}>
                پاک‌کردن فیلترها
              </Button>
            }
          />
        )}

        {failure === 'forbidden' && <DeniedPanel />}

        {failure === null && (
          <>
            <OrdersFilters
              draft={draft}
              onDraftChange={setDraft}
              status={status}
              onStatusChange={(value) => setFilterParam('status', value)}
              fromUtc={searchParams.get('fromUtc') ?? ''}
              onFromChange={(value) => setFilterParam('fromUtc', value)}
              toUtc={searchParams.get('toUtc') ?? ''}
              onToChange={(value) => setFilterParam('toUtc', value)}
              fieldErrors={fieldErrors}
              hasActiveFilter={hasActiveFilter}
              onClearFilters={clearFilters}
              disabled={isBusy && !hasPreviousData}
            />

            {/* Fixed content area: the skeleton and the table share the same
                footprint so loading → data causes no layout shift. */}
            <div
              className={cn(
                'overflow-hidden rounded-xl border border-border bg-surface shadow-soft',
                isBusy && hasPreviousData && 'opacity-60 transition-opacity',
              )}
            >
              {hasPreviousData && list === null && (
                <ListOrders orders={lastGoodRef.current!.orders} />
              )}
              {!hasPreviousData && isBusy && <OrdersSkeleton />}
              {list !== null && list.length === 0 && (
                <EmptyPanel hasActiveFilter={hasActiveFilter} onClearFilters={clearFilters} />
              )}
              {list !== null && list.length > 0 && <ListOrders orders={list} />}
            </div>

            <div className="min-h-[3.25rem]">
              {pagination && (
                <div className="rounded-xl border border-border bg-surface shadow-soft">
                  <PaginationControls
                    meta={pagination}
                    label="سفارش‌ها"
                    disabled={isBusy}
                    onPageChange={setPage}
                    onPageSizeChange={handlePageSizeChange}
                  />
                </div>
              )}
            </div>
          </>
        )}
      </section>
    </DashboardShell>
  )
}

/** Desktop table + mobile cards rendered from the same data. */
function ListOrders({ orders }: { orders: AdminOrderSummary[] }) {
  return (
    <>
      <div className="hidden overflow-x-auto md:block">
        <table className="w-full min-w-[52rem] text-sm">
          <caption className="sr-only">فهرست سفارش‌های فروشگاه</caption>
          <thead>
            <tr className="border-b border-border">
              <th scope="col" className="px-4 py-3 text-start font-semibold">شمارهٔ سفارش</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">مشتری</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">تلفن</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">جمع کل</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">تاریخ ثبت</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold"><span className="sr-only">مشاهدهٔ جزئیات</span></th>
            </tr>
          </thead>
          <tbody>
            {orders.map((order) => (
              <OrderRow key={order.id} order={order} />
            ))}
          </tbody>
        </table>
      </div>
      <ul className="space-y-3 p-3 md:hidden" aria-label="فهرست سفارش‌ها">
        {orders.map((order) => (
          <OrderCard key={order.id} order={order} />
        ))}
      </ul>
    </>
  )
}

function OrderRow({ order }: { order: AdminOrderSummary }) {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const href = `/t/${encodeURIComponent(tenantId)}/shop/orders/${encodeURIComponent(order.id)}`
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3">
        <Link
          to={href}
          className="inline-flex items-center gap-1 font-semibold text-foreground hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <bdi dir="ltr">{order.orderNumber}</bdi>
          <ArrowRight aria-hidden="true" className="size-4 text-muted-foreground" />
        </Link>
      </td>
      <td className="px-4 py-3">{order.customerName}</td>
      <td className="px-4 py-3 text-muted-foreground"><bdi dir="ltr">{order.customerPhone}</bdi></td>
      <td className="px-4 py-3"><OrderStatusBadge status={order.status} /></td>
      <td className="px-4 py-3 text-muted-foreground">{formatMoney(order.grandTotal)} تومان</td>
      <td className="px-4 py-3 text-muted-foreground">
        <time dateTime={order.createdAtUtc}>{formatDateTime(order.createdAtUtc)}</time>
      </td>
      <td className="px-4 py-3">
        <Link
          to={href}
          className="rounded focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          aria-label={`جزئیات سفارش ${order.orderNumber}`}
        >
          <ArrowRight aria-hidden="true" className="size-4 text-muted-foreground" />
        </Link>
      </td>
    </tr>
  )
}

function OrderCard({ order }: { order: AdminOrderSummary }) {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const href = `/t/${encodeURIComponent(tenantId)}/shop/orders/${encodeURIComponent(order.id)}`
  return (
    <li className="rounded-xl border border-border bg-surface p-4 shadow-soft">
      <div className="flex items-center justify-between gap-2">
        <Link
          to={href}
          className="font-semibold text-foreground hover:text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <bdi dir="ltr">{order.orderNumber}</bdi>
        </Link>
        <OrderStatusBadge status={order.status} />
      </div>
      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <div className="col-span-2"><dt className="text-muted-foreground">مشتری</dt><dd>{order.customerName}</dd></div>
        <div><dt className="text-muted-foreground">تلفن</dt><dd><bdi dir="ltr">{order.customerPhone}</bdi></dd></div>
        <div>
          <dt className="text-muted-foreground">تاریخ ثبت</dt>
          <dd><time dateTime={order.createdAtUtc}>{formatDateTime(order.createdAtUtc)}</time></dd>
        </div>
        <div className="col-span-2"><dt className="text-muted-foreground">جمع کل</dt><dd>{formatMoney(order.grandTotal)} تومان</dd></div>
      </dl>
      <div className="mt-4">
        <Link
          to={href}
          className="inline-flex min-h-10 items-center justify-center gap-1 rounded-md border border-border bg-surface px-3 py-2 text-sm font-semibold text-foreground transition-colors duration-150 ease-admin hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          مشاهدهٔ جزئیات
          <ArrowRight aria-hidden="true" className="size-4" />
        </Link>
      </div>
    </li>
  )
}

function OrdersFilters({
  draft,
  onDraftChange,
  status,
  onStatusChange,
  fromUtc,
  onFromChange,
  toUtc,
  onToChange,
  fieldErrors,
  hasActiveFilter,
  onClearFilters,
  disabled,
}: {
  draft: string
  onDraftChange: (value: string) => void
  status: AdminOrderStatus | undefined
  onStatusChange: (value: string) => void
  fromUtc: string
  onFromChange: (value: string) => void
  toUtc: string
  onToChange: (value: string) => void
  fieldErrors: Record<string, string>
  hasActiveFilter: boolean
  onClearFilters: () => void
  disabled: boolean
}) {
  return (
    <div
      role="group"
      aria-label="فیلترهای سفارش‌ها"
      className="grid gap-3 rounded-xl border border-border bg-surface p-4 shadow-soft sm:grid-cols-2 lg:grid-cols-[1.5fr_1fr_1fr_1fr_auto]"
    >
      <div className="sm:col-span-2 lg:col-span-1">
        <label className="mb-2 block text-sm font-semibold" htmlFor="orders-q">جست‌وجو</label>
        <TextInput
          id="orders-q"
          placeholder="شمارهٔ سفارش، کد رهگیری، نام یا تلفن"
          autoComplete="off"
          value={draft}
          disabled={disabled}
          aria-invalid={Boolean(fieldErrors.q)}
          aria-describedby={fieldErrors.q ? 'orders-q-error' : undefined}
          onChange={(event) => onDraftChange(event.target.value)}
        />
        {fieldErrors.q && (
          <p className="mt-2 text-sm text-destructive" id="orders-q-error">{fieldErrors.q}</p>
        )}
      </div>

      <div>
        <label className="mb-2 block text-sm font-semibold" htmlFor="orders-status">وضعیت</label>
        <select
          id="orders-status"
          value={status ?? ''}
          disabled={disabled}
          aria-invalid={Boolean(fieldErrors.status)}
          aria-describedby={fieldErrors.status ? 'orders-status-error' : undefined}
          onChange={(event) => onStatusChange(event.target.value)}
          className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
        >
          <option value="">همهٔ وضعیت‌ها</option>
          <option value="PendingPayment">در انتظار پرداخت</option>
          <option value="Paid">پرداخت‌شده</option>
          <option value="Cancelled">لغو‌شده</option>
          <option value="Fulfilled">تحویل‌شده</option>
        </select>
        {fieldErrors.status && (
          <p className="mt-2 text-sm text-destructive" id="orders-status-error">{fieldErrors.status}</p>
        )}
      </div>

      <div>
        <label className="mb-2 block text-sm font-semibold" htmlFor="orders-from">از تاریخ</label>
        <input
          id="orders-from"
          type="date"
          value={fromUtc}
          disabled={disabled}
          aria-invalid={Boolean(fieldErrors.fromUtc)}
          aria-describedby={fieldErrors.fromUtc ? 'orders-from-error' : undefined}
          onChange={(event) => onFromChange(event.target.value)}
          className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
        />
        {fieldErrors.fromUtc && (
          <p className="mt-2 text-sm text-destructive" id="orders-from-error">{fieldErrors.fromUtc}</p>
        )}
      </div>

      <div>
        <label className="mb-2 block text-sm font-semibold" htmlFor="orders-to">تا تاریخ</label>
        <input
          id="orders-to"
          type="date"
          value={toUtc}
          disabled={disabled}
          aria-invalid={Boolean(fieldErrors.toUtc)}
          aria-describedby={fieldErrors.toUtc ? 'orders-to-error' : undefined}
          onChange={(event) => onToChange(event.target.value)}
          className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
        />
        {fieldErrors.toUtc && (
          <p className="mt-2 text-sm text-destructive" id="orders-to-error">{fieldErrors.toUtc}</p>
        )}
      </div>

      <div className="flex items-end">
        {hasActiveFilter && (
          <SecondaryButton type="button" className="mb-0.5 min-h-11 px-3" onClick={onClearFilters} disabled={disabled}>
            پاک‌کردن فیلترها
          </SecondaryButton>
        )}
      </div>
    </div>
  )
}

function EmptyPanel({ hasActiveFilter, onClearFilters }: { hasActiveFilter: boolean; onClearFilters: () => void }) {
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-12 text-center">
      <span className="inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        {hasActiveFilter ? <SearchX aria-hidden="true" className="size-6" /> : <ClipboardList aria-hidden="true" className="size-6" />}
      </span>
      {hasActiveFilter ? (
        <>
          <p className="text-sm font-semibold">سفارشی مطابق فیلترها پیدا نشد</p>
          <p className="max-w-sm text-xs leading-5 text-muted-foreground">فیلترهای اعمال‌شده روی هیچ سفارشی نمی‌خورند.</p>
          <SecondaryButton type="button" className="mt-2" onClick={onClearFilters}>پاک‌کردن فیلترها</SecondaryButton>
        </>
      ) : (
        <>
          <p className="text-sm font-semibold">هنوز سفارشی ثبت نشده است</p>
          <p className="max-w-sm text-xs leading-5 text-muted-foreground">با ثبت اولین سفارش از فروشگاه، همین‌جا فهرست می‌شود.</p>
        </>
      )}
    </div>
  )
}

function DeniedPanel() {
  return (
    <DashboardShell>
      <section aria-label="دسترسی مجاز نیست" className="space-y-6">
        <DeniedInner />
      </section>
    </DashboardShell>
  )
}

function DeniedInner() {
  return (
    <StatePanel
      density="prominent"
      icon={
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <Lock aria-hidden="true" className="size-6" />
        </span>
      }
      title="مشاهدهٔ سفارش‌ها مجاز نیست"
      description="حساب فعلی مجوز مشاهدهٔ سفارش‌های این مستأجر را ندارد. سرور این درخواست را بدون افشای داده‌ها رد کرده است."
    />
  )
}

function DeniedResolving() {
  return (
    <DashboardShell>
      <section aria-label="بررسی دسترسی" className="space-y-6">
        <div className="h-11 w-40 animate-pulse rounded-md bg-muted motion-reduce:animate-none" aria-busy="true" />
      </section>
    </DashboardShell>
  )
}

function UnavailablePanel({ onRetry }: { onRetry: () => void }) {
  return (
    <StatePanel
      density="prominent"
      icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
      title="سفارش‌ها در دسترس نیست"
      description="هم‌اکنون نمی‌توانیم سفارش‌ها را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید."
      action={
        <Button type="button" className="mt-3 min-w-32" onClick={onRetry}>
          <RefreshCw aria-hidden="true" className="me-2 size-4" />
          تلاش دوباره
        </Button>
      }
    />
  )
}

/** Loading placeholder with the table's footprint so data causes no layout shift. */
function OrdersSkeleton() {
  return (
    <div aria-busy="true">
      <p className="sr-only">در حال بارگذاری سفارش‌ها<span aria-hidden="true">…</span></p>
      <div className="hidden border-b border-border px-4 py-3 md:block">
        <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2, 3].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-28 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-24 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-20 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-28 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}

function formatMoney(value: number): string {
  return value.toLocaleString('fa-IR')
}

function formatDateTime(isoUtc: string): string {
  const date = new Date(isoUtc)
  if (Number.isNaN(date.getTime())) return isoUtc
  return new Intl.DateTimeFormat('fa-IR', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}
