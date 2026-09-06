import {
  CircleCheck,
  Clock,
  FlaskConical,
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
import {
  httpInvitationAdapter,
  type InvitationActor,
} from '@/features/invitations/invitationsAdapter'
import {
  InvitationConflictError,
  InvitationForbiddenError,
  InvitationValidationError,
  type Invitation,
  type InvitationRole,
} from '@/features/invitations/invitationsTypes'
import { cn } from '@/lib/utils'

/**
 * S10 tenant invitations — mocked page (F015).
 *
 * A tenant Owner invites a person by email with a role, sees the pending
 * invitation and its expiry, and (development mode only) a clearly-labeled
 * stand-in for the emailed acceptance link. The data source is F015's
 * sessionStorage mock, which freezes the B010 contract; F016 swaps it for the
 * real endpoints without changing this page.
 *
 * States:
 * - loading: initial/tenant-change request in flight (skeleton);
 * - loaded: invite form + pending list (which may be empty);
 * - forbidden: a member without invitation permission — designed 403, modeled
 *   by `?invitationsViewer=member`;
 * - unavailable: network/server failure — retryable.
 *
 * Form states: idle, field validation (email/role), submitting, success (with
 * dev-only acceptance link), and 409 duplicate conflict. Authorization remains
 * server-owned; the mock models the 403 the same way F013 did.
 */

type InvitationsState =
  | { kind: 'loading' }
  | { kind: 'loaded'; invitations: Invitation[] }
  | { kind: 'forbidden' }
  | { kind: 'unavailable' }

/** The two built-in roles that exist by the end of S09 (F013's seeded set). */
const INVITE_ROLES: { value: InvitationRole; label: string }[] = [
  { value: 'Owner', label: 'مالک' },
  { value: 'Viewer', label: 'مشاهده‌گر' },
]

const ROLE_LABELS: Record<InvitationRole, string> = {
  Owner: 'مالک',
  Viewer: 'مشاهده‌گر',
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

/**
 * A development-only stand-in for the emailed acceptance link. The real
 * one-time token is generated and stored server-side (B010) and is deliberately
 * absent from the contract; this labeled, non-HTTP sample exists only so the
 * milestone's "we would show a link" behavior is visible, and it is gated so it
 * never renders in a production build.
 */
function DevelopmentAcceptanceLink({ invitationId }: { invitationId: string }) {
  if (!import.meta.env.DEV) return null
  return (
    <div className="mt-3 rounded-lg border border-warning/40 bg-warning/10 p-3">
      <p className="flex items-center gap-2 text-xs font-semibold text-warning">
        <FlaskConical aria-hidden="true" className="size-4" />
        حالت توسعه — ارسال ایمیل واقعی در این مایل‌ستون انجام نمی‌شود
      </p>
      <p className="mt-1 text-xs leading-5 text-muted-foreground">
        در نسخهٔ نهایی، لینک پذیرش یک‌بارمصرف از طریق ایمیل ارسال می‌شود. این نمونه فقط رفتار نمایش
        لینک را نشان می‌دهد:
      </p>
      <code
        dir="ltr"
        className="mt-2 block truncate rounded bg-background/60 px-2 py-1 text-[11px] text-foreground"
      >
        acceptance://invite/{invitationId}
      </code>
    </div>
  )
}

export function InvitationsPage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const { session, signOut } = useAuth()
  const [state, setState] = useState<InvitationsState>({ kind: 'loading' })
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [created, setCreated] = useState<Invitation | null>(null)
  const [conflictError, setConflictError] = useState<string | null>(null)

  const requestIdRef = useRef(0)
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

  const onSubmit = useCallback(
    async (values: InviteFormValues) => {
      if (!tenantId || !session?.user) return
      setIsSubmitting(true)
      setConflictError(null)
      setCreated(null)
      const actor: InvitationActor = {
        displayName: session.user.displayName,
        email: session.user.email,
      }
      try {
        const invitation = await httpInvitationAdapter.createInvitation(
          sessionRef.current?.accessToken ?? '',
          tenantId,
          { email: values.email, role: values.role as InvitationRole },
          actor,
        )
        setState((current) =>
          current.kind === 'loaded'
            ? { kind: 'loaded', invitations: [invitation, ...current.invitations] }
            : current,
        )
        reset({ email: '', role: '' })
        setCreated(invitation)
      } catch (error) {
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
        setIsSubmitting(false)
      }
    },
    [reset, session?.user, setError, tenantId],
  )

  const submitInvite = useCallback((event: FormEvent<HTMLFormElement>) => {
    void handleSubmit(onSubmit)(event)
  }, [handleSubmit, onSubmit])

  return (
    <DashboardShell>
      <section aria-label="دعوت‌های مستأجر" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold text-primary">مدیریت مستأجر</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">دعوت‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              یک نفر را با ایمیل و نقش مشخص به این مستأجر دعوت کنید؛ دعوت‌های در انتظار و تاریخ
              انقضای آن‌ها همین‌جا دیده می‌شود.
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
            <form
              className="space-y-5 rounded-xl border border-border bg-surface p-5 shadow-soft"
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
                  aria-invalid={Boolean(errors.role)}
                  aria-describedby={errors.role ? 'invite-role-error' : 'invite-role-hint'}
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
                  <option value="">انتخاب نقش…</option>
                  {INVITE_ROLES.map((role) => (
                    <option key={role.value} value={role.value}>{role.label}</option>
                  ))}
                </select>
                {errors.role ? (
                  <p className="mt-2 text-sm text-destructive" id="invite-role-error">{errors.role.message}</p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="invite-role-hint">
                    نقشی که عضو پس از پذیرش دعوت دریافت می‌کند.
                  </p>
                )}
              </div>

              <div className="border-t border-border pt-4">
                <Button type="submit" disabled={isSubmitting} className="w-full sm:w-auto">
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

            <PendingInvitations invitations={state.invitations} created={created} />
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function PendingInvitations({ invitations, created }: { invitations: Invitation[]; created: Invitation | null }) {
  return (
    <section className="rounded-xl border border-border bg-surface p-5 shadow-soft" aria-label="دعوت‌های در انتظار">
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
            دعوت برای <bdi dir="ltr">{created.email}</bdi> با نقش <bdi>{ROLE_LABELS[created.role]}</bdi> ایجاد شد.
          </p>
          <DevelopmentAcceptanceLink invitationId={created.id} />
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

function RoleBadge({ role }: { role: InvitationRole }) {
  const isOwner = role === 'Owner'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        isOwner ? 'bg-primary/10 text-primary' : 'bg-muted text-muted-foreground',
      )}
    >
      <span aria-hidden="true" className={cn('size-1.5 rounded-full', isOwner ? 'bg-primary' : 'bg-muted-foreground')} />
      {isOwner ? 'مالک' : 'مشاهده‌گر'}
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
