import { CircleCheck, Loader2, RefreshCw, Tag, TriangleAlert } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import type { AdminCategory } from '@/features/shop/contracts/categoryHierarchyContract'
import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { cn } from '@/lib/utils'

/**
 * S26/S34 admin catalog management — categories (F046: hierarchy mock).
 *
 * The data source is the `categories` slot of `ShopClientsProvider` — the
 * deterministic B038 mock now, the HTTP client after F056. The page never
 * imports fixtures and never calls `fetch`.
 *
 * The list is deliberately a plain two-level list (roots, each with its
 * direct children indented): B038's data model has no third level, so no
 * general-purpose tree component is built. The form's parent selector lists
 * ONLY active root categories — a child or an inactive category can never be
 * selected as a parent from the UI; the mock additionally rejects the named
 * B038 violations (third level, inactive parent, unsafe reparent) with the
 * exact contract error shape.
 *
 * States:
 * - list: initial skeleton, loaded two-level list, empty panel, retryable
 *   error panel (unavailable or forbidden);
 * - form: idle, invalid (per-field errors including `parentCategoryId`),
 *   submitting, conflict/forbidden/not-found banner, unavailable-with-retry,
 *   success feedback shown only when the client actually returned one.
 */

const categorySchema = z.object({
  name: z.string().min(1, 'نام دسته‌بندی الزامی است.').max(120),
  slug: z.string().min(1, 'نامک الزامی است.').max(120),
  displayOrder: z.coerce.number().int().min(0),
  parent: z.string(),
})
type CategoryFormValues = z.infer<typeof categorySchema>

/** True when the client rejected with an AbortError (superseded request). */
function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

