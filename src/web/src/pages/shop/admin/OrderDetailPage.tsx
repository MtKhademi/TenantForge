import {
  ArrowRight,
  Banknote,
  CalendarClock,
  ChevronLeft,
  Lock,
  MapPin,
  PackageOpen,
  Receipt,
  RefreshCw,
  TriangleAlert,
  Wallet,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import type { AdminOrderDetail } from '@/features/shop/contracts/adminOrdersContract'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { SHOP_ORDERS_VIEW_KEY } from '@/features/roles/roleTypes'
import { useTenantPermissions } from '@/features/roles/tenantPermissions'
import { cn } from '@/lib/utils'
import { OrderStatusBadge } from './OrderStatusBadge'

/**
 * S38 (F050): the permission-gated, read-only admin order detail. Consumes the
 * `orders` client slot (`useShopClients()`) — it never imports fixtures and
 * never calls `fetch`.
 *
 * Every value shown is the order's frozen snapshot (the stored item
 * name/price/variant, customer and address) — never a live join to the current
 * product or customer data. The payment-attempt history is bounded to the 20
 * newest (B042's temporary cap), so the list never grows without bound.
 *
 * This task is read-only: there are NO fulfil/cancel controls here — those
 * belong to F051. States: loading (fixed-footprint skeleton, no layout shift),
 * success, `403`/permission-denied, a non-leaking `404` (malformed, missing or
 * other-tenant order id all render the same generic not-found), and unavailable
 * (retryable). A superseded request never lands.
 */

/** B042's temporary cap on the payment-attempt history (newest first). */
const PAYMENT_ATTEMPT_CAP = 20

export function OrderDetailPage() {
  const { tenantId = '', orderId = '' } = useParams<{ tenantId: string; orderId: string }>()
  const { orders } = useShopClients()
  const { session, signOut } = useAuth()
  const { permissions, isResolving } = useTenantPermissions(tenantId || undefined)
  const canView = permissions?.has(SHOP_ORDERS_VIEW_KEY) ?? false

  const [reloadKey, setReloadKey] = useState(0)
  const [detail, setDetail] = useState<AdminOrderDetail | null>(null)
  const [failure, setFailure] = useState<'notFound' | 'forbidden' | 'unavailable' | null>(null)
  const [isBusy, setIsBusy] = useState(true)

  const lastGoodRef = useRef<AdminOrderDetail | null>(null)
  const hasPreviousData = lastGoodRef.current !== null
  const controllerRef = useRef<AbortController | null>(null)

  const signOutRef = useRef(signOut)
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])
  void session

  const load = useCallback(() => {
    // Superseded-request safety: a newer request aborts the previous one, so a
    // stale result never overwrites the newer state and an abort is silent.
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setIsBusy(true)
    setFailure(null)

    orders
      .get(tenantId, orderId, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return
        lastGoodRef.current = res
        setDetail(res)
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
        } else if (error instanceof ShopClientError && error.problem.status === 404) {
          setFailure('notFound')
        } else if (error instanceof ShopClientError && error.problem.status === 403) {
          setFailure('forbidden')
        } else {
          setFailure('unavailable')
        }
        setDetail(null)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsBusy(false)
      })
  }, [orders, tenantId, orderId])

  const shouldFetch = !isResolving && canView
  useEffect(() => {
    if (!shouldFetch) return
    load()
    return () => controllerRef.current?.abort()
  }, [shouldFetch, load, reloadKey])

  const listsBackHref = `/t/${encodeURIComponent(tenantId)}/shop/orders`

  function retry() {
    setReloadKey((key) => key + 1)
  }

  if (isResolving) return <DetailResolving />
  if (!canView) return <DetailDenied />

  const shown = detail ?? (isBusy && hasPreviousData ? lastGoodRef.current : null)

  return (
    <DashboardShell>
      <section aria-label="جزئیات سفارش" className="space-y-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Link
            to={listsBackHref}
            className="inline-flex min-h-10 items-center gap-1 text-sm font-semibold text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            <ChevronLeft aria-hidden="true" className="size-4" />
            بازگشت به فهرست سفارش‌ها
          </Link>
          {detail !== null && failure === null && (
            <SecondaryButton type="button" className="px-3" onClick={retry} disabled={isBusy}>
              <RefreshCw aria-hidden="true" className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')} />
              <span className="hidden sm:inline">به‌روزرسانی</span>
            </SecondaryButton>
          )}
        </div>

        {failure === 'forbidden' && <DetailDeniedInline />}

        {failure === 'notFound' && <NotFoundPanel listsBackHref={listsBackHref} />}

        {failure === 'unavailable' && (
          <StatePanel
            density="prominent"
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title="سفارش در دسترس نیست"
            description="هم‌اکنون نمی‌توانیم سفارش را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید."
            action={
              <SecondaryButton type="button" className="mt-3 min-w-32" onClick={retry}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </SecondaryButton>
            }
          />
        )}

        {failure === null && (
          <div className={cn(isBusy && hasPreviousData && 'opacity-60 transition-opacity')}>
            {shown === null ? <DetailSkeleton /> : <DetailContent order={shown} />}
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function DetailContent({ order }: { order: AdminOrderDetail }) {
  const { customer, totals, items, paymentAttempts } = order
  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1">
            <p className="text-sm font-semibold text-primary">سفارش فروشگاه</p>
            <h2 className="text-2xl font-semibold tracking-tight">
              <bdi dir="ltr">{order.orderNumber}</bdi>
            </h2>
            <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground">
              <span className="inline-flex items-center gap-1.5">
                <CalendarClock aria-hidden="true" className="size-4" />
                ثبت در <time dateTime={order.createdAtUtc}>{formatDateTime(order.createdAtUtc)}</time>
              </span>
              <span className="inline-flex items-center gap-1.5">
                کد رهگیری <bdi dir="ltr" className="font-medium text-foreground">{order.trackingCode}</bdi>
              </span>
            </p>
          </div>
          <OrderStatusBadge status={order.status} />
        </div>
        <p className="mt-3 text-xs text-muted-foreground">
          نسخهٔ فعلی: <bdi dir="ltr">{order.version}</bdi>
        </p>
      </div>

      {/* Customer + address */}
      <div className="grid gap-6 lg:grid-cols-2">
        <Card title="مشتری" icon={Wallet}>
          <dl className="mt-4 space-y-3 text-sm">
            <Field label="نام"><span>{customer.name}</span></Field>
            <Field label="تلفن"><bdi dir="ltr">{customer.phone}</bdi></Field>
          </dl>
        </Card>
        <Card title="آدرس ارسال" icon={MapPin}>
          <dl className="mt-4 space-y-3 text-sm">
            <Field label="استان"><span>{customer.shippingProvince}</span></Field>
            <Field label="شهر"><span>{customer.shippingCity}</span></Field>
            <Field label="آدرس"><span>{customer.shippingAddressLine}</span></Field>
            <Field label="کد پستی"><bdi dir="ltr">{customer.shippingPostalCode}</bdi></Field>
          </dl>
        </Card>
      </div>

      {/* Items (stored snapshots) */}
      <Card title="اقلام سفارش (اسنپ‌شات لحظهٔ ثبت)" icon={PackageOpen}>
        <p className="mt-1 text-xs text-muted-foreground">
          نام، مشخصات و قیمت اقلام دقیقاً همان‌اند که هنگام ثبت سفارش ذخیره شده‌اند؛ تغییرات بعدیِ محصول روی
          این نمایش اثر نمی‌گذارند.
        </p>
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[40rem] text-sm">
            <caption className="sr-only">اقلام سفارش</caption>
            <thead>
              <tr className="border-b border-border">
                <th scope="col" className="px-3 py-2.5 text-start font-semibold">محصول</th>
                <th scope="col" className="px-3 py-2.5 text-start font-semibold">مشخصات</th>
                <th scope="col" className="px-3 py-2.5 text-start font-semibold">قیمت واحد</th>
                <th scope="col" className="px-3 py-2.5 text-start font-semibold">تعداد</th>
                <th scope="col" className="px-3 py-2.5 text-start font-semibold">جمع</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item, index) => (
                <tr key={index} className="border-b border-border last:border-b-0">
                  <td className="px-3 py-2.5 align-top">{item.productNameSnapshot}</td>
                  <td className="px-3 py-2.5 align-top text-muted-foreground">{item.variantLabelSnapshot}</td>
                  <td className="px-3 py-2.5 align-top text-muted-foreground">{formatMoney(item.unitPrice)} تومان</td>
                  <td className="px-3 py-2.5 align-top text-muted-foreground">{item.quantity.toLocaleString('fa-IR')}</td>
                  <td className="px-3 py-2.5 align-top text-muted-foreground">
                    {formatMoney(item.unitPrice * item.quantity)} تومان
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Totals */}
      <Card title="جمع‌ها" icon={Banknote}>
        <dl className="mt-4 divide-y divide-border text-sm">
          <TotalRow label="جمع اقلام" value={totals.subTotal} />
          <TotalRow label="هزینهٔ ارسال" value={totals.shippingCost} />
          <TotalRow label="تخفیف" value={-totals.discountAmount} />
          <div className="flex items-center justify-between py-2.5 text-base font-semibold">
            <dt>جمع کل</dt>
            <dd dir="ltr">{formatMoney(totals.grandTotal)} تومان</dd>
          </div>
        </dl>
      </Card>

      {/* Payment history (capped) */}
      <PaymentHistory attempts={paymentAttempts} cap={PAYMENT_ATTEMPT_CAP} />
    </div>
  )
}

function PaymentHistory({ attempts, cap }: { attempts: AdminOrderDetail['paymentAttempts']; cap: number }) {
  const remaining = attempts.length > cap ? attempts.length - cap : 0
  return (
    <Card title="تاریخچهٔ پرداخت" icon={Receipt}>
      <p className="mt-1 text-xs text-muted-foreground">
        فقط {cap.toLocaleString('fa-IR')} تلاشِ پرداخت جدیدترین به نمایش درمی‌آید.
      </p>
      {attempts.length === 0 ? (
        <p className="mt-4 rounded-lg bg-muted px-4 py-3 text-sm text-muted-foreground">
          تاکنون تلاشِ پرداختی برای این سفارش ثبت نشده است.
        </p>
      ) : (
        <ol className="mt-4 divide-y divide-border">
          {attempts.map((attempt, index) => (
            <li key={attempt.id} className="flex items-center justify-between gap-3 py-2.5 text-sm">
              <span className="inline-flex items-center gap-2">
                <span className="inline-flex size-6 items-center justify-center rounded-md bg-muted text-xs font-semibold text-muted-foreground">
                  {new Intl.NumberFormat('fa-IR').format(attempts.length - index)}
                </span>
                <bdi dir="ltr" className="text-muted-foreground">{attempt.id.slice(-6)}</bdi>
              </span>
              <span><PaymentAttemptStatus status={attempt.status} /></span>
              <time dateTime={attempt.createdAtUtc} className="text-muted-foreground">
                {formatDateTime(attempt.createdAtUtc)}
              </time>
            </li>
          ))}
        </ol>
      )}
      {remaining > 0 && (
        <p className="mt-3 text-xs text-muted-foreground">
          {remaining.toLocaleString('fa-IR')} تلاشِ قدیمی‌تر خارج از این محدوده نمایش داده نمی‌شود.
        </p>
      )}
    </Card>
  )
}

/** Localized label for the minimal payment-attempt status string (wire value is free-form). */
function PaymentAttemptStatus({ status }: { status: string }) {
  const labels: Record<string, string> = {
    Initiated: 'در حال شروع',
    Paid: 'پرداخت‌شده',
    Declined: 'ردشده',
    Failed: 'ناموفق',
  }
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        status === 'Paid' && 'bg-success/10 text-success',
        status === 'Declined' && 'bg-destructive/10 text-destructive',
        status === 'Failed' && 'bg-destructive/10 text-destructive',
        status === 'Initiated' && 'bg-warning/10 text-warning',
        !(status in labels) && 'bg-muted text-muted-foreground',
      )}
    >
      <span aria-hidden="true" className="size-1.5 rounded-full bg-current" />
      {labels[status] ?? status}
    </span>
  )
}

