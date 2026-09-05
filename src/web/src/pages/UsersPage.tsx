import { CircleCheck, Loader2, RefreshCw, ShieldCheck, TriangleAlert, UserPlus, Users } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import type { FormEvent } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { mockUserAdapter } from '@/features/users/userAdapter'
import {
  UserConflictError,
  type PlatformUser,
  type UserStatus,
} from '@/features/users/userTypes'
import { cn } from '@/lib/utils'

/**
 * S06 user management (F008: contract + mock).
 *
 * The platform administrator sees platform-scoped accounts and creates a
 * basic active user through a validated form. Data comes from
 * `mockUserAdapter` (task-approved mock); F009 swaps in the real
 * `GET/POST /api/platform/users` behind the same seam.
 *
 * States:
 * - list: initial skeleton, loaded table, empty panel, retryable error;
 * - create: idle, invalid (field errors), submitting, success, and a
 *   duplicate-email conflict on the email field.
 */

const createUserSchema = z.object({
  email: z
    .string()
    .min(1, 'ایمیل الزامی است.')
    .email('یک ایمیل معتبر وارد کنید.'),
  displayName: z
    .string()
    .min(1, 'نام نمایشی الزامی است.')
    .max(80, 'نام نمایشی نباید بیشتر از ۸۰ نویسه باشد.'),
  password: z
    .string()
    .min(8, 'رمز عبور باید حداقل ۸ نویسه باشد.')
    .max(128, 'رمز عبور نباید بیشتر از ۱۲۸ نویسه باشد.'),
})

type CreateUserFormValues = z.infer<typeof createUserSchema>

