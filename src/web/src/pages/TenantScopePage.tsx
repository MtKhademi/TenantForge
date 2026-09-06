import {
  Building2,
  ChevronRight,
  KeyRound,
  Loader2,
  Lock,
  RefreshCw,
  ShieldCheck,
  Users,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { SecondaryButton } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import { httpTenantMembersAdapter, TenantAccessDeniedError } from '@/features/tenants/tenantMembersAdapter'
import type { TenantMember, TenantMembersResponse } from '@/features/tenants/tenantTypes'
import { cn } from '@/lib/utils'

/**
 * S08 tenant isolation — tenant-scoped member view (F012, connected to B008).
 *
 * The URL (`/t/:tenantId`) is the single source of truth for the active
 * tenant, derived with `useParams`. The page asks B008 for this tenant's
 * members; B008 returns them **only** when the signed-in account holds an
 * active membership in that tenant. Selecting a tenant in the UI is
 * presentation only — it never grants access, and a non-member is answered
 * with a designed `403` page while the server is what actually denied the
 * request. The forbidden response leaks no tenant or member data.
 *
 * States:
 * - resolving: initial/tenant-change request in flight (no data yet);
 * - loaded: tenant context banner + member table (refetch keeps the table);
 * - invalid selection: id not in the loaded platform list (no API call);
 * - forbidden: server returned 403 — designed denial + recovery;
 * - unavailable: network/server failure — retryable.
 * A `401` signs the user out (handled, no dedicated panel).
 */

type MembersState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: TenantMembersResponse }
  | { kind: 'forbidden' }
  | { kind: 'unavailable' }

export function TenantScopePage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const { tenants, selectPlatform, getTenantById } = useTenantScope()
  const { session, signOut } = useAuth()

  const [state, setState] = useState<MembersState>({ kind: 'loading' })
  const requestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  // Fetch this tenant's members whenever the route tenant changes. A
  // superseded request never writes, so a fast tenant switch cannot flash the
  // wrong tenant's data.
  useEffect(() => {
    if (!tenantId) return
    const requestId = ++requestIdRef.current
    setState({ kind: 'loading' })
    httpTenantMembersAdapter
      .getTenantMembers(sessionRef.current?.accessToken ?? '', tenantId)
      .then((data) => {
        if (requestId === requestIdRef.current) setState({ kind: 'loaded', data })
      })
      .catch((error) => {
        if (requestId !== requestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        if (error instanceof TenantAccessDeniedError) {
          setState({ kind: 'forbidden' })
          return
        }
        setState({ kind: 'unavailable' })
      })
  }, [tenantId])

  const retry = useCallback(() => {
    // Re-run the effect by bumping a counter would require extra plumbing; the
    // route is already fixed, so force a fresh fetch by re-invoking the same
    // adapter path directly.
    if (!tenantId) return
    const requestId = ++requestIdRef.current
    setState({ kind: 'loading' })
    httpTenantMembersAdapter
      .getTenantMembers(sessionRef.current?.accessToken ?? '', tenantId)
      .then((data) => {
        if (requestId === requestIdRef.current) setState({ kind: 'loaded', data })
      })
      .catch((error) => {
        if (requestId !== requestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setState({ kind: error instanceof TenantAccessDeniedError ? 'forbidden' : 'unavailable' })
      })
  }, [tenantId])

  const listSettled = tenants !== null
  // "Invalid selection" only makes sense when the signed-in user can see the
  // platform list (a platform admin). A non-member gets a 403 from the
  // members endpoint instead — the two cases are intentionally indistinguishable.
  const invalidSelection =
    listSettled &&
    state.kind !== 'loading' &&
    tenantId !== undefined &&
    getTenantById(tenantId) === null

  return (
    <DashboardShell>
      <section aria-label="محدوده مستأجر" className="space-y-6">
        <button
          type="button"
          onClick={selectPlatform}
          className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-border bg-surface px-3 text-sm font-medium transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <ChevronRight aria-hidden="true" className="size-4" />
          بازگشت به پلتفرم
        </button>

        {tenantId && (
          <nav className="flex flex-wrap gap-2" aria-label="ناوبری محدوده مستأجر">
            <span className="inline-flex min-h-9 items-center gap-1.5 rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">
              <Users aria-hidden="true" className="size-4" />
              اعضا
            </span>
            <Link
              to={`/t/${encodeURIComponent(tenantId)}/roles`}
              className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-border bg-surface px-3 text-sm font-semibold transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <KeyRound aria-hidden="true" className="size-4" />
              نقش‌ها
            </Link>
          </nav>
        )}

        {state.kind === 'loading' && !invalidSelection && <MembersSkeleton />}

        {invalidSelection ? (
          <InvalidSelection onRecover={selectPlatform} />
        ) : (
          <>
            {state.kind === 'loaded' && <MembersLoaded data={state.data} />}
            {state.kind === 'forbidden' && <ForbiddenTenant onRecover={selectPlatform} />}
            {state.kind === 'unavailable' && <Unavailable onRetry={retry} />}
          </>
        )}
      </section>
    </DashboardShell>
  )
}

function MembersLoaded({ data }: { data: TenantMembersResponse }) {
  const { tenant, members } = data
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-4 rounded-xl border border-primary/30 bg-primary/5 p-5 shadow-soft">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-primary/15 text-primary">
          <Building2 aria-hidden="true" className="size-6" />
        </span>
        <div className="min-w-0">
          <p className="text-xs font-semibold tracking-[0.08em] text-primary">
            محدوده مستأجر
          </p>
          <h2 className="mt-1 truncate text-xl font-semibold tracking-tight md:text-2xl">
            <bdi>{tenant.name}</bdi>
          </h2>
          <p className="mt-0.5 text-xs text-muted-foreground">
            <code dir="ltr" className="rounded bg-muted px-1.5 py-0.5">
              {tenant.slug}
            </code>
            <span className="mx-2" aria-hidden="true">·</span>
            {tenant.status === 'Active' ? 'فعال' : 'معلول'}
          </p>
        </div>
      </div>

      <div className="flex items-center gap-2 rounded-lg border border-border bg-surface p-3 text-xs text-muted-foreground shadow-soft">
        <ShieldCheck aria-hidden="true" className="size-4 shrink-0 text-primary" />
        دسترسی به این داده فقط با عضویت فعال شما در این مستأجر تأیید شده است؛ انتخاب محدوده هرگز دسترسی نمی‌بخشد.
      </div>

      <div className="overflow-x-auto rounded-xl border border-border bg-surface shadow-soft">
        <table className="w-full min-w-[40rem] text-sm">
          <caption className="sr-only">اعضای مستأجر {tenant.name}</caption>
          <thead>
            <tr className="border-b border-border text-start">
              <th scope="col" className="px-4 py-3 text-start font-semibold">نام</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">ایمیل</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">نقش</th>
              <th scope="col" className="px-4 py-3 text-start font-semibold">پیوسته در</th>
            </tr>
          </thead>
          <tbody>
            {members.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-muted-foreground">
                  عضوی برای این مستأجر ثبت نشده است.
                </td>
              </tr>
            ) : (
              members.map((member) => <MemberRow key={member.id} member={member} />)
            )}
          </tbody>
        </table>
      </div>

      <p className="flex items-center gap-2 text-xs text-muted-foreground">
        <Users aria-hidden="true" className="size-3.5" />
        <bdi>{members.length}</bdi> عضو
      </p>
    </div>
  )
}

