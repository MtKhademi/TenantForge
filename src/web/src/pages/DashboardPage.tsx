import { Activity, RefreshCw, Server, TriangleAlert, Users } from 'lucide-react'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { mockDashboardAdapter } from '@/features/dashboard/dashboardAdapter'
import type { DashboardSummary } from '@/features/dashboard/dashboardTypes'
import { cn } from '@/lib/utils'

/**
 * S03 dashboard summary (F006: contract + mock).
 *
 * States, in order of precedence:
 * - initial loading  – skeleton with the exact footprint of the loaded cards,
 *   so the page never shifts when data arrives;
 * - loaded           – the three contract values only (no invented metrics);
 * - background refetch – values stay put while a refresh is in flight;
 * - retryable error  – no data yet: a full alert panel with retry; data
 *   already shown: a compact inline warning that keeps the last values.
 *
 * Data comes from `mockDashboardAdapter` (task-approved mock). F007 swaps in
 * the real `GET /api/platform/dashboard-summary` call behind the same seam.
 */
export function DashboardPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [refetchFailed, setRefetchFailed] = useState(false)
  // Monotonic guard: a superseded fetch (unmount, rapid retries) never writes.
  const requestIdRef = useRef(0)

  /**
   * Initial fetch: the synchronous state is already correct at mount
   * (`isBusy` starts true, `refetchFailed` starts false), so this path does
   * no synchronous `setState` — the mount effect only subscribes to the
   * adapter, which is its only external system.
   */
  const startInitialFetch = useCallback(() => {
    const requestId = ++requestIdRef.current
    return mockDashboardAdapter
      .fetchSummary()
      .then((next) => {
        if (requestId !== requestIdRef.current) return
        setSummary(next)
      })
      .catch(() => {
        if (requestId !== requestIdRef.current) return
        // Initial failure: summary stays null, the error panel takes over.
        // Refresh failure: keep the last values and flag the refetch.
        setRefetchFailed(true)
      })
      .finally(() => {
        if (requestId === requestIdRef.current) setIsBusy(false)
      })
  }, [])

  /** Refresh/retry (event handlers): mark busy synchronously, then fetch. */
  const refresh = useCallback(() => {
    const requestId = ++requestIdRef.current
    setIsBusy(true)
    setRefetchFailed(false)
    return mockDashboardAdapter
      .fetchSummary()
      .then((next) => {
        if (requestId !== requestIdRef.current) return
        setSummary(next)
      })
      .catch(() => {
        if (requestId !== requestIdRef.current) return
        setRefetchFailed(true)
      })
      .finally(() => {
        if (requestId === requestIdRef.current) setIsBusy(false)
      })
  }, [])

  useEffect(() => {
    void startInitialFetch()
  }, [startInitialFetch])

  const isLoading = summary === null && isBusy
  const isError = summary === null && !isBusy

  return (
    <DashboardShell>
      <section aria-label="خلاصه سیستم" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">خلاصه سیستم</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">
              وضعیت فعلی محیط توسعه
            </h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              فقط اطلاعاتی که این مرحله از سیستم به‌صورت واقعی در دسترس دارد نمایش داده می‌شود؛ هیچ شاخص تحلیلی ساخته‌شده‌ای وجود ندارد.
            </p>
          </div>
          <SecondaryButton
            type="button"
            aria-label="به‌روزرسانی خلاصه"
            className="shrink-0 px-3"
            disabled={isBusy}
            onClick={() => void refresh()}
          >
            <RefreshCw
              aria-hidden="true"
              className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')}
            />
            <span className="hidden sm:inline">به‌روزرسانی</span>
          </SecondaryButton>
        </div>

        {isLoading && <SummarySkeleton />}

        {isError && (
          <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-5" role="alert">
            <div className="flex items-start gap-3">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="text-sm font-semibold">خلاصه داشبورد در دسترس نیست</p>
                <p className="text-sm leading-6 text-muted-foreground">
                  هم‌اکنون نمی‌توانیم خلاصه داشبورد را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.
                </p>
                <Button
                  type="button"
                  className="mt-3 min-w-32"
                  disabled={isBusy}
                  onClick={() => void refresh()}
                >
                  <RefreshCw aria-hidden="true" className="me-2 size-4" />
                  تلاش دوباره
                </Button>
              </div>
            </div>
          </div>
        )}

        {summary && (
          <>
            <dl className="grid gap-4 md:grid-cols-3">
              <SummaryCard
                icon={<Server aria-hidden="true" className="size-5" />}
                label="محیط"
              >
                <bdi className="text-2xl font-semibold tracking-tight">
                  {summary.environment}
                </bdi>
              </SummaryCard>

              <SummaryCard
                icon={<Activity aria-hidden="true" className="size-5" />}
                label="وضعیت API"
              >
                <span className="flex items-center gap-2.5">
                  <span
                    aria-hidden="true"
                    className={cn(
                      'size-2.5 rounded-full',
                      summary.apiStatus === 'Healthy' ? 'bg-success' : 'bg-muted-foreground',
                    )}
                  />
                  <bdi className="text-2xl font-semibold tracking-tight">
                    {summary.apiStatus}
                  </bdi>
                </span>
              </SummaryCard>

              <SummaryCard
                icon={<Users aria-hidden="true" className="size-5" />}
                label="مدیران پلتفرم"
              >
                <span className="text-2xl font-semibold tracking-tight">
                  {formatCount(summary.platformAdminCount)}
                </span>
              </SummaryCard>
            </dl>

            <p className="text-xs text-muted-foreground">
              آخرین به‌روزرسانی:{' '}
              <time dateTime={summary.generatedAtUtc}>
                {formatGeneratedAt(summary.generatedAtUtc)}
              </time>
            </p>

            {refetchFailed && !isBusy && (
              <div
                className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-warning/50 bg-warning/10 p-3 text-sm"
                role="alert"
              >
                <span className="flex items-center gap-2">
                  <TriangleAlert aria-hidden="true" className="size-4 shrink-0 text-warning" />
                  به‌روزرسانی ناموفق بود؛ آخرین مقدارهای بارگذاری‌شده نمایش داده می‌شوند.
                </span>
                <SecondaryButton
                  type="button"
                  className="px-3"
                  disabled={isBusy}
                  onClick={() => void refresh()}
                >
                  <RefreshCw aria-hidden="true" className="me-2 size-4" />
                  تلاش دوباره
                </SecondaryButton>
              </div>
            )}
          </>
        )}
      </section>
    </DashboardShell>
  )
}