function Card({ title, icon: Icon, children }: { title: string; icon: LucideIcon; children: ReactNode }) {
  return (
    <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
      <div className="flex items-center gap-2">
        <span className="inline-flex size-8 items-center justify-center rounded-lg bg-muted text-muted-foreground">
          <Icon aria-hidden="true" className="size-4" />
        </span>
        <h3 className="text-base font-semibold">{title}</h3>
      </div>
      {children}
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5">{children}</dd>
    </div>
  )
}

function TotalRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-center justify-between py-2.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd dir="ltr">{formatMoney(value)} تومان</dd>
    </div>
  )
}

function NotFoundPanel({ listsBackHref }: { listsBackHref: string }) {
  return (
    <StatePanel
      density="prominent"
      icon={
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <PackageOpen aria-hidden="true" className="size-6" />
        </span>
      }
      title="این سفارش پیدا نشد"
      description="سفارشی با این شناسه برای شما در دسترس نیست یا دیگر وجود ندارد."
      action={
        <Link
          to={listsBackHref}
          className="mt-3 inline-flex min-h-10 items-center justify-center gap-1 rounded-md bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground shadow-soft transition-[background-color,box-shadow,transform] duration-150 ease-admin hover:brightness-105 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <ArrowRight aria-hidden="true" className="size-4" />
          بازگشت به فهرست
        </Link>
      }
    />
  )
}

