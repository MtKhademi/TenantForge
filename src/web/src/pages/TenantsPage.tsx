import { Building2, CircleCheck, Loader2, LogIn, RefreshCw, TriangleAlert } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import type { FormEvent } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { useAuth } from '@/features/auth/AuthContext'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import {
  mockTenantAdapter,
  TenantForbiddenError,
  TenantValidationError,
} from '@/features/tenants/tenantAdapter'
import { TenantConflictError, type TenantSummary } from '@/features/tenants/tenantTypes'
import { httpUserAdapter } from '@/features/users/userAdapter'
import { type PlatformUser } from '@/features/users/userTypes'
import { cn } from '@/lib/utils'

/**
 * S07 tenant membership — platform tenants page (F010, mocked).
 *
 * The platform administrator sees the (mocked) tenant list and creates a
 * tenant together with its first Owner. The tenant data source is the F010
 * mock (`sessionStorage`); the Owner dropdown is fed by the **real** B006
 * `GET /api/platform/users` so the owner assigned in the demo is a real
 * platform user. Selecting a tenant (row action or header switcher) only
 * navigates — it grants no access.
 *
 * States:
 * - list: initial skeleton, loaded table, empty panel, retryable error;
 * - create: idle, invalid (field errors), submitting, success, and a
 *   duplicate-slug conflict on the slug field.
 */

const createTenantSchema = z.object({
  name: z
    .string()
    .min(1, 'نام مستأجر الزامی است.')
    .max(80, 'نام مستأجر نباید بیشتر از ۸۰ نویسه باشد.'),
  slug: z
    .string()
    .min(3, 'شناسه باید حداقل ۳ نویسه باشد.')
    .max(50, 'شناسه نباید بیشتر از ۵۰ نویسه باشد.')
    .regex(
      /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i,
      'شناسه فقط شامل a-z، 0-9 و خط تیره باشد و با حرف یا رقم شروع و پایان یابد.',
    ),
  ownerUserId: z.string().min(1, 'مالک نخست مستأجر را انتخاب کنید.'),
})

type CreateTenantFormValues = z.infer<typeof createTenantSchema>

