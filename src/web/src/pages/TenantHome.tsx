import { Building2, LogIn, RefreshCw, TriangleAlert } from 'lucide-react'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { SecondaryButton } from '@/components/ui/Button'
import { useTenantScope, type ScopeEntry } from '@/features/tenants/TenantScopeContext'
import { cn } from '@/lib/utils'

/**
 * S11 membership home (F018) — the in-shell landing for an ordinary account
 * after login or reload.
 *
 * A platform administrator is redirected away from here (see `HomeRoute` in
 * `App.tsx`); this page is for accounts that reach it by being a non-admin.
 * The scope list is the caller's own active memberships from
 * `GET /api/auth/me/tenants` (B012) — never the platform list.
 *
 * States, in order of precedence:
 * - loading   — skeleton with the list's footprint (no layout shift);
 * - single    — auto-enters the one tenant (brief "entering" beat, then
 *   navigates — no routine platform request, no 403);
 * - chooser   — two or more memberships: pick one to enter;
 * - empty     — no active memberships: a useful, non-alarming state;
 * - unavailable — the discovery request failed: retryable.
 * A `401`/`403` (disabled account) is handled by the context (sign-out), not
 * rendered here.
 */
export function TenantHome() {
  const { scopes, isBusy, failure, refresh, selectTenant } = useTenantScope()
  const navigate = useNavigate()
  // Auto-enter only once per loaded list; a refetch must not re-trigger it.
  const autoEnteredRef = useRef(false)

  useEffect(() => {
    if (isBusy || scopes === null || scopes.length !== 1 || autoEnteredRef.current) return
    autoEnteredRef.current = true
    navigate(`/t/${encodeURIComponent(scopes[0].id)}`, { replace: true })
  }, [isBusy, scopes, navigate])

  const isLoading = scopes === null && isBusy
  const isError = scopes === null && !isBusy && failure !== null
  const isEmpty = scopes !== null && scopes.length === 0
  const isChooser = scopes !== null && scopes.length >= 2
  const isEntering = scopes !== null && scopes.length === 1

  return (
    <DashboardShell>
      <section aria-label="مستأجران من" className="space-y-6">
        <div className="max-w-2xl">
          <p className="text-sm font-semibold text-primary">محدوده‌های من</p>
          <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">
            {isEntering ? 'در حال ورود به مستأجر شما' : 'انتخاب مستأجر'}
          </h2>
          <p className="mt-2 text-sm leading-6 text-muted-foreground">
            {isEntering
              ? 'بر اساس عضویت فعال شما در حال ورود به تنها مستأجر شما هستیم.'
              : isChooser
                ? 'به یکی از مستأجرانی که عضو آن هستید وارد شوید. انتخاب محدوده فقط ناوبری است؛ دسترسی را سرور بر اساس عضویت شما تأیید می‌کند.'
                : 'مستأجرانی که عضو آن‌ها هستید اینجا نمایش داده می‌شوند.'}
          </p>
        </div>

        {isLoading && <ScopeListSkeleton />}

        {isError && (
          <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-5" role="alert">
            <div className="flex items-start gap-3">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="text-sm font-semibold">فهرست مستأجران شما در دسترس نیست</p>
                <p className="text-sm leading-6 text-muted-foreground">
                  هم‌اکنون نمی‌توانیم مستأجرانی که عضو آن‌ها هستید را بارگذاری کنیم. اتصال را
                  بررسی کنید و دوباره تلاش کنید.
                </p>
                <SecondaryButton type="button" className="mt-3" onClick={() => void refresh()}>
                  <RefreshCw aria-hidden="true" className="me-2 size-4" />
                  تلاش دوباره
                </SecondaryButton>
              </div>
            </div>
          </div>
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <Building2 aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">هنوز عضوی در مستأجری ندارید</p>
            <p className="mx-auto mt-1 max-w-md text-sm leading-6 text-muted-foreground">
              وقتی مدیر پلتفرم شما را به یک مستأجر اضافه کند، آن مستأجر اینجا ظاهر می‌شود و از
              همین‌جا وارد آن خواهید شد.
            </p>
          </div>
        )}

        {isChooser && (
          <ul className="space-y-3">
            {scopes!.map((tenant) => (
              <ScopeRow key={tenant.id} tenant={tenant} onEnter={() => selectTenant(tenant.id)} />
            ))}
          </ul>
        )}
      </section>
    </DashboardShell>
  )
}

function ScopeRow({ tenant, onEnter }: { tenant: ScopeEntry; onEnter: () => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={onEnter}
        aria-label={`ورود به مستأجر ${tenant.name}`}
        className="group flex w-full items-center gap-4 rounded-xl border border-border bg-surface p-5 text-start shadow-soft transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      >
        <span className="inline-flex size-11 shrink-0 items-center justify-center rounded-lg bg-primary/15 text-primary">
          <Building2 aria-hidden="true" className="size-5" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <bdi className="truncate text-base font-semibold">{tenant.name}</bdi>
            <MembershipRoleBadge role={tenant.membershipRole} />
          </span>
          <code dir="ltr" className="mt-0.5 block truncate text-xs text-muted-foreground">
            {tenant.slug}
          </code>
        </span>
        <span className="inline-flex shrink-0 items-center gap-1.5 rounded-md border border-border bg-background px-3 py-2 text-xs font-semibold text-foreground transition-colors group-hover:bg-surface">
          <LogIn aria-hidden="true" className="size-3.5" />
          ورود
        </span>
      </button>
    </li>
  )
}

/** Localized membership kind: `Owner` → مالک, `Member` → عضو. */
function MembershipRoleBadge({ role }: { role: ScopeEntry['membershipRole'] }) {
  const isOwner = role === 'Owner'
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        isOwner ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
      )}
    >
      <span
        aria-hidden="true"
        className={cn('size-1.5 rounded-full', isOwner ? 'bg-primary' : 'bg-muted-foreground')}
      />
      {isOwner ? 'مالک' : 'عضو'}
    </span>
  )
}

/** Loading placeholder matching the row footprint to avoid layout shift. */
function ScopeListSkeleton() {
  return (
    <div aria-busy="true" className="space-y-3">
      <p className="sr-only">
        در حال بارگذاری مستأجران شما
        <span aria-hidden="true">…</span>
      </p>
      {[0, 1].map((index) => (
        <div
          key={index}
          className="flex items-center gap-4 rounded-xl border border-border bg-surface p-5 shadow-soft"
        >
          <div className="size-11 shrink-0 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />
          <div className="min-w-0 flex-1">
            <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="mt-2 h-3 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          </div>
          <div className="h-8 w-20 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}