export function CategoriesPage() {
  const { signOut } = useAuth()
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { categories: categoryClient } = useShopClients()

  const [rows, setRows] = useState<AdminCategory[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [listFailure, setListFailure] = useState<'unavailable' | 'forbidden' | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editIsActive, setEditIsActive] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)
  const [formFailure, setFormFailure] = useState<{ message: string; retryable: boolean } | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const nameInputRef = useRef<HTMLInputElement | null>(null)
  const slugInputRef = useRef<HTMLInputElement | null>(null)
  const parentSelectRef = useRef<HTMLSelectElement | null>(null)
  const signOutRef = useRef(signOut)
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  // The client slot is stable per render; keep a fresh ref for the
  // referentially-stable load callback (same pattern as the F029 adapter).
  const clientRef = useRef(categoryClient)
  useEffect(() => {
    clientRef.current = categoryClient
  }, [categoryClient])
  const listAbortRef = useRef<AbortController | null>(null)

  const handleListFailure = useCallback((error: unknown) => {
    if (isAbortError(error)) return
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    if (error instanceof ShopClientError && error.problem.status === 403) {
      setListFailure('forbidden')
      return
    }
    setListFailure('unavailable')
  }, [])

  const loadCategories = useCallback(() => {
    // Aborting the in-flight request guarantees an old response can never
    // overwrite a newer one's result.
    listAbortRef.current?.abort()
    const controller = new AbortController()
    listAbortRef.current = controller
    setIsBusy(true)
    setListFailure(null)
    return clientRef.current
      .listAdmin(tenantId, controller.signal)
      .then((list) => {
        if (!controller.signal.aborted) setRows(list)
      })
      .catch((error) => {
        if (controller.signal.aborted) return
        handleListFailure(error)
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsBusy(false)
      })
  }, [tenantId, handleListFailure])

  useEffect(() => {
    void loadCategories()
    return () => {
      listAbortRef.current?.abort()
    }
  }, [loadCategories])

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm({
    resolver: zodResolver(categorySchema),
    defaultValues: { name: '', slug: '', displayOrder: 0, parent: '' },
  })

  // The list is already sorted by displayOrder within each level, so the
  // two-level view model is derived, never re-sorted client-side per level.
  const { roots, childrenOf } = useMemo(() => {
    const current = rows ?? []
    const byOrder = (list: AdminCategory[]) => [...list].sort((a, b) => a.displayOrder - b.displayOrder)
    return {
      roots: byOrder(current.filter((row) => row.parentCategoryId === null)),
      childrenOf: (rootId: string) => byOrder(current.filter((row) => row.parentCategoryId === rootId)),
    }
  }, [rows])

  // Only ACTIVE ROOTS may be parents (B038); children and inactive categories
  // never appear here.
  const parentOptions = useMemo(
    () => [...roots].filter((root) => root.isActive).sort((a, b) => a.displayOrder - b.displayOrder),
    [roots],
  )

  // Focus moves into the form when it opens (back to the toggle on close).
  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => nameInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const closeForm = useCallback(() => {
    setFormOpen(false)
    setEditingId(null)
    reset()
    clearErrors()
    setSuccess(null)
    setFormFailure(null)
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  const openCreateForm = useCallback(() => {
    setEditingId(null)
    setEditIsActive(true)
    reset({ name: '', slug: '', displayOrder: 0, parent: '' })
    clearErrors()
    setSuccess(null)
    setFormFailure(null)
    setFormOpen(true)
  }, [reset, clearErrors])

  const startEdit = useCallback(
    (category: AdminCategory) => {
      setEditingId(category.id)
      setEditIsActive(category.isActive)
      reset({
        name: category.name,
        slug: category.slug,
        displayOrder: category.displayOrder,
        parent: category.parentCategoryId ?? '',
      })
      clearErrors()
      setSuccess(null)
      setFormFailure(null)
      setFormOpen(true)
    },
    [reset, clearErrors],
  )

  const onSubmit = useCallback(
    async (values: CategoryFormValues) => {
      setIsSubmitting(true)
      setSuccess(null)
      setFormFailure(null)
      const controller = new AbortController()
      const parentCategoryId = values.parent.length > 0 ? values.parent : null
      try {
        if (editingId) {
          const updated = await clientRef.current.update(tenantId, editingId, {
            name: values.name,
            slug: values.slug,
            displayOrder: values.displayOrder,
            isActive: editIsActive,
            parentCategoryId,
          }, controller.signal)
          setSuccess(`دسته‌بندی ${updated.name} به‌روزرسانی شد.`)
        } else {
          const created = await clientRef.current.create(
            tenantId,
            {
              name: values.name,
              slug: values.slug,
              displayOrder: values.displayOrder,
              parentCategoryId,
            },
            controller.signal,
          )
          setSuccess(`دسته‌بندی ${created.name} ایجاد شد.`)
        }
        reset()
        setFormOpen(false)
        setEditingId(null)
        void loadCategories()
      } catch (error) {
        if (isAbortError(error)) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof ApiUnavailableError) {
          setFormFailure({
            message: 'ذخیره تغییرات ممکن نشد؛ اتصال در دسترس نیست. دوباره تلاش کنید.',
            retryable: true,
          })
        } else if (error instanceof ShopClientError) {
          const { problem } = error
          if (problem.status === 400) {
            const fieldErrors = problem.errors ?? {}
            let focused = false
            for (const [field, messages] of Object.entries(fieldErrors)) {
              const message = messages[0]
              if (!message) continue
              if (field === 'parentCategoryId') {
                setError('parent', { message })
                parentSelectRef.current?.focus()
                focused = true
              } else {
                setError(field as keyof CategoryFormValues, { message })
                if (!focused) {
                  if (field === 'name') nameInputRef.current?.focus()
                  else if (field === 'slug') slugInputRef.current?.focus()
                  focused = true
                }
              }
            }
            if (!focused) {
              setFormFailure({ message: problem.detail ?? problem.title ?? 'مقادیر ارسالی معتبر نیست.', retryable: false })
            }
          } else {
            // 409 (unsafe reparent), 403 (no permission) and 404 (unknown
            // category) each keep their own message — never one generic one.
            setFormFailure({ message: problem.detail ?? problem.title ?? 'عملیات انجام نشد.', retryable: false })
          }
        }
      } finally {
        if (!controller.signal.aborted) setIsSubmitting(false)
      }
    },
    [editingId, editIsActive, tenantId, reset, setError, loadCategories],
  )

  const isLoading = rows === null && isBusy
  const listError = rows === null && !isBusy && listFailure !== null
  const isEmpty = rows !== null && rows.length === 0
  const nameField = register('name')
  const { ref: nameRef, ...nameInput } = nameField
  const slugField = register('slug')
  const { ref: slugRef, ...slugInput } = slugField
  const orderField = register('displayOrder')
  const parentField = register('parent')
  const { ref: parentRef, ...parentInput } = parentField

  return (
    <DashboardShell>
      <section aria-label="دسته‌بندی‌های فروشگاه" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">دسته‌بندی‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              ریشه‌ها و زیر‌دسته‌های فروشگاه را مشاهده و مدیریت کنید. هر ریشه فقط یک سطح زیر‌دسته دارد.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {rows !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست دسته‌بندی‌ها"
                className="px-3"
                disabled={isBusy}
                onClick={() => void loadCategories()}
              >
                <RefreshCw
                  aria-hidden="true"
                  className={cn('size-4', isBusy && 'animate-spin motion-reduce:animate-none')}
                />
                <span className="hidden sm:inline">به‌روزرسانی</span>
              </SecondaryButton>
            )}
            <Button
              type="button"
              ref={toggleButtonRef}
              className={cn(formOpen && 'bg-primary hover:bg-primary')}
              aria-expanded={formOpen}
              aria-controls="category-form"
              disabled={isSubmitting}
              onClick={() => (formOpen ? closeForm() : openCreateForm())}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <Tag aria-hidden="true" className="me-2 size-4" />
                  ایجاد دسته‌بندی
                </>
              )}
            </Button>
          </div>
        </div>

        {success && (
          <div
            role="status"
            className="flex items-center gap-2 rounded-lg border border-success/40 bg-success/10 px-4 py-2 text-sm font-medium text-success"
          >
            <CircleCheck aria-hidden="true" className="size-4" />
            {success}
          </div>
        )}

        {formOpen && (
          <form
            id="category-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleSubmit(onSubmit)(event)}
            noValidate
          >
            <h3 className="text-base font-semibold">
              {editingId ? 'ویرایش دسته‌بندی' : 'ایجاد دسته‌بندی جدید'}
            </h3>
            <p className="mt-1 text-sm text-muted-foreground">
              {editingId
                ? 'اطلاعات دسته‌بندی را ویرایش کنید.'
                : 'دسته‌بندی با وضعیت «فعال» ایجاد می‌شود. زیر‌دسته فقط می‌تواند مستقیماً زیر یک ریشه باشد.'}
            </p>

            <div className="mt-4 grid gap-4 md:grid-cols-2 lg:grid-cols-4">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-name">
                  نام
                </label>
                <TextInput
                  id="category-name"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.name)}
                  aria-describedby={errors.name ? 'category-name-error' : undefined}
                  {...nameInput}
                  ref={(node) => {
                    nameRef(node)
                    nameInputRef.current = node
                  }}
                />
                {errors.name && (
                  <p className="mt-2 text-sm text-destructive" id="category-name-error">
                    {errors.name.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-slug">
                  نامک
                </label>
                <TextInput
                  id="category-slug"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.slug)}
                  aria-describedby={errors.slug ? 'category-slug-error' : undefined}
                  {...slugInput}
                  ref={(node) => {
                    slugRef(node)
                    slugInputRef.current = node
                  }}
                />
                {errors.slug && (
                  <p className="mt-2 text-sm text-destructive" id="category-slug-error">
                    {errors.slug.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-order">
                  ترتیب نمایش
                </label>
                <TextInput
                  id="category-order"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  step={1}
                  aria-invalid={Boolean(errors.displayOrder)}
                  aria-describedby={errors.displayOrder ? 'category-order-error' : undefined}
                  {...orderField}
                />
                {errors.displayOrder && (
                  <p className="mt-2 text-sm text-destructive" id="category-order-error">
                    {errors.displayOrder.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-parent">
                  ریشه‌ی والد
                </label>
                <select
                  id="category-parent"
                  ref={(node) => {
                    parentRef(node)
                    parentSelectRef.current = node
                  }}
                  aria-invalid={Boolean(errors.parent)}
                  aria-describedby={errors.parent ? 'category-parent-error' : 'category-parent-hint'}
                  className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground transition-colors focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring md:text-sm"
                  {...parentInput}
                >
                  <option value="">بدون والد (ریشه)</option>
                  {parentOptions.map((root) => (
                    <option key={root.id} value={root.id}>
                      {root.name}
                    </option>
                  ))}
                </select>
                {errors.parent ? (
                  <p className="mt-2 text-sm text-destructive" id="category-parent-error">
                    {errors.parent.message}
                  </p>
                ) : (
                  <p className="mt-2 text-xs text-muted-foreground" id="category-parent-hint">
                    فقط ریشه‌های فعال قابل انتخاب هستند.
                  </p>
                )}
              </div>
            </div>

            {editingId && (
              <label
                className="mt-4 flex cursor-pointer items-center gap-2 text-sm font-semibold"
                htmlFor="category-active"
              >
                <input
                  id="category-active"
                  type="checkbox"
                  className="size-4 rounded border-input bg-surface accent-[var(--primary)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                  checked={editIsActive}
                  onChange={(event) => setEditIsActive(event.target.checked)}
                />
                فعال (در فروشگاه نمایش داده می‌شود)
              </label>
            )}

            {formFailure && (
              <div
                role="alert"
                className="mt-4 flex flex-wrap items-center gap-2 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-2 text-sm font-medium text-destructive"
              >
                <TriangleAlert aria-hidden="true" className="size-4 shrink-0" />
                <span className="min-w-0 flex-1">{formFailure.message}</span>
                {formFailure.retryable && (
                  <Button type="submit" className="min-w-28">
                    <RefreshCw aria-hidden="true" className="me-2 size-4" />
                    تلاش دوباره
                  </Button>
                )}
              </div>
            )}

            <div className="mt-5 flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ذخیره
                  </>
                ) : editingId ? (
                  'ذخیره تغییرات'
                ) : (
                  'ایجاد دسته‌بندی'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeForm} disabled={isSubmitting}>
                لغو
              </SecondaryButton>
            </div>
          </form>
        )}

        {isLoading && <CategoriesSkeleton />}

        {listError && (
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title={listFailure === 'forbidden' ? 'دسترسی مدیریت فروشگاه مجاز نیست' : 'فهرست دسته‌بندی‌ها در دسترس نیست'}
            description={
              listFailure === 'forbidden'
                ? 'حساب فعلی مجوز مدیریت فروشگاه این مستأجر را ندارد.'
                : 'هم‌اکنون نمی‌توانیم دسته‌بندی‌ها را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.'
            }
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={() => void loadCategories()}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </Button>
            }
          />
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <Tag aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">دسته‌بندی‌ای موجود نیست</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین دسته‌بندی فروشگاه را با دکمه «ایجاد دسته‌بندی» بسازید.
            </p>
          </div>
        )}

        {rows !== null && rows.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <div className="border-b border-border px-4 py-3">
              <h3 className="text-sm font-semibold">ریشه‌ها و زیر‌دسته‌ها</h3>
            </div>
            <ul className="divide-y divide-border" role="list">
              {roots.map((root) => {
                const children = childrenOf(root.id)
                return (
                  <li key={root.id}>
                    <CategoryRow
                      category={root}
                      isChild={false}
                      isSubmitting={isSubmitting}
                      onEdit={startEdit}
                    />
                    {children.length > 0 && (
                      <ul className="divide-y divide-border/60 border-t border-border/60 bg-muted/30" role="list">
                        {children.map((child) => (
                          <li key={child.id}>
                            <CategoryRow category={child} isChild isSubmitting={isSubmitting} onEdit={startEdit} />
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                )
              })}
            </ul>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}

function CategoryRow({
  category,
  isChild,
  isSubmitting,
  onEdit,
}: {
  category: AdminCategory
  isChild: boolean
  isSubmitting: boolean
  onEdit: (category: AdminCategory) => void
}) {
  return (
    <div
      className={cn(
        'flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3',
        isChild && 'ps-10', // logical-start indent: RTL-safe child level
      )}
    >
      <div className="flex min-w-0 flex-1 basis-48 items-center gap-2">
        {isChild && <span aria-hidden="true" className="text-muted-foreground">↳</span>}
        <bdi className={cn('truncate font-medium', !category.isActive && 'text-muted-foreground')}>
          {category.name}
        </bdi>
      </div>
      <bdi className="hidden min-w-0 max-w-40 truncate text-sm text-muted-foreground sm:block">{category.slug}</bdi>
      <span className="text-sm text-muted-foreground">{category.displayOrder}</span>
      <StatusBadge active={category.isActive} />
      <SecondaryButton type="button" disabled={isSubmitting} onClick={() => onEdit(category)}>
        ویرایش
      </SecondaryButton>
    </div>
  )
}

function StatusBadge({ active }: { active: boolean }) {
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
 * Loading placeholder with the list's footprint (header + rows) so the
 * transition to data causes no layout shift.
 */
function CategoriesSkeleton() {
  return (
    <div
      aria-busy="true"
      className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft"
    >
      <p className="sr-only">
        در حال بارگذاری فهرست دسته‌بندی‌ها
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