export function TenantsPage() {
  const { session, signOut } = useAuth()
  const { tenants, isBusy, failure, refresh, selectTenant } = useTenantScope()

  const [users, setUsers] = useState<PlatformUser[] | null>(null)
  const [usersBusy, setUsersBusy] = useState(true)
  const [usersFailure, setUsersFailure] = useState(false)

  const [formOpen, setFormOpen] = useState(false)
  const [isCreating, setIsCreating] = useState(false)
  const [createSuccess, setCreateSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const nameInputRef = useRef<HTMLInputElement | null>(null)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm<CreateTenantFormValues>({
    resolver: zodResolver(createTenantSchema),
    defaultValues: { name: '', slug: '', ownerUserId: '' },
  })

  /**
   * Initial fetch of the real platform users for the Owner dropdown (real
   * B006 data). Synchronous state is already correct at mount (`usersBusy`
   * starts true, `usersFailure` false), so this path does no synchronous
   * `setState` — the mount effect only subscribes to the adapter, its
   * external system.
   */
  const startInitialUsersFetch = useCallback(() => {
    return httpUserAdapter
      .listUsers(sessionRef.current?.accessToken ?? '')
      .then((response) => setUsers(response.users))
      .catch(() => setUsersFailure(true))
      .finally(() => setUsersBusy(false))
  }, [])

  useEffect(() => {
    void startInitialUsersFetch()
  }, [startInitialUsersFetch])

  const nameField = register('name')

  const closeForm = useCallback(() => {
    setFormOpen(false)
    reset()
    clearErrors()
    setCreateSuccess(null)
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  // Focus moves into the form when it opens.
  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => nameInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const onSubmit = useCallback(
    async (values: CreateTenantFormValues) => {
      setIsCreating(true)
      setCreateSuccess(null)
      try {
        const created = await mockTenantAdapter.createTenant(
          sessionRef.current?.accessToken ?? '',
          values,
        )
        setCreateSuccess(`مستأجر ${created.name} با مالک نخست ایجاد شد.`)
        reset()
        // Background refresh: the new row appears without a skeleton flash.
        void refresh()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof TenantConflictError) {
          setError('slug', { message: error.message })
        } else if (error instanceof TenantValidationError) {
          for (const [field, message] of Object.entries(error.fieldErrors)) {
            setError(field as keyof CreateTenantFormValues, { message })
          }
        } else if (error instanceof TenantForbiddenError || error instanceof ApiUnavailableError) {
          setError('name', { message: error.message })
        }
        nameInputRef.current?.focus()
      }       finally {
        setIsCreating(false)
      }
    },
    [refresh, reset, setError],
  )

  const submitCreateTenant = useCallback(
    (event: FormEvent<HTMLFormElement>) => {
      void handleSubmit(onSubmit)(event)
    },
    [handleSubmit, onSubmit],
  )

  const isLoading = tenants === null && isBusy
  const listError = tenants === null && !isBusy && failure !== null
  const isEmpty = tenants !== null && tenants.length === 0

  return (
    <DashboardShell>
      <section aria-label="مدیریت مستأجران" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت پلتفرم</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">مستأجران</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              مستأجر جدید بسازید و یک کاربر پلتفرم را به‌عنوان مالک نخست منصوب کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {tenants !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست مستأجران"
                className="px-3"
                disabled={isBusy}
                onClick={() => refresh()}
              >
                <RefreshCw
                  aria-hidden="true"
                  className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')}
                />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            )}
            <button
              type="button"
              className={cn(
                'inline-flex min-h-10 items-center justify-center rounded-md border border-border px-4 py-2 text-sm font-semibold transition-colors duration-150 ease-admin disabled:pointer-events-none disabled:opacity-60',
                formOpen
                  ? 'bg-primary text-primary-foreground hover:bg-primary'
                  : 'bg-surface text-foreground hover:bg-muted',
              )}
              aria-expanded={formOpen}
              aria-controls="create-tenant-form"
              disabled={isBusy}
              ref={toggleButtonRef}
              onClick={() => setFormOpen((open) => !open)}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <Building2 aria-hidden="true" className="me-2 size-4" />
                  ایجاد مستأجر
                </>
              )}
            </button>
          </div>
        </div>

        {formOpen && (
          <form
            id="create-tenant-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={submitCreateTenant}
            noValidate
          >
            <h3 className="text-base font-semibold">ایجاد مستأجر جدید</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              مستأجر و عضویت مالک نخست در یک‌جا و به‌طور اتمی ایجاد می‌شود.
            </p>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="tenant-name">
                  نام مستأجر
                </label>
                <TextInput
                  id="tenant-name"
                  autoComplete="off"
                  placeholder="Acme"
                  aria-invalid={Boolean(errors.name)}
                  aria-describedby={errors.name ? 'tenant-name-error' : 'tenant-name-hint'}
                  {...nameField}
                  ref={(node) => {
                    nameField.ref(node)
                    nameInputRef.current = node
                  }}
                />
                {errors.name ? (
                  <p className="mt-2 text-sm text-destructive" id="tenant-name-error">
                    {errors.name.message}
                  </p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="tenant-name-hint">
                    نام نمایشی مستأجر (مثلاً Acme).
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="tenant-slug">
                  شناسه (slug)
                </label>
                <TextInput
                  id="tenant-slug"
                  autoComplete="off"
                  dir="ltr"
                  placeholder="acme"
                  aria-invalid={Boolean(errors.slug)}
                  aria-describedby={errors.slug ? 'tenant-slug-error' : 'tenant-slug-hint'}
                  {...register('slug')}
                />
                {errors.slug ? (
                  <p className="mt-2 text-sm text-destructive" id="tenant-slug-error">
                    {errors.slug.message}
                  </p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="tenant-slug-hint">
                    یکتا؛ فقط a-z، 0-9 و خط تیره (مثلاً acme).
                  </p>
                )}
              </div>
              <div className="md:col-span-2">
                <label className="mb-2 block text-sm font-semibold" htmlFor="tenant-owner">
                  مالک نخست
                </label>
                <select
                  id="tenant-owner"
                  aria-invalid={Boolean(errors.ownerUserId)}
                  aria-describedby={errors.ownerUserId ? 'tenant-owner-error' : 'tenant-owner-hint'}
                  className={cn(
                    'min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm',
                    errors.ownerUserId && 'border-destructive',
                  )}
                  disabled={usersBusy || usersFailure || users === null}
                  {...register('ownerUserId')}
                >
                  <option value="">
                    {usersBusy ? 'در حال بارگذاری کاربران…' : usersFailure ? 'فهرست کاربران در دسترس نیست' : 'مالک را انتخاب کنید…'}
                  </option>
                  {(users ?? []).map((user) => (
                    <option key={user.id} value={user.id}>
                      {user.displayName} — {user.email}
                    </option>
                  ))}
                </select>
                {errors.ownerUserId ? (
                  <p className="mt-2 text-sm text-destructive" id="tenant-owner-error">
                    {errors.ownerUserId.message}
                  </p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="tenant-owner-hint">
                    کاربر پلتفرمی که مالک نخست این مستأجر می‌شود.
                  </p>
                )}
              </div>
            </div>

            <div className="mt-5 flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={isCreating}>
                {isCreating ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ایجاد
                  </>
                ) : (
                  'ایجاد مستأجر'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeForm} disabled={isCreating}>
                لغو
              </SecondaryButton>
              {createSuccess && (
                <p className="flex items-center gap-2 text-sm font-medium text-success" role="status">
                  <CircleCheck aria-hidden="true" className="size-4" />
                  {createSuccess}
                </p>
              )}
            </div>
          </form>
        )}

        {isLoading && <TenantsSkeleton />}

        {listError && (
          <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-5" role="alert">
            <div className="flex items-start gap-3">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="text-sm font-semibold">
                  {failure === 'forbidden'
                    ? 'دسترسی مدیریت مستأجران مجاز نیست'
                    : 'فهرست مستأجران در دسترس نیست'}
                </p>
                <p className="text-sm leading-6 text-muted-foreground">
                  {failure === 'forbidden'
                    ? 'حساب فعلی مجوز مدیریت مستأجران پلتفرم را ندارد.'
                    : 'هم‌اکنون نمی‌توانیم مستأجران را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.'}
                </p>
                <Button type="button" className="mt-3 min-w-32" onClick={() => refresh()}>
                  <RefreshCw aria-hidden="true" className="me-2 size-4" />
                  تلاش دوباره
                </Button>
              </div>
            </div>
          </div>
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <Building2 aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">مستأجری وجود ندارد</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین مستأجر را با دکمه «ایجاد مستأجر» بسازید.
            </p>
          </div>
        )}

        {tenants !== null && tenants.length > 0 && (
          <div className="overflow-x-auto rounded-xl border border-border bg-surface shadow-soft">
            <table className="w-full min-w-[46rem] text-sm">
              <caption className="sr-only">فهرست مستأجران پلتفرم</caption>
              <thead>
                <tr className="border-b border-border text-start">
                  <th scope="col" className="px-4 py-3 text-start font-semibold">نام</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">شناسه</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">اعضا</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">ساخته‌شده در</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                </tr>
              </thead>
              <tbody>
                {tenants.map((tenant) => (
                  <TenantRow key={tenant.id} tenant={tenant} onEnter={() => selectTenant(tenant.slug)} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function TenantRow({ tenant, onEnter }: { tenant: TenantSummary; onEnter: () => void }) {
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3">
        <bdi className="font-medium">{tenant.name}</bdi>
      </td>
      <td className="px-4 py-3">
        <code dir="ltr" className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground">
          {tenant.slug}
        </code>
      </td>
      <td className="px-4 py-3">
        <TenantStatusBadge status={tenant.status} />
      </td>
      <td className="px-4 py-3 text-muted-foreground">
        <bdi>{tenant.memberCount}</bdi>
      </td>
      <td className="px-4 py-3 text-muted-foreground">
        <time dateTime={tenant.createdAtUtc}>{formatCreated(tenant.createdAtUtc)}</time>
      </td>
      <td className="px-4 py-3">
        <button
          type="button"
          onClick={onEnter}
          className="inline-flex min-h-9 items-center gap-1.5 rounded-md border border-border bg-surface px-3 text-xs font-semibold text-foreground transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        >
          <LogIn aria-hidden="true" className="size-3.5" />
          ورود
        </button>
      </td>
    </tr>
  )
}

function TenantStatusBadge({ status }: { status: TenantSummary['status'] }) {
  const active = status === 'Active'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        active ? 'bg-success/10 text-success' : 'bg-muted text-muted-foreground',
      )}
    >
      <span
        aria-hidden="true"
        className={cn('size-1.5 rounded-full', active ? 'bg-success' : 'bg-muted-foreground')}
      />
      {active ? 'فعال' : 'معلول'}
    </span>
  )
}

/**
 * Loading placeholder with the table's footprint so the transition to data
 * causes no layout shift.
 */
function TenantsSkeleton() {
  return (
    <div aria-busy="true" className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
      <p className="sr-only">
        در حال بارگذاری فهرست مستأجران
        <span aria-hidden="true">…</span>
      </p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-10 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
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
