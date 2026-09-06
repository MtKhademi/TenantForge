import {
  KeyRound,
  Lock,
  MailPlus,
  RefreshCw,
  ScrollText,
  SearchX,
  TriangleAlert,
  UserCheck,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { httpAuditAdapter } from '@/features/audit/auditAdapter'
import {
  AuditForbiddenError,
  type AuditAction,
  type AuditEvent,
} from '@/features/audit/auditTypes'
import { cn } from '@/lib/utils'

/**
 * S10 audit log — connected to the real B010 API (F016).
 *
 * Shows an immutable, tenant-scoped log of sensitive actions: each row carries
 * the actor, action, target, a one-line detail and the time. F015's
 * sessionStorage mock (with its seeded role-change event) is gone; every event
 * shown here is now recorded server-side around a real sensitive command
 * (invitation creation, role create/update/assign/unassign).
 *
 * States:
 * - loading: request in flight (skeleton);
 * - loaded: filter bar + event table (which may be empty);
 * - no matches: filters returned nothing (distinct from a truly empty log);
 * - forbidden: a caller without `IAM.Audit.View` — B010 returns a non-leaking
 *   403;
 * - unavailable: network/server failure — retryable.
 *
 * Filters (action + from-date) are sent to B010 as `action`/`fromUtc` query
 * parameters, unchanged from the F015 contract.
 */

type AuditState =
  | { kind: 'loading' }
  | { kind: 'loaded'; events: AuditEvent[] }
  | { kind: 'forbidden' }
  | { kind: 'unavailable' }

type AuditFilters = {
  action: AuditAction | ''
  fromUtc: string
}

/** Persian labels for the stable action keys, used by the table and the filter. */
const ACTION_META: Record<AuditAction, { label: string; icon: LucideIcon }> = {
  'Invitation.Created': { label: 'ایجاد دعوت', icon: MailPlus },
  'Role.Created': { label: 'ایجاد نقش', icon: KeyRound },
  'Role.Updated': { label: 'به‌روزرسانی نقش', icon: KeyRound },
  'Role.Assigned': { label: 'انتساب نقش', icon: UserCheck },
  'Role.Unassigned': { label: 'برداشتن نقش', icon: UserCheck },
}

const ACTION_OPTIONS: { value: AuditAction; label: string }[] = (
  Object.keys(ACTION_META) as AuditAction[]
).map((value) => ({ value, label: ACTION_META[value].label }))

export function AuditLogPage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const { session, signOut } = useAuth()
  const [state, setState] = useState<AuditState>({ kind: 'loading' })
  const [filters, setFilters] = useState<AuditFilters>({ action: '', fromUtc: '' })

  const requestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const loadEvents = useCallback(() => {
    if (!tenantId) return
    const requestId = ++requestIdRef.current
    setState({ kind: 'loading' })
    const fromUtc = toFromUtc(filters.fromUtc)
    const query = {
      action: filters.action || undefined,
      fromUtc: fromUtc || undefined,
    }
    httpAuditAdapter
      .listAuditEvents(sessionRef.current?.accessToken ?? '', tenantId, query)
      .then((response) => {
        if (requestId !== requestIdRef.current) return
        setState({ kind: 'loaded', events: response.events })
      })
      .catch((error) => {
        if (requestId !== requestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        if (error instanceof AuditForbiddenError) {
          setState({ kind: 'forbidden' })
          return
        }
        setState({ kind: 'unavailable' })
      })
  }, [tenantId, filters.action, filters.fromUtc])

  useEffect(() => {
    loadEvents()
  }, [loadEvents])

  const hasActiveFilter = filters.action !== '' || filters.fromUtc !== ''

  return (
    <DashboardShell>
      <section aria-label="گزارش فعالیت مستأجر" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold text-primary">مدیریت مستأجر</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">گزارش فعالیت</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              تاریخچهٔ بی‌تغییر عملیات حساس این مستأجر — ایجاد دعوت و تغییر نقش‌ها و مجوزها — همراه
              با انجام‌دهنده، هدف و زمان.
            </p>
          </div>
          {state.kind === 'loaded' && (
            <div className="flex shrink-0 items-center gap-2">
              <SecondaryButton type="button" className="px-3" onClick={loadEvents}>
                <RefreshCw aria-hidden="true" className="size-4" />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            </div>
          )}
        </div>

        {state.kind === 'loading' && <AuditSkeleton />}
        {state.kind === 'forbidden' && <ForbiddenAudit />}
        {state.kind === 'unavailable' && <UnavailableAudit onRetry={loadEvents} />}

        {state.kind === 'loaded' && (
          <div className="space-y-4">
            <AuditFilters
              value={filters}
              hasActiveFilter={hasActiveFilter}
              onChange={setFilters}
            />
            <AuditTable
              events={state.events}
              hasActiveFilter={hasActiveFilter}
              onClearFilters={() => setFilters({ action: '', fromUtc: '' })}
            />
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

/** Converts a `YYYY-MM-DD` date-picker value to a UTC midnight ISO string. */
function toFromUtc(dateValue: string) {
  if (!dateValue) return ''
  const parsed = new Date(`${dateValue}T00:00:00Z`)
  return Number.isNaN(parsed.getTime()) ? '' : parsed.toISOString()
}

function AuditFilters({
  value,
  hasActiveFilter,
  onChange,
}: {
  value: AuditFilters
  hasActiveFilter: boolean
  onChange: (filters: AuditFilters) => void
}) {
  return (
    <div
      className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-surface p-4 shadow-soft"
      role="group"
      aria-label="فیلترهای گزارش فعالیت"
    >
      <div className="min-w-40 flex-1 sm:flex-none">
        <label className="mb-2 block text-sm font-semibold" htmlFor="audit-action">عملیات</label>
        <select
          id="audit-action"
          value={value.action}
          onChange={(event) => onChange({ ...value, action: event.target.value as AuditFilters['action'] })}
          className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring md:text-sm"
        >
          <option value="">همهٔ عملیات</option>
          {ACTION_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>{option.label}</option>
          ))}
        </select>
      </div>

      <div className="min-w-40 flex-1 sm:flex-none">
        <label className="mb-2 block text-sm font-semibold" htmlFor="audit-from">از تاریخ</label>
        <input
          id="audit-from"
          type="date"
          value={value.fromUtc}
          onChange={(event) => onChange({ ...value, fromUtc: event.target.value })}
          className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring md:text-sm"
        />
      </div>

      {hasActiveFilter && (
        <SecondaryButton
          type="button"
          className="mb-0.5 px-3"
          onClick={() => onChange({ action: '', fromUtc: '' })}
        >
          پاک‌کردن فیلترها
        </SecondaryButton>
      )}
    </div>
  )
}

function AuditTable({
  events,
  hasActiveFilter,
  onClearFilters,
}: {
  events: AuditEvent[]
  hasActiveFilter: boolean
  onClearFilters: () => void
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
      {events.length === 0 ? (
        <div className="flex flex-col items-center gap-2 px-4 py-12 text-center">
          {hasActiveFilter ? (
            <>
              <SearchX aria-hidden="true" className="size-6 text-muted-foreground" />
              <p className="text-sm font-medium">نتیجه‌ای مطابق فیلترها پیدا نشد</p>
              <p className="max-w-sm text-xs leading-5 text-muted-foreground">
                فیلترهای اعمال‌شده روی هیچ رویدادی نمی‌خورند.
              </p>
              <SecondaryButton type="button" className="mt-2" onClick={onClearFilters}>
                پاک‌کردن فیلترها
              </SecondaryButton>
            </>
          ) : (
            <>
              <ScrollText aria-hidden="true" className="size-6 text-muted-foreground" />
              <p className="text-sm font-medium">هنوز رویدادی ثبت نشده است</p>
              <p className="max-w-sm text-xs leading-5 text-muted-foreground">
                با ایجاد دعوت یا تغییر نقش، اولین رویداد همین‌جا ثبت می‌شود.
              </p>
            </>
          )}
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[46rem] text-sm">
            <caption className="sr-only">رویدادهای گزارش فعالیت این مستأجر</caption>
            <thead>
              <tr className="border-b border-border">
                <th scope="col" className="px-4 py-3 text-start font-semibold">عملکرد</th>
                <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                <th scope="col" className="px-4 py-3 text-start font-semibold">هدف</th>
                <th scope="col" className="px-4 py-3 text-start font-semibold">جزئیات</th>
                <th scope="col" className="px-4 py-3 text-start font-semibold">زمان</th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => (
                <AuditRow key={event.id} event={event} />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function AuditRow({ event }: { event: AuditEvent }) {
  const meta = ACTION_META[event.action]
  const Icon = meta.icon
  const isInvitation = event.action === 'Invitation.Created'
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3 align-top">
        <span className="font-medium"><bdi>{event.actor}</bdi></span>
        <span className="block text-xs text-muted-foreground"><bdi dir="ltr">{event.actorEmail}</bdi></span>
      </td>
      <td className="px-4 py-3 align-top">
        <span
          className={cn(
            'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
            isInvitation ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
          )}
        >
          <Icon aria-hidden="true" className="size-3.5" />
          {meta.label}
        </span>
      </td>
      <td className="px-4 py-3 align-top">
        <bdi className={event.target.includes('@') ? 'break-all' : undefined}>
          {event.target.includes('@') ? <span dir="ltr">{event.target}</span> : event.target}
        </bdi>
      </td>
      <td className="px-4 py-3 align-top text-muted-foreground">{event.details}</td>
      <td className="whitespace-nowrap px-4 py-3 align-top text-muted-foreground">
        <time dateTime={event.createdAtUtc}>{formatDate(event.createdAtUtc)}</time>
      </td>
    </tr>
  )
}

function ForbiddenAudit() {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <Lock aria-hidden="true" className="size-6" />
        </span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">مشاهدهٔ گزارش فعالیت مجاز نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            حساب فعلی مجوز مشاهدهٔ گزارش فعالیت این مستأجر را ندارد. مخفی‌سازی کنترل‌های UI امنیت
            محسوب نمی‌شود؛ B010 باید همین عملیات را سمت سرور با 403 رد کند.
          </p>
        </div>
      </div>
    </div>
  )
}

function UnavailableAudit({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">گزارش فعالیت در دسترس نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            هم‌اکنون نمی‌توانیم گزارش فعالیت را بارگذاری کنیم.
          </p>
          <Button type="button" className="mt-3" onClick={onRetry}>
            <RefreshCw aria-hidden="true" className="me-2 size-4" />
            تلاش دوباره
          </Button>
        </div>
      </div>
    </div>
  )
}

function AuditSkeleton() {
  return (
    <div className="space-y-4" aria-busy="true">
      <div className="rounded-xl border border-border bg-surface p-4 shadow-soft">
        <p className="sr-only">در حال بارگذاری گزارش فعالیت…</p>
        <div className="flex flex-wrap gap-3">
          <div className="h-11 w-40 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
          <div className="h-11 w-40 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
        </div>
      </div>
      <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
        <div className="border-b border-border px-4 py-3">
          <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
        {[0, 1, 2, 3].map((index) => (
          <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
            <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="h-5 w-24 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
            <div className="h-4 flex-1 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="h-4 w-28 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          </div>
        ))}
      </div>
    </div>
  )
}

/** Persian (fa-IR) rendering of a UTC ISO timestamp. */
function formatDate(isoUtc: string) {
  const date = new Date(isoUtc)
  if (Number.isNaN(date.getTime())) return isoUtc
  return new Intl.DateTimeFormat('fa-IR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