export function UsersPage() {
  const [users, setUsers] = useState<PlatformUser[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  // Monotonic guard: a superseded list fetch never writes.
  const listRequestIdRef = useRef(0)

  const [formOpen, setFormOpen] = useState(false)
  const [isCreating, setIsCreating] = useState(false)
  const [createSuccess, setCreateSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const emailInputRef = useRef<HTMLInputElement | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm<CreateUserFormValues>({
    resolver: zodResolver(createUserSchema),
    defaultValues: { email: '', displayName: '', password: '' },
  })

  /**
   * Initial list fetch: the synchronous state is already correct at mount
   * (`isBusy` starts true), so this path does no synchronous `setState` —
   * the mount effect only subscribes to the adapter, its external system.
   */
  const startInitialFetch = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    return mockUserAdapter
      .listUsers()
      .then((response) => {
        if (requestId !== listRequestIdRef.current) return
        setUsers(response.items)
      })
      .catch(() => {
        if (requestId !== listRequestIdRef.current) return
        // Initial failure: users stays null, the error panel takes over.
        // Refetch failure after a success keeps the table on screen.
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [])

  /** Refresh/retry (event handlers): mark busy synchronously, then fetch. */
  const loadUsers = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    setIsBusy(true)
    return mockUserAdapter
      .listUsers()
      .then((response) => {
        if (requestId !== listRequestIdRef.current) return
        setUsers(response.items)
      })
      .catch(() => {
        if (requestId !== listRequestIdRef.current) return
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [])

  useEffect(() => {
    void startInitialFetch()
  }, [startInitialFetch])

  // Focus moves into the form when it opens (and back to the toggle on
  // close/cancel — see closeForm), done in an effect so the DOM has updated.
  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => emailInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const closeForm = useCallback(() => {
    setFormOpen(false)
    reset()
    clearErrors()
    setCreateSuccess(null)
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  const onSubmit = useCallback(
    async (values: CreateUserFormValues) => {
      setIsCreating(true)
      setCreateSuccess(null)
      try {
        const created = await mockUserAdapter.createUser(values)
        setCreateSuccess(`کاربر ${created.displayName} ایجاد شد.`)
        reset()
        // Background re-fetch: the new row appears without a skeleton flash.
        void loadUsers()
      } catch (error) {
        if (error instanceof UserConflictError) {
          // Duplicate email: a stable conflict on the email field.
          setError('email', { message: error.message })
          emailInputRef.current?.focus()
        }
      } finally {
        setIsCreating(false)
      }
    },
    [loadUsers, reset, setError],
  )
  const submitCreateUser = useCallback(
    (event: FormEvent<HTMLFormElement>) => {
      void handleSubmit(onSubmit)(event)
    },
    [handleSubmit, onSubmit],
  )

  const isLoading = users === null && isBusy
  const listError = users === null && !isBusy
  const isEmpty = users !== null && users.length === 0
  const emailField = register('email')

  return (
    <DashboardShell>
      <section aria-label="مدیریت کاربران" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت پلتفرم</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">
              کاربران
            </h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              حساب‌های سطح پلتفرم را مشاهده و یک کاربر فعال پایه بسازید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {users !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست کاربران"
                className="px-3"
                disabled={isBusy}
                onClick={() => void loadUsers()}
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
              aria-controls="create-user-form"
              disabled={isBusy}
              ref={toggleButtonRef}
              onClick={() => {
                setFormOpen((open) => !open)
              }}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <UserPlus aria-hidden="true" className="me-2 size-4" />
                  ایجاد کاربر
                </>
              )}
            </button>
          </div>
        </div>

        {formOpen && (
          <form
            id="create-user-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={submitCreateUser}
            noValidate
          >
            <h3 className="text-base font-semibold">ایجاد کاربر جدید</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              کاربر با وضعیت «فعال» و بدون دسترسی ادمین ایجاد می‌شود.
            </p>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="user-email">
                  ایمیل
                </label>
                <TextInput
                  id="user-email"
                  type="email"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.email)}
                  aria-describedby={errors.email ? 'user-email-error' : undefined}
                  {...emailField}
                  ref={(node) => {
                    emailField.ref(node)
                    emailInputRef.current = node
                  }}
                />
                {errors.email && (
                  <p className="mt-2 text-sm text-destructive" id="user-email-error">
                    {errors.email.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="user-display-name">
                  نام نمایشی
                </label>
                <TextInput
                  id="user-display-name"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.displayName)}
                  aria-describedby={errors.displayName ? 'user-display-name-error' : undefined}
                  {...register('displayName')}
                />
                {errors.displayName && (
                  <p className="mt-2 text-sm text-destructive" id="user-display-name-error">
                    {errors.displayName.message}
                  </p>
                )}
              </div>
              <div className="md:col-span-2">
                <label className="mb-2 block text-sm font-semibold" htmlFor="user-password">
                  رمز عبور اولیه
                </label>
                <TextInput
                  id="user-password"
                  type="password"
                  autoComplete="new-password"
                  aria-invalid={Boolean(errors.password)}
                  aria-describedby={errors.password ? 'user-password-error' : 'user-password-hint'}
                  {...register('password')}
                />
                {errors.password ? (
                  <p className="mt-2 text-sm text-destructive" id="user-password-error">
                    {errors.password.message}
                  </p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="user-password-hint">
                    حداقل ۸ نویسه؛ یک‌بار مصرف و هرگز در پاسخ‌ها نمایش داده نمی‌شود.
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
                  'ایجاد کاربر'
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

        {isLoading && <UsersSkeleton />}

        {listError && (
          <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-5" role="alert">
            <div className="flex items-start gap-3">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="text-sm font-semibold">فهرست کاربران در دسترس نیست</p>
                <p className="text-sm leading-6 text-muted-foreground">
                  هم‌اکنون نمی‌توانیم کاربران را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.
                </p>
                <Button type="button" className="mt-3 min-w-32" onClick={() => void loadUsers()}>
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
              <Users aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">کاربری موجود نیست</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین حساب پلتفرم را با دکمه «ایجاد کاربر» بسازید.
            </p>
          </div>
        )}

        {users !== null && users.length > 0 && (
          <div className="overflow-x-auto rounded-xl border border-border bg-surface shadow-soft">
            <table className="w-full min-w-[40rem] text-sm">
              <caption className="sr-only">فهرست حساب‌های پلتفرم</caption>
              <thead>
                <tr className="border-b border-border text-start">
                  <th scope="col" className="px-4 py-3 text-start font-semibold">ایمیل</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">نام نمایشی</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">نقش</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">ساخته‌شده در</th>
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <UserRow key={user.id} user={user} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function UserRow({ user }: { user: PlatformUser }) {
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3">
        <bdi className="font-medium">{user.email}</bdi>
      </td>
      <td className="px-4 py-3">
        <bdi>{user.displayName}</bdi>
      </td>
      <td className="px-4 py-3">
        <StatusBadge status={user.status} />
      </td>
      <td className="px-4 py-3">
        {user.isPlatformAdmin ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-primary/10 px-2.5 py-0.5 text-xs font-semibold text-primary">
            <ShieldCheck aria-hidden="true" className="size-3.5" />
            ادمین پلتفرم
          </span>
        ) : (
          <span className="text-xs text-muted-foreground">کاربر</span>
        )}
      </td>
      <td className="px-4 py-3 text-muted-foreground">
        <time dateTime={user.createdAtUtc}>{formatCreated(user.createdAtUtc)}</time>
      </td>
    </tr>
  )
}

function StatusBadge({ status }: { status: UserStatus }) {
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
      {active ? 'فعال' : 'غیرفعال'}
    </span>
  )
}

/**
 * Loading placeholder with the table's footprint (header + rows) so the
 * transition to data causes no layout shift.
 */
function UsersSkeleton() {
  return (
    <div aria-busy="true" className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
      <p className="sr-only">
        در حال بارگذاری فهرست کاربران
        <span aria-hidden="true">…</span>
      </p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
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