function MemberRow({ member }: { member: TenantMember }) {
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3">
        <bdi className="font-medium">{member.displayName}</bdi>
      </td>
      <td className="px-4 py-3">
        <bdi className="text-muted-foreground" dir="ltr">
          {member.email}
        </bdi>
      </td>
      <td className="px-4 py-3">
        <MemberRoleBadge role={member.role} />
      </td>
      <td className="px-4 py-3 text-muted-foreground">
        <time dateTime={member.createdAtUtc}>{formatCreated(member.createdAtUtc)}</time>
      </td>
    </tr>
  )
}

function MemberRoleBadge({ role }: { role: TenantMember['role'] }) {
  const isOwner = role === 'Owner'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        isOwner ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
      )}
    >
      <span
        aria-hidden="true"
        className={cn('size-1.5 rounded-full', isOwner ? 'bg-primary' : 'bg-muted-foreground')}
      />
      {isOwner ? 'مالک' : role}
    </span>
  )
}

function ForbiddenTenant({ onRecover }: { onRecover: () => void }) {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <Lock aria-hidden="true" className="size-6" />
        </span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">دسترسی به این مستأجر مجاز نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            حساب فعلی عضویت فعال در این مستأجر ندارد. دسترسی به دادهٔ اعضا فقط توسط سرور و بر
            اساس عضویت شما کنترل می‌شود؛ تغییر آدرس مرورگر این محدودیت را دور نمی‌زند.
          </p>
          <SecondaryButton type="button" className="mt-4" onClick={onRecover}>
            بازگشت به فهرست مستأجران
          </SecondaryButton>
        </div>
      </div>
    </div>
  )
}

function InvalidSelection({ onRecover }: { onRecover: () => void }) {
  return (
    <div className="rounded-xl border border-border bg-surface p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
          <Building2 aria-hidden="true" className="size-6" />
        </span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">مستأجر یافت نشد</p>
          <p className="text-sm leading-6 text-muted-foreground">
            این محدوده در فهرست مستأجران شما موجود نیست.
          </p>
          <SecondaryButton type="button" className="mt-4" onClick={onRecover}>
            بازگشت به پلتفرم
          </SecondaryButton>
        </div>
      </div>
    </div>
  )
}

function Unavailable({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <RefreshCw aria-hidden="true" className="size-6" />
        </span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">اعضای مستأجر در دسترس نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            هم‌اکنون نمی‌توانیم دادهٔ این محدوده را بارگذاری کنیم. اتصال را بررسی کنید و دوباره
            تلاش کنید.
          </p>
          <SecondaryButton type="button" className="mt-4" onClick={onRetry}>
            <RefreshCw aria-hidden="true" className="me-2 size-4" />
            تلاش دوباره
          </SecondaryButton>
        </div>
      </div>
    </div>
  )
}

/** Loading placeholder matching the member table's footprint to avoid layout shift. */
function MembersSkeleton() {
  return (
    <div className="space-y-4">
      <div
        className="flex items-center gap-3 rounded-xl border border-border bg-surface p-6 shadow-soft"
        aria-busy="true"
      >
        <Loader2
          aria-hidden="true"
          className="size-5 animate-spin text-muted-foreground motion-reduce:animate-none"
        />
        <p className="text-sm font-medium text-muted-foreground">
          در حال بارگذاری محدوده مستأجر و اعضا…
        </p>
      </div>
      <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
        <div className="border-b border-border px-4 py-3">
          <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
        {[0, 1, 2].map((index) => (
          <div
            key={index}
            className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0"
          >
            <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
            <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
            <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          </div>
        ))}
      </div>
    </div>
  )
}

/** Persian (fa-IR) rendering of the UTC `createdAtUtc` value. */
function formatCreated(isoUtc: string) {
  const date = new Date(isoUtc)
  if (Number.isNaN(date.getTime())) return isoUtc
  return new Intl.DateTimeFormat('fa-IR', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