type SummaryCardProps = {
  icon: ReactNode
  label: string
  children: ReactNode
}

/** One contract value: icon + label (dt) and the value (dd). */
function SummaryCard({ icon, label, children }: SummaryCardProps) {
  return (
    <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
      <div className="flex items-center gap-3">
        <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
          {icon}
        </span>
        <dt className="text-sm font-semibold">{label}</dt>
      </div>
      <dd className="mt-4">{children}</dd>
    </div>
  )
}

/**
 * Loading placeholder with the exact footprint of the loaded cards (header
 * row + value row), so the transition to data causes no layout shift.
 */
function SummarySkeleton() {
  return (
    <div aria-busy="true" className="space-y-4">
      <p className="sr-only">
        در حال بارگذاری خلاصه داشبورد
        <span aria-hidden="true">…</span>
      </p>
      <div className="grid gap-4 md:grid-cols-3">
        {[0, 1, 2].map((index) => (
          <div key={index} className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <div className="flex items-center gap-3">
              <div className="size-10 shrink-0 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />
              <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            </div>
            <div className="mt-4 h-7 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          </div>
        ))}
      </div>
      <div className="h-3.5 w-44 animate-pulse rounded bg-muted motion-reduce:animate-none" />
    </div>
  )
}

/** Persian (fa-IR) digits for contract numbers. */
function formatCount(value: number) {
  return new Intl.NumberFormat('fa-IR').format(value)
}

/** Localized, readable rendering of the UTC `generatedAtUtc` value. */
function formatGeneratedAt(isoUtc: string) {
  const date = new Date(isoUtc)
  if (Number.isNaN(date.getTime())) return isoUtc
  return new Intl.DateTimeFormat('fa-IR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
