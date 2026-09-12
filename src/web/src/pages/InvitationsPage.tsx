import {
  CircleCheck,
  Clock,
  Loader2,
  Lock,
  MailPlus,
  RefreshCw,
  TriangleAlert,
} from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import type { FormEvent } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { httpInvitationAdapter } from '@/features/invitations/invitationsAdapter'
import {
  InvitationConflictError,
  InvitationForbiddenError,
  InvitationValidationError,
  type BuiltInInvitationRole,
  type Invitation,
  type InvitationRole,
} from '@/features/invitations/invitationsTypes'
import { httpRoleAdapter } from '@/features/roles/roleAdapter'
import type { TenantRole } from '@/features/roles/roleTypes'
import { useTenantPermissions } from '@/features/roles/tenantPermissions'
import { cn } from '@/lib/utils'

/**
 * S10 tenant invitations — connected to the real B010 API (F016).
 *
 * A tenant Owner invites a person by email with a built-in or tenant custom
 * role, sees the pending invitation and its expiry, and gets an honest local
 * confirmation that does not imply email delivery or acceptance. F015's
 * sessionStorage mock is gone; this page now calls the real invitation
 * endpoints, but keeps every state F015 established.
 *
 * States:
 * - loading: initial/tenant-change request in flight (skeleton);
 * - loaded: invite form + pending list (which may be empty);
 * - forbidden: a caller without `IAM.Invitations.View` — B010
 *   returns a non-leaking 403;
 * - unavailable: network/server failure — retryable.
 *
 * S12 (F019): the create form follows the server-resolved
 * `IAM.Invitations.Create`. A viewer (`Invitations.View` without
 * `Invitations.Create`) sees the pending list plus an honest «فقط مشاهده»
 * notice — no dead form; B013 still denies a direct `POST` with 403.
 *
 * F020 (S13): `role` is a validated non-empty role-name string. The form offers
 * Owner/Viewer plus custom role names from this tenant's roles API only when the
 * caller can create invitations; historical unfamiliar role names remain
 * displayable in the list.
 *
 * Form states: idle, role-list loading/failure, field validation (email/role),
 * submitting, success without an acceptance link, and 409 duplicate conflict.
 * Authorization is server-owned; the audit event's actor is resolved by B010
 * from the bearer token, not supplied by this page.
 */

type InvitationsState =
  | { kind: 'loading' }
  | { kind: 'loaded'; invitations: Invitation[] }
  | { kind: 'forbidden' }
  | { kind: 'unavailable' }

type RoleChoicesState =
  | { kind: 'idle' }
  | { kind: 'loading' }
  | { kind: 'loaded'; choices: InvitationRoleChoice[] }
  | { kind: 'error' }

type InvitationRoleChoice = {
  value: InvitationRole
  label: string
  kind: 'builtIn' | 'custom'
}

/** The two built-in invitation roles preserved by the S13 backend contract. */
const BUILT_IN_INVITE_ROLES: InvitationRoleChoice[] = [
  { value: 'Owner', label: 'مالک', kind: 'builtIn' },
  { value: 'Viewer', label: 'مشاهده‌گر', kind: 'builtIn' },
]

const BUILT_IN_ROLE_LABELS: Record<BuiltInInvitationRole, string> = {
  Owner: 'مالک',
  Viewer: 'مشاهده‌گر',
}

function roleDisplayName(role: InvitationRole) {
  if (role === 'Owner' || role === 'Viewer') return BUILT_IN_ROLE_LABELS[role]
  return role
}

function buildRoleChoices(roles: TenantRole[]): InvitationRoleChoice[] {
  const seen = new Set(BUILT_IN_INVITE_ROLES.map((role) => role.value))
  const customChoices = roles
    .filter((role) => role.kind === 'custom')
    .flatMap((role) => {
      const name = role.name.trim()
      if (!name || seen.has(name)) return []
      seen.add(name)
      return [{ value: name, label: name, kind: 'custom' as const }]
    })
  return [...BUILT_IN_INVITE_ROLES, ...customChoices]
}

const inviteSchema = z.object({
  email: z
    .string()
    .min(1, 'ایمیل الزامی است.')
    .max(254, 'ایمیل نباید بیشتر از ۲۵۴ نویسه باشد.')
    .email('ایمیل معتبر وارد کنید.'),
  role: z.string().min(1, 'نقش را انتخاب کنید.'),
})

