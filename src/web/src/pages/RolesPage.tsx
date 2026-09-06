import {
  CircleCheck,
  Loader2,
  Lock,
  RefreshCw,
  ShieldCheck,
  TriangleAlert,
  UserCheck,
} from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import type { FormEvent } from 'react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { PERMISSION_CATALOG } from '@/features/roles/permissionCatalog'
import { httpRoleAdapter } from '@/features/roles/roleAdapter'
import {
  TenantRoleConflictError,
  TenantRoleForbiddenError,
  TenantRoleInvariantError,
  TenantRoleValidationError,
  type PermissionKey,
  type TenantRole,
} from '@/features/roles/roleTypes'
import { httpTenantMembersAdapter, TenantAccessDeniedError } from '@/features/tenants/tenantMembersAdapter'
import type { TenantMember, TenantMembersResponse } from '@/features/tenants/tenantTypes'
import { cn } from '@/lib/utils'

/**
 * S09 permission matrix mock (F013).
 *
 * This page consumes the real B009 role endpoints: tenant Owners can create
 * custom roles, select grouped permissions and assign roles to tenant members.
 * Authorization remains server-owned; the UI renders B009's non-leaking 403
 * and 409 responses without treating hidden controls as a security boundary.
 */

type RolesState =
  | { kind: 'loading' }
  | { kind: 'loaded'; roles: TenantRole[]; members: TenantMember[]; tenant: TenantMembersResponse['tenant'] }
  | { kind: 'forbidden' }
  | { kind: 'unavailable' }

const DRAFT_ROLE_ID = '__new-role__'

const roleSchema = z.object({
  name: z.string().min(1, 'نام نقش الزامی است.').max(64, 'نام نقش نباید بیشتر از ۶۴ نویسه باشد.'),
  permissionKeys: z.array(z.custom<PermissionKey>()),
})

type RoleFormValues = z.infer<typeof roleSchema>