function DetailDeniedInline() {
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

function DetailDenied() {
  return (
    <DashboardShell>
      <section aria-label="دسترسی مجاز نیست" className="space-y-6">
        <DetailDeniedInline />
      </section>
    </DashboardShell>
  )
}

function DetailResolving() {
  return (
    <DashboardShell>
      <section aria-label="بررسی دسترسی" className="space-y-6">
        <div className="h-11 w-40 animate-pulse rounded-md bg-muted motion-reduce:animate-none" aria-busy="true" />
      </section>
    </DashboardShell>
  )
}

/** Fixed-footprint skeleton so loading → data causes no layout shift. */
function DetailSkeleton() {
  return (
    <div className="space-y-6" aria-busy="true">
      <p className="sr-only">در حال بارگذاری سفارش<span aria-hidden="true">…</span></p>
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <div className="h-5 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        <div className="mt-3 h-7 w-52 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        <div className="mt-3 h-4 w-64 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      <div className="grid gap-6 lg:grid-cols-2">
        <div className="h-40 rounded-xl border border-border bg-surface shadow-soft" />
        <div className="h-40 rounded-xl border border-border bg-surface shadow-soft" />
      </div>
      <div className="h-56 rounded-xl border border-border bg-surface shadow-soft" />
      <div className="h-48 rounded-xl border border-border bg-surface shadow-soft" />
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