type InviteFormValues = z.infer<typeof inviteSchema>

export function InvitationsPage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const { session, signOut } = useAuth()
  // S12 (F019): the list follows `IAM.Invitations.View` (already enforced by
  // the route guard/navigation); the create form follows `IAM.Invitations.Create`.
  // A viewer sees the pending invitations and an honest notice instead of a
  // form the server would deny with 403 anyway.
  const permissions = useTenantPermissions(tenantId)
  const canCreateInvitations = permissions.canCreateInvitations
  const [state, setState] = useState<InvitationsState>({ kind: 'loading' })
  const [roleChoicesState, setRoleChoicesState] = useState<RoleChoicesState>({ kind: 'idle' })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [created, setCreated] = useState<Invitation | null>(null)
  const [conflictError, setConflictError] = useState<string | null>(null)

  const requestIdRef = useRef(0)
  const roleChoicesRequestIdRef = useRef(0)
  const submitRequestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)
  const emailInputRef = useRef<HTMLInputElement | null>(null)
  const roleSelectRef = useRef<HTMLSelectElement | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<InviteFormValues>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { email: '', role: '' },
  })

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  useEffect(() => {
    submitRequestIdRef.current += 1
    reset({ email: '', role: '' })
    setIsSubmitting(false)
    setConflictError(null)
    setCreated(null)
  }, [reset, tenantId])

  const loadInvitations = useCallback(() => {
    if (!tenantId) return
    const requestId = ++requestIdRef.current
    setState({ kind: 'loading' })
    setConflictError(null)
    setCreated(null)

    httpInvitationAdapter
      .listInvitations(sessionRef.current?.accessToken ?? '', tenantId)
      .then((response) => {
        if (requestId !== requestIdRef.current) return
        setState({ kind: 'loaded', invitations: response.invitations })
      })
      .catch((error) => {
        if (requestId !== requestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        if (error instanceof InvitationForbiddenError) {
          setState({ kind: 'forbidden' })
          return
        }
        setState({ kind: 'unavailable' })
      })
  }, [tenantId])

  useEffect(() => {
    loadInvitations()
  }, [loadInvitations])

  const loadRoleChoices = useCallback(() => {
    if (!tenantId || !permissions.canCreateInvitations) {
      roleChoicesRequestIdRef.current += 1
      setRoleChoicesState({ kind: 'idle' })
      return
    }
    const requestId = ++roleChoicesRequestIdRef.current
    setRoleChoicesState({ kind: 'loading' })
    httpRoleAdapter
      .listRoles(sessionRef.current?.accessToken ?? '', tenantId)
      .then((response) => {
        if (requestId !== roleChoicesRequestIdRef.current) return
        setRoleChoicesState({ kind: 'loaded', choices: buildRoleChoices(response.roles) })
      })
      .catch((error) => {
        if (requestId !== roleChoicesRequestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setRoleChoicesState({ kind: 'error' })
      })
  }, [permissions.canCreateInvitations, tenantId])

  useEffect(() => {
    loadRoleChoices()
  }, [loadRoleChoices])

  const onSubmit = useCallback(
    async (values: InviteFormValues) => {
      if (!tenantId || roleChoicesState.kind !== 'loaded') return
      const requestId = ++submitRequestIdRef.current
      setIsSubmitting(true)
      setConflictError(null)
      setCreated(null)
      try {
        const invitation = await httpInvitationAdapter.createInvitation(
          sessionRef.current?.accessToken ?? '',
          tenantId,
          { email: values.email, role: values.role },
        )
        if (requestId !== submitRequestIdRef.current) return
        setState((current) =>
          current.kind === 'loaded'
            ? { kind: 'loaded', invitations: [invitation, ...current.invitations] }
            : current,
        )
        reset({ email: '', role: '' })
        setCreated(invitation)
      } catch (error) {
        if (requestId !== submitRequestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof InvitationConflictError) {
          setConflictError(error.message)
        } else if (error instanceof InvitationValidationError) {
          let firstField: keyof InviteFormValues | null = null
          for (const [field, message] of Object.entries(error.fieldErrors)) {
            setError(field as keyof InviteFormValues, { message })
            firstField ??= field as keyof InviteFormValues
          }
          // Move focus to the first problem field so it is not lost.
          if (firstField === 'email') emailInputRef.current?.focus()
          else if (firstField === 'role') roleSelectRef.current?.focus()
        } else if (error instanceof InvitationForbiddenError || error instanceof ApiUnavailableError) {
          setConflictError(error.message)
        }
      } finally {
        if (requestId === submitRequestIdRef.current) setIsSubmitting(false)
      }
    },
    [reset, roleChoicesState.kind, setError, tenantId],
  )

  const submitInvite = useCallback((event: FormEvent<HTMLFormElement>) => {
    void handleSubmit(onSubmit)(event)
  }, [handleSubmit, onSubmit])

  const roleChoicesLoaded = roleChoicesState.kind === 'loaded'
  const roleChoices = roleChoicesLoaded ? roleChoicesState.choices : []
  const roleChoicesUnavailable = roleChoicesState.kind === 'error'
  const canSubmit = roleChoicesLoaded && !isSubmitting

  return (
    <DashboardShell>
      <section aria-label="دعوت‌های مستأجر" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold text-primary">مدیریت مستأجر</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">دعوت‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              {canCreateInvitations
                ? 'یک نفر را با ایمیل و نقش مشخص به این مستأجر دعوت کنید؛ دعوت‌های در انتظار و تاریخ انقضای آن‌ها همین‌جا دیده می‌شود.'
                : 'دعوت‌های در انتظار این مستأجر و تاریخ انقضای آن‌ها همین‌جا دیده می‌شود.'}
            </p>
          </div>
          {state.kind === 'loaded' && (
            <div className="flex shrink-0 items-center gap-2">
              <SecondaryButton type="button" className="px-3" onClick={loadInvitations}>
                <RefreshCw aria-hidden="true" className="size-4" />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            </div>
          )}
        </div>

        {state.kind === 'loading' && <InvitationsSkeleton />}
        {state.kind === 'forbidden' && <ForbiddenInvitations />}
        {state.kind === 'unavailable' && <UnavailableInvitations onRetry={loadInvitations} />}

        {state.kind === 'loaded' && (
          <div className="grid gap-6 xl:grid-cols-[minmax(18rem,22rem)_1fr]">
            {permissions.isResolving ? (
              <CreateFormSkeleton />
            ) : !canCreateInvitations ? (
              <CreateRestrictedNotice />
            ) : (
              <form
                className="min-w-0 space-y-5 rounded-xl border border-border bg-surface p-5 shadow-soft"
                onSubmit={submitInvite}
                noValidate
                aria-label="فرم دعوت"
              >
              <div>
                <h3 className="text-base font-semibold">دعوت عضو جدید</h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  ایمیل و نقش را انتخاب کنید. هر ایمیل تا وقتی یک دعوت فعال دارد، فقط یک دعوت فعال
                  می‌تواند داشته باشد.
                </p>
              </div>

              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="invite-email">ایمیل</label>
                <TextInput
                  id="invite-email"
                  type="email"
                  dir="ltr"
                  autoComplete="off"
                  placeholder="teammate@company.com"
                  aria-invalid={Boolean(errors.email)}
                  aria-describedby={errors.email ? 'invite-email-error' : 'invite-email-hint'}
                  {...register('email')}
                  ref={(node) => {
                    register('email').ref(node)
                    emailInputRef.current = node
                  }}
                />
                {errors.email ? (
                  <p className="mt-2 text-sm text-destructive" id="invite-email-error">{errors.email.message}</p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="invite-email-hint">
                    ایمیل واقعی دریافت‌کنندهٔ دعوت.
                  </p>
                )}
              </div>

              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="invite-role">نقش</label>
                <select
                  id="invite-role"
                  disabled={!roleChoicesLoaded || isSubmitting}
                  aria-invalid={Boolean(errors.role)}
                  aria-describedby={
                    errors.role
                      ? 'invite-role-error'
                      : roleChoicesUnavailable
                        ? 'invite-role-load-error'
                        : 'invite-role-hint'
                  }
                  className={cn(
                    'min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm',
                    errors.role && 'border-destructive',
                  )}
                  {...register('role')}
                  ref={(node) => {
                    register('role').ref(node)
                    roleSelectRef.current = node
                  }}
                >
                  <option value="">
                    {roleChoicesState.kind === 'loading' ? 'در حال بارگذاری نقش‌ها…' : 'انتخاب نقش…'}
                  </option>
                  {roleChoices.map((role) => (
                    <option key={`${role.kind}-${role.value}`} value={role.value}>
                      {role.kind === 'custom' ? role.label : `${role.label} (پیش‌فرض)`}
                    </option>
                  ))}
                </select>
                {errors.role ? (
                  <p className="mt-2 text-sm text-destructive" id="invite-role-error">{errors.role.message}</p>
                ) : roleChoicesUnavailable ? (
                  <div className="mt-2 rounded-lg border border-destructive/40 bg-destructive/10 p-3" id="invite-role-load-error" role="alert">
                    <p className="text-sm text-destructive">نقش‌های این مستأجر بارگذاری نشد؛ ایجاد دعوت تا بارگذاری موفق غیرفعال است.</p>
                    <SecondaryButton type="button" className="mt-3 px-3" onClick={loadRoleChoices}>
                      <RefreshCw aria-hidden="true" className="size-4" />
                      تلاش دوباره
                    </SecondaryButton>
                  </div>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="invite-role-hint">
                    {roleChoicesState.kind === 'loading'
                      ? 'برای جلوگیری از نقش‌های قدیمی یا متعلق به مستأجر دیگر، ابتدا فهرست نقش‌های همین مستأجر بارگذاری می‌شود.'
                      : 'نقش داخلی یا سفارشی همین مستأجر که عضو پس از پذیرش دعوت دریافت می‌کند.'}
                  </p>
                )}
              </div>

              <div className="border-t border-border pt-4">
                <Button type="submit" disabled={!canSubmit} className="w-full sm:w-auto">
                  {isSubmitting ? (
                    <><Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />در حال ایجاد دعوت</>
                  ) : (
                    <><MailPlus aria-hidden="true" className="me-2 size-4" />ایجاد دعوت</>
                  )}
                </Button>
              </div>

              {conflictError && (
                <p className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
                  {conflictError}
                </p>
              )}
              </form>
            )}

            <PendingInvitations invitations={state.invitations} created={created} />
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function PendingInvitations({ invitations, created }: { invitations: Invitation[]; created: Invitation | null }) {
  return (
    <section className="min-w-0 rounded-xl border border-border bg-surface p-5 shadow-soft" aria-label="دعوت‌های در انتظار">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-base font-semibold">دعوت‌های در انتظار</h3>
        <span className="rounded-full bg-muted px-2.5 py-0.5 text-xs text-muted-foreground">
          <bdi>{invitations.length}</bdi>
        </span>
      </div>

      {created && (
        <div className="mt-4 rounded-lg border border-success/40 bg-success/10 p-3" role="status">
          <p className="flex items-center gap-2 text-sm font-medium text-success">
            <CircleCheck aria-hidden="true" className="size-4" />
            دعوت برای <bdi dir="ltr">{created.email}</bdi> با نقش <bdi>{roleDisplayName(created.role)}</bdi> ایجاد شد.
          </p>
          <p className="mt-2 text-xs leading-5 text-muted-foreground">
            این وضعیت فقط ایجاد دعوت را تأیید می‌کند؛ ارسال ایمیل و پذیرش دعوت در این نسخه ارائه نشده است.
          </p>
        </div>
      )}

      {invitations.length === 0 ? (
        <div className="mt-4 flex flex-col items-center gap-2 rounded-lg border border-dashed border-border px-4 py-10 text-center">
          <MailPlus aria-hidden="true" className="size-6 text-muted-foreground" />
          <p className="text-sm font-medium">هنوز دعوتی در انتظار نیست</p>
          <p className="max-w-sm text-xs leading-5 text-muted-foreground">
            اولین دعوت را با فرم کنار این جدول ایجاد کنید؛ اینجا نمایش داده می‌شود.
          </p>
        </div>
      ) : (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full min-w-[38rem] text-sm">
            <caption className="sr-only">دعوت‌های در انتظار این مستأجر</caption>
            <thead>
              <tr className="border-b border-border">
                <th scope="col" className="px-3 py-3 text-start font-semibold">ایمیل</th>
                <th scope="col" className="px-3 py-3 text-start font-semibold">نقش</th>
                <th scope="col" className="px-3 py-3 text-start font-semibold">وضعیت</th>
                <th scope="col" className="px-3 py-3 text-start font-semibold">انقضا</th>
              </tr>
            </thead>
            <tbody>
              {invitations.map((invitation) => (
                <tr key={invitation.id} className="border-b border-border last:border-b-0">
                  <th scope="row" className="px-3 py-3 text-start font-medium">
                    <bdi dir="ltr">{invitation.email}</bdi>
                  </th>
                  <td className="px-3 py-3">
                    <RoleBadge role={invitation.role} />
                  </td>
                  <td className="px-3 py-3">
                    <span className="inline-flex items-center gap-1.5 rounded-full bg-warning/10 px-2.5 py-0.5 text-xs font-semibold text-warning">
                      <span aria-hidden="true" className="size-1.5 rounded-full bg-warning" />
                      در انتظار
                    </span>
                  </td>
                  <td className="px-3 py-3 text-muted-foreground">
                    <time dateTime={invitation.expiresAtUtc} className="inline-flex items-center gap-1.5">
                      <Clock aria-hidden="true" className="size-3.5" />
                      {formatDate(invitation.expiresAtUtc)}
                    </time>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}

function CreateFormSkeleton() {
  return (
    <div className="space-y-5 rounded-xl border border-border bg-surface p-5 shadow-soft" aria-busy="true">
      <p className="sr-only">در حال بررسی مجوزهای شما برای ایجاد دعوت…</p>
      <div className="h-6 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      <div className="h-11 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
      <div className="h-11 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
      <div className="h-10 w-32 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
    </div>
  )
}

/**
 * S12 (F019): the caller resolves `IAM.Invitations.View` but not
 * `IAM.Invitations.Create` — the honest counterpart of the hidden form. The
 * server still denies a direct `POST` with 403; this notice only keeps the
 * page consistent with the resolved permission set.
 */
function CreateRestrictedNotice() {
  return (
    <div className="rounded-xl border border-border bg-surface p-5 shadow-soft" role="status">
      <div className="flex items-start gap-3">
        <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
          <Lock aria-hidden="true" className="size-5" />
        </span>
        <div className="space-y-1.5">
          <h3 className="text-base font-semibold">فقط مشاهده</h3>
          <p className="text-sm leading-6 text-muted-foreground">
            حساب فعلی مجوز «ایجاد دعوت» را در این مستأجر ندارد؛ فقط دعوت‌های در انتظار قابل
            مشاهده است. برای ایجاد دعوت، یک نقش دارای این مجوز را از صفحهٔ نقش‌ها دریافت کنید.
          </p>
        </div>
      </div>
    </div>
  )
}

function RoleBadge({ role }: { role: InvitationRole }) {
  const isOwner = role === 'Owner'
  const isBuiltIn = role === 'Owner' || role === 'Viewer'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        isOwner ? 'bg-primary/10 text-primary' : isBuiltIn ? 'bg-muted text-muted-foreground' : 'bg-success/10 text-success',
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'size-1.5 rounded-full',
          isOwner ? 'bg-primary' : isBuiltIn ? 'bg-muted-foreground' : 'bg-success',
        )}
      />
      <bdi>{roleDisplayName(role)}</bdi>
    </span>
  )
}

function ForbiddenInvitations() {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
          <Lock aria-hidden="true" className="size-6" />
        </span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">مدیریت دعوت‌ها مجاز نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            حساب فعلی مجوز دعوت در این مستأجر را ندارد. مخفی‌سازی کنترل‌های UI امنیت محسوب نمی‌شود؛
            B010 باید همین عملیات را سمت سرور با 403 رد کند.
          </p>
        </div>
      </div>
    </div>
  )
}

function UnavailableInvitations({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">دعوت‌ها در دسترس نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">
            هم‌اکنون نمی‌توانیم دعوت‌ها را بارگذاری کنیم.
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

function InvitationsSkeleton() {
  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(18rem,22rem)_1fr]" aria-busy="true">
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="sr-only">در حال بارگذاری فرم دعوت…</p>
        <div className="h-6 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        <div className="mt-5 h-11 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
        <div className="mt-3 h-11 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
        <div className="mt-5 h-10 w-32 animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
      </div>
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="sr-only">در حال بارگذاری دعوت‌های در انتظار…</p>
        <div className="h-6 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        <div className="mt-5 space-y-3">
          {[0, 1, 2].map((index) => (
            <div key={index} className="h-12 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />
          ))}
        </div>
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