export function RolesPage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const { session, signOut } = useAuth()
  const [state, setState] = useState<RolesState>({ kind: 'loading' })
  const [selectedRoleId, setSelectedRoleId] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)
  const [isAssigning, setIsAssigning] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)
  const [assignmentError, setAssignmentError] = useState<string | null>(null)

  const requestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)
  const nameInputRef = useRef<HTMLInputElement | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    getValues,
    setError,
    formState: { errors, isDirty },
  } = useForm<RoleFormValues>({
    resolver: zodResolver(roleSchema),
    defaultValues: { name: '', permissionKeys: [] },
  })

  const selectedKeys = watch('permissionKeys') ?? []

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const loadRoles = useCallback(() => {
    if (!tenantId) return
    const requestId = ++requestIdRef.current
    setState({ kind: 'loading' })
    setAssignmentError(null)
    setSuccess(null)

    Promise.all([
      httpRoleAdapter.listRoles(sessionRef.current?.accessToken ?? '', tenantId),
      httpTenantMembersAdapter.getTenantMembers(sessionRef.current?.accessToken ?? '', tenantId),
    ])
      .then(([roleResponse, memberResponse]) => {
        if (requestId !== requestIdRef.current) return
        const roles = roleResponse.roles
        setState({ kind: 'loaded', roles, members: memberResponse.members, tenant: memberResponse.tenant })
        setSelectedRoleId((current) => current ?? roles.find((role) => role.kind === 'custom')?.id ?? roles[0]?.id ?? null)
      })
      .catch((error) => {
        if (requestId !== requestIdRef.current) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        if (error instanceof TenantRoleForbiddenError || error instanceof TenantAccessDeniedError) {
          setState({ kind: 'forbidden' })
          return
        }
        setState({ kind: 'unavailable' })
      })
  }, [tenantId])

  useEffect(() => {
    loadRoles()
  }, [loadRoles])

  const selectedRole = useMemo(() => {
    if (state.kind !== 'loaded' || selectedRoleId === DRAFT_ROLE_ID) return null
    return state.roles.find((role) => role.id === selectedRoleId) ?? state.roles[0] ?? null
  }, [selectedRoleId, state])

  useEffect(() => {
    if (!selectedRole) return
    reset({ name: selectedRole.kind === 'custom' ? selectedRole.name : '', permissionKeys: selectedRole.permissionKeys })
  }, [reset, selectedRole])

  useEffect(() => {
    if (selectedRole?.kind !== 'custom') return
    const frame = requestAnimationFrame(() => nameInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [selectedRole?.id, selectedRole?.kind])

  const replaceRoles = useCallback((roles: TenantRole[]) => {
    setState((current) => (current.kind === 'loaded' ? { ...current, roles } : current))
  }, [])

  const createNewDraft = useCallback(() => {
    setSelectedRoleId(DRAFT_ROLE_ID)
    reset({ name: '', permissionKeys: [] })
    setSuccess(null)
    setAssignmentError(null)
    const frame = requestAnimationFrame(() => nameInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [reset])

  const togglePermission = useCallback((key: PermissionKey) => {
    const current = getValues('permissionKeys') ?? []
    const next = current.includes(key)
      ? current.filter((item) => item !== key)
      : [...current, key]
    setValue('permissionKeys', next, { shouldDirty: true, shouldValidate: true })
  }, [getValues, setValue])

  const setGroup = useCallback((keys: PermissionKey[], checked: boolean) => {
    const current = getValues('permissionKeys') ?? []
    const next = checked
      ? [...new Set([...current, ...keys])]
      : current.filter((key) => !keys.includes(key))
    setValue('permissionKeys', next, { shouldDirty: true, shouldValidate: true })
  }, [getValues, setValue])

  const onSubmit = useCallback(async (values: RoleFormValues) => {
    if (!tenantId) return
    setIsSaving(true)
    setSuccess(null)
    setAssignmentError(null)
    try {
      if (selectedRole?.kind === 'custom') {
        const updated = await httpRoleAdapter.updateRole(
          sessionRef.current?.accessToken ?? '',
          tenantId,
          selectedRole.id,
          { permissionKeys: values.permissionKeys },
        )
        replaceRoles(state.kind === 'loaded' ? state.roles.map((role) => role.id === updated.id ? updated : role) : [updated])
        reset({ name: updated.name, permissionKeys: updated.permissionKeys })
        setSuccess(`مجوزهای نقش ${updated.name} ذخیره شد.`)
      } else {
        const created = await httpRoleAdapter.createRole(sessionRef.current?.accessToken ?? '', tenantId, values)
        if (state.kind === 'loaded') replaceRoles([...state.roles, created])
        setSelectedRoleId(created.id)
        reset({ name: created.name, permissionKeys: created.permissionKeys })
        setSuccess(`نقش ${created.name} ایجاد شد.`)
      }
    } catch (error) {
      if (error instanceof SessionExpiredError) {
        void signOutRef.current()
      } else if (error instanceof TenantRoleConflictError) {
        setError('name', { message: error.message })
      } else if (error instanceof TenantRoleValidationError) {
        for (const [field, message] of Object.entries(error.fieldErrors)) {
          setError(field as keyof RoleFormValues, { message })
        }
      } else if (error instanceof TenantRoleForbiddenError || error instanceof ApiUnavailableError) {
        setError('name', { message: error.message })
      }
      nameInputRef.current?.focus()
    } finally {
      setIsSaving(false)
    }
  }, [replaceRoles, reset, selectedRole, setError, state, tenantId])

  const submitRole = useCallback((event: FormEvent<HTMLFormElement>) => {
    void handleSubmit(onSubmit)(event)
  }, [handleSubmit, onSubmit])

  const toggleAssignment = useCallback(async (memberId: string, role: TenantRole) => {
    if (!tenantId) return
    setIsAssigning(true)
    setAssignmentError(null)
    setSuccess(null)
    try {
      const response = role.memberIds.includes(memberId)
        ? await httpRoleAdapter.unassignRole(sessionRef.current?.accessToken ?? '', tenantId, memberId, role.id)
        : await httpRoleAdapter.assignRole(sessionRef.current?.accessToken ?? '', tenantId, memberId, role.id)
      replaceRoles(response.roles)
      setSuccess('انتساب نقش به‌روزرسانی شد.')
    } catch (error) {
      if (error instanceof SessionExpiredError) {
        void signOutRef.current()
      } else if (error instanceof TenantRoleInvariantError) {
        setAssignmentError(error.message)
      } else if (error instanceof TenantRoleForbiddenError || error instanceof ApiUnavailableError) {
        setAssignmentError(error.message)
      }
    } finally {
      setIsAssigning(false)
    }
  }, [replaceRoles, tenantId])

  return (
    <DashboardShell>
      <section aria-label="مدیریت نقش‌ها و مجوزها" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-3xl">
            <p className="text-sm font-semibold text-primary">مجوزهای مستأجر</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">نقش‌ها و ماتریس مجوز</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              نقش سفارشی بسازید، مجوزها را به‌صورت گروهی انتخاب کنید و نقش را به اعضای همین مستأجر اختصاص دهید.
            </p>
          </div>
          {state.kind === 'loaded' && (
            <div className="flex shrink-0 items-center gap-2">
              <SecondaryButton type="button" className="px-3" onClick={loadRoles}>
                <RefreshCw aria-hidden="true" className="size-4" />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
              <Button type="button" onClick={createNewDraft}>نقش جدید</Button>
            </div>
          )}
        </div>

        {state.kind === 'loading' && <RolesSkeleton />}
        {state.kind === 'forbidden' && <ForbiddenRoles />}
        {state.kind === 'unavailable' && <UnavailableRoles onRetry={loadRoles} />}

        {state.kind === 'loaded' && (
          <div className="space-y-6">
            <div className="rounded-xl border border-primary/30 bg-primary/5 p-4 text-sm text-muted-foreground shadow-soft">
              <div className="flex items-start gap-3">
                <ShieldCheck aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-primary" />
                <p className="leading-6">
                  این ماتریس برای <bdi className="font-semibold text-foreground">{state.tenant.name}</bdi> است. پنهان‌سازی ناوبری فقط تجربه کاربری است؛ B009 باید هر خواندن، نوشتن و انتساب نقش را روی سرور بررسی کند.
                </p>
              </div>
            </div>

            <div className="grid gap-6 xl:grid-cols-[minmax(18rem,22rem)_1fr]">
              <RoleList roles={state.roles} selectedRoleId={selectedRole?.id ?? null} onSelect={setSelectedRoleId} />

              <form className="space-y-5 rounded-xl border border-border bg-surface p-5 shadow-soft" onSubmit={submitRole} noValidate>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <h3 className="text-base font-semibold">
                      {selectedRole?.kind === 'builtIn' ? `نقش سیستمی ${selectedRole.name}` : selectedRole ? `ویرایش ${selectedRole.name}` : 'نقش سفارشی جدید'}
                    </h3>
                    <p className="mt-1 text-sm leading-6 text-muted-foreground">
                      {selectedRole?.kind === 'builtIn'
                        ? 'نقش‌های سیستمی قابل مشاهده‌اند اما مجوزهایشان در این نسخه قفل است.'
                        : 'نام نقش در مستأجر یکتا است؛ مجوزهای انتخاب‌شده قرارداد B009 را تعیین می‌کنند.'}
                    </p>
                  </div>
                  {isDirty && selectedRole?.kind !== 'builtIn' && (
                    <span className="rounded-full bg-warning/10 px-3 py-1 text-xs font-semibold text-warning">تغییرات ذخیره‌نشده</span>
                  )}
                </div>

                {selectedRole?.kind !== 'builtIn' && (
                  <div>
                    <label className="mb-2 block text-sm font-semibold" htmlFor="role-name">نام نقش</label>
                    <TextInput
                      id="role-name"
                      autoComplete="off"
                      placeholder="User Manager"
                      aria-invalid={Boolean(errors.name)}
                      aria-describedby={errors.name ? 'role-name-error' : 'role-name-hint'}
                      {...register('name')}
                      ref={(node) => {
                        register('name').ref(node)
                        nameInputRef.current = node
                      }}
                    />
                    {errors.name ? (
                      <p className="mt-2 text-sm text-destructive" id="role-name-error">{errors.name.message}</p>
                    ) : (
                      <p className="mt-2 text-xs text-muted-foreground" id="role-name-hint">مثلاً User Manager؛ نام در هر مستأجر مستقل است.</p>
                    )}
                  </div>
                )}

                <PermissionMatrix
                  disabled={selectedRole?.kind === 'builtIn'}
                  selectedKeys={selectedKeys}
                  onToggle={togglePermission}
                  onSetGroup={setGroup}
                />
                {errors.permissionKeys && (
                  <p className="text-sm text-destructive" role="alert">{errors.permissionKeys.message}</p>
                )}

                <div className="flex flex-wrap items-center gap-2 border-t border-border pt-4">
                  <Button type="submit" disabled={isSaving || selectedRole?.kind === 'builtIn'}>
                    {isSaving ? (
                      <><Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />در حال ذخیره</>
                    ) : selectedRole?.kind === 'custom' ? 'ذخیره مجوزها' : 'ایجاد نقش'}
                  </Button>
                  {selectedRole?.kind === 'builtIn' && (
                    <p className="flex items-center gap-2 text-sm text-muted-foreground"><Lock aria-hidden="true" className="size-4" />نقش سیستمی قابل تغییر نیست.</p>
                  )}
                  {success && <p className="flex items-center gap-2 text-sm font-medium text-success" role="status"><CircleCheck aria-hidden="true" className="size-4" />{success}</p>}
                </div>
              </form>
            </div>

            <AssignmentPanel
              members={state.members}
              roles={state.roles}
              onToggle={toggleAssignment}
              busy={isAssigning}
              error={assignmentError}
            />
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function RoleList({ roles, selectedRoleId, onSelect }: { roles: TenantRole[]; selectedRoleId: string | null; onSelect: (id: string) => void }) {
  const customCount = roles.filter((role) => role.kind === 'custom').length
  return (
    <aside className="rounded-xl border border-border bg-surface p-4 shadow-soft" aria-label="فهرست نقش‌ها">
      <div className="mb-3 flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">نقش‌ها</h3>
        <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground"><bdi>{customCount}</bdi> سفارشی</span>
      </div>
      <div className="space-y-2">
        {roles.map((role) => (
          <button
            key={role.id}
            type="button"
            onClick={() => onSelect(role.id)}
            aria-pressed={selectedRoleId === role.id}
            className={cn(
              'w-full rounded-lg border p-3 text-start transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
              selectedRoleId === role.id ? 'border-primary bg-primary/10' : 'border-border hover:bg-muted',
            )}
          >
            <span className="flex items-center justify-between gap-2">
              <span className="font-semibold"><bdi>{role.name}</bdi></span>
              <RoleKindBadge kind={role.kind} />
            </span>
            <span className="mt-2 block text-xs leading-5 text-muted-foreground">{role.description}</span>
            <span className="mt-2 block text-xs text-muted-foreground"><bdi>{role.permissionKeys.length}</bdi> مجوز · <bdi>{role.memberIds.length}</bdi> عضو</span>
          </button>
        ))}
      </div>
    </aside>
  )
}

function PermissionMatrix({ disabled, selectedKeys, onToggle, onSetGroup }: { disabled: boolean; selectedKeys: PermissionKey[]; onToggle: (key: PermissionKey) => void; onSetGroup: (keys: PermissionKey[], checked: boolean) => void }) {
  return (
    <fieldset className="space-y-3" disabled={disabled}>
      <legend className="text-sm font-semibold">ماتریس مجوزها</legend>
      {PERMISSION_CATALOG.map((group) => {
        const keys = group.permissions.map((permission) => permission.key)
        const allChecked = keys.every((key) => selectedKeys.includes(key))
        return (
          <section key={group.id} className="rounded-lg border border-border bg-background/60 p-4" aria-label={`گروه ${group.label}`}>
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h4 className="text-sm font-semibold">{group.label}</h4>
                <p className="mt-1 text-xs leading-5 text-muted-foreground">{group.description}</p>
              </div>
              <label className="inline-flex min-h-9 items-center gap-2 rounded-md border border-border bg-surface px-3 text-xs font-semibold">
                <input type="checkbox" checked={allChecked} onChange={(event) => onSetGroup(keys, event.currentTarget.checked)} />
                انتخاب گروه
              </label>
            </div>
            <div className="mt-4 grid gap-2 md:grid-cols-2">
              {group.permissions.map((permission) => (
                <label key={permission.key} className="flex min-h-16 items-start gap-3 rounded-md border border-border bg-surface p-3 text-sm transition-colors hover:bg-muted">
                  <input
                    type="checkbox"
                    checked={selectedKeys.includes(permission.key)}
                    onChange={() => onToggle(permission.key)}
                    className="mt-1"
                  />
                  <span>
                    <span className="font-semibold">{permission.label}</span>
                    <code dir="ltr" className="ms-2 rounded bg-muted px-1.5 py-0.5 text-[11px] text-muted-foreground">{permission.key}</code>
                    <span className="mt-1 block text-xs leading-5 text-muted-foreground">{permission.description}</span>
                  </span>
                </label>
              ))}
            </div>
          </section>
        )
      })}
    </fieldset>
  )
}

function AssignmentPanel({ members, roles, onToggle, busy, error }: { members: TenantMember[]; roles: TenantRole[]; onToggle: (memberId: string, role: TenantRole) => void; busy: boolean; error: string | null }) {
  return (
    <section className="rounded-xl border border-border bg-surface p-5 shadow-soft" aria-label="انتساب نقش به اعضا">
      <div className="flex items-start gap-3">
        <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary"><UserCheck aria-hidden="true" className="size-5" /></span>
        <div>
          <h3 className="text-base font-semibold">انتساب نقش به اعضا</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">هر نقش به اعضای همین مستأجر اختصاص داده می‌شود؛ آخرین مالک مؤثر قابل حذف نیست.</p>
        </div>
      </div>
      {error && <p className="mt-4 rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive" role="alert">{error}</p>}
      <div className="mt-4 overflow-x-auto">
        <table className="w-full min-w-[46rem] text-sm">
          <caption className="sr-only">انتساب نقش‌ها به اعضای مستأجر</caption>
          <thead>
            <tr className="border-b border-border">
              <th scope="col" className="px-3 py-3 text-start font-semibold">عضو</th>
              {roles.map((role) => <th key={role.id} scope="col" className="px-3 py-3 text-start font-semibold"><bdi>{role.name}</bdi></th>)}
            </tr>
          </thead>
          <tbody>
            {members.map((member) => (
              <tr key={member.id} className="border-b border-border last:border-b-0">
                <th scope="row" className="px-3 py-3 text-start font-medium">
                  <bdi>{member.displayName}</bdi>
                  <span className="block text-xs font-normal text-muted-foreground"><bdi dir="ltr">{member.email}</bdi></span>
                </th>
                {roles.map((role) => {
                  const checked = role.memberIds.includes(member.id)
                  return (
                    <td key={role.id} className="px-3 py-3">
                      <button
                        type="button"
                        className={cn(
                          'inline-flex min-h-9 items-center rounded-md border px-3 text-xs font-semibold transition-colors disabled:pointer-events-none disabled:opacity-60',
                          checked ? 'border-primary bg-primary/10 text-primary' : 'border-border bg-surface hover:bg-muted',
                        )}
                        disabled={busy}
                        onClick={() => onToggle(member.id, role)}
                        aria-pressed={checked}
                      >
                        {checked ? 'اختصاص یافته' : 'اختصاص'}
                      </button>
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

function RoleKindBadge({ kind }: { kind: TenantRole['kind'] }) {
  return <span className="rounded-full bg-muted px-2 py-0.5 text-[11px] font-semibold text-muted-foreground">{kind === 'builtIn' ? 'سیستمی' : 'سفارشی'}</span>
}

function ForbiddenRoles() {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive"><Lock aria-hidden="true" className="size-6" /></span>
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">مدیریت نقش‌ها مجاز نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">حساب فعلی مالک مؤثر این مستأجر نیست. مخفی‌سازی کنترل‌های UI امنیت محسوب نمی‌شود؛ B009 باید همین عملیات را سمت سرور با 403 رد کند.</p>
        </div>
      </div>
    </div>
  )
}

function UnavailableRoles({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
      <div className="flex items-start gap-4">
        <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
        <div className="space-y-1.5">
          <p className="text-sm font-semibold">نقش‌ها در دسترس نیست</p>
          <p className="text-sm leading-6 text-muted-foreground">هم‌اکنون نمی‌توانیم ماتریس مجوز را بارگذاری کنیم.</p>
          <Button type="button" className="mt-3" onClick={onRetry}><RefreshCw aria-hidden="true" className="me-2 size-4" />تلاش دوباره</Button>
        </div>
      </div>
    </div>
  )
}

function RolesSkeleton() {
  return (
    <div className="grid gap-6 xl:grid-cols-[minmax(18rem,22rem)_1fr]" aria-busy="true">
      <div className="rounded-xl border border-border bg-surface p-4 shadow-soft">
        {[0, 1, 2].map((index) => <div key={index} className="mb-3 h-20 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />)}
      </div>
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <p className="sr-only">در حال بارگذاری نقش‌ها و مجوزها…</p>
        <div className="h-6 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        <div className="mt-5 grid gap-3 md:grid-cols-2">
          {[0, 1, 2, 3].map((index) => <div key={index} className="h-24 animate-pulse rounded-lg bg-muted motion-reduce:animate-none" />)}
        </div>
      </div>
    </div>
  )
}
