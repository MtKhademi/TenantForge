import { CircleCheck, Loader2, RefreshCw, Tag, TriangleAlert } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import {
  createShopCatalogAdapter,
  ShopForbiddenError,
  ShopValidationError,
} from '@/features/shop/shopCatalogAdapter'
import { CategoryConflictError, type ShopCategory } from '@/features/shop/shopCatalogTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { cn } from '@/lib/utils'

/**
 * S26 admin catalog management — categories (F029: connected to the real B026 API).
 *
 * A tenant member lists, creates and edits the shop's categories. The data
 * source is `createShopCatalogAdapter`, which calls B026's tenant-scoped
 * admin endpoints with the current session bearer token. The page's
 * structure and accepted UX are unchanged from the F028 mock.
 *
 * States:
 * - list: initial skeleton, loaded table, empty panel (a fresh tenant starts
 *   empty), retryable error panel (unavailable or forbidden);
 * - form: idle, invalid (per-field errors), submitting, success feedback, and
 *   a duplicate-slug conflict surfaced under the slug field.
 * - the form is a create form and an edit form: in edit mode an "active"
 *   checkbox appears (a created category is always active).
 */

const categorySchema = z.object({
  name: z.string().min(1, 'نام دسته‌بندی الزامی است.').max(120),
  slug: z.string().min(1, 'نامک الزامی است.').max(120),
  displayOrder: z.coerce.number().int().min(0),
})
type CategoryFormValues = z.infer<typeof categorySchema>

export function CategoriesPage() {
  const { session, signOut } = useAuth()
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [categories, setCategories] = useState<ShopCategory[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [listFailure, setListFailure] = useState<'unavailable' | 'forbidden' | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editIsActive, setEditIsActive] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const nameInputRef = useRef<HTMLInputElement | null>(null)
  const slugInputRef = useRef<HTMLInputElement | null>(null)
  // Fresh refs so the (stable) callbacks below always read the current token
  // and sign-out action without re-creating the data-source per render.
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)
  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  // One bound data-source instance per render, carrying the current token.
  // Event handlers (submit / startEdit) read `adapter` directly; the
  // effect-driven load callbacks read `adapterRef` so they stay referentially
  // stable (a fresh per-render object must not land in a `useEffect` dep, or
  // it refetches forever).
  const adapter = createShopCatalogAdapter(session?.accessToken ?? '')
  const adapterRef = useRef(adapter)
  useEffect(() => {
    adapterRef.current = adapter
  }, [adapter])

  const {
    register,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm({
    resolver: zodResolver(categorySchema),
    defaultValues: { name: '', slug: '', displayOrder: 0 },
  })

  const handleListFailure = useCallback((error: unknown) => {
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    setListFailure(error instanceof ShopForbiddenError ? 'forbidden' : 'unavailable')
  }, [])

  const loadCategories = useCallback(() => {
    setIsBusy(true)
    setListFailure(null)
    return adapterRef.current
      .listCategories(tenantId)
      .then(setCategories)
      .catch((error) => handleListFailure(error))
      .finally(() => setIsBusy(false))
  }, [tenantId, handleListFailure])

  useEffect(() => {
    void loadCategories()
  }, [loadCategories])

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
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  const openCreateForm = useCallback(() => {
    setEditingId(null)
    setEditIsActive(true)
    reset()
    clearErrors()
    setSuccess(null)
    setFormOpen(true)
  }, [reset, clearErrors])

  const startEdit = useCallback(
    (category: ShopCategory) => {
      setEditingId(category.id)
      setEditIsActive(category.isActive)
      reset({ name: category.name, slug: category.slug, displayOrder: category.displayOrder })
      clearErrors()
      setSuccess(null)
      setFormOpen(true)
    },
    [reset, clearErrors],
  )

  const onSubmit = useCallback(
    async (values: CategoryFormValues) => {
      setIsSubmitting(true)
      setSuccess(null)
      try {
        if (editingId) {
          const updated = await adapter.updateCategory(tenantId, editingId, {
            ...values,
            isActive: editIsActive,
          })
          setSuccess(`دسته‌بندی ${updated.name} به‌روزرسانی شد.`)
        } else {
          const created = await adapter.createCategory(tenantId, values)
          setSuccess(`دسته‌بندی ${created.name} ایجاد شد.`)
        }
        reset()
        setFormOpen(false)
        setEditingId(null)
        void loadCategories()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof CategoryConflictError) {
          setError('slug', { message: error.message })
          slugInputRef.current?.focus()
        } else if (error instanceof ShopValidationError) {
          for (const [field, message] of Object.entries(error.fieldErrors)) {
            setError(field as keyof CategoryFormValues, { message })
          }
          nameInputRef.current?.focus()
        } else if (error instanceof ShopForbiddenError) {
          setError('name', { message: error.message })
          nameInputRef.current?.focus()
        }
      } finally {
        setIsSubmitting(false)
      }
    },
    [editingId, editIsActive, tenantId, adapter, reset, setError, loadCategories],
  )

  const isLoading = categories === null && isBusy
  const listError = categories === null && !isBusy && listFailure !== null
  const isEmpty = categories !== null && categories.length === 0
  const nameField = register('name')
  const slugField = register('slug')
  const orderField = register('displayOrder')

  return (
    <DashboardShell>
      <section aria-label="دسته‌بندی‌های فروشگاه" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">دسته‌بندی‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              دسته‌بندی‌های محصولات فروشگاه را مشاهده و مدیریت کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {categories !== null && (
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
                : 'دسته‌بندی با وضعیت «فعال» ایجاد می‌شود.'}
            </p>

            <div className="mt-4 grid gap-4 md:grid-cols-3">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-name">
                  نام
                </label>
                <TextInput
                  id="category-name"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.name)}
                  aria-describedby={errors.name ? 'category-name-error' : undefined}
                  {...nameField}
                  ref={(node) => {
                    nameField.ref(node)
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
                  {...slugField}
                  ref={(node) => {
                    slugField.ref(node)
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

        {categories !== null && categories.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[40rem] text-sm">
                <caption className="sr-only">فهرست دسته‌بندی‌های فروشگاه</caption>
                <thead>
                  <tr className="border-b border-border text-start">
                    <th scope="col" className="px-4 py-3 text-start font-semibold">نام</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">نامک</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">ترتیب</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                  </tr>
                </thead>
                <tbody>
                  {categories.map((category) => (
                    <tr key={category.id} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3">
                        <bdi className="font-medium">{category.name}</bdi>
                      </td>
                      <td className="px-4 py-3">
                        <bdi>{category.slug}</bdi>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">{category.displayOrder}</td>
                      <td className="px-4 py-3">
                        <StatusBadge active={category.isActive} />
                      </td>
                      <td className="px-4 py-3">
                        <SecondaryButton
                          type="button"
                          disabled={isSubmitting}
                          onClick={() => startEdit(category)}
                        >
                          ویرایش
                        </SecondaryButton>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </section>
    </DashboardShell>
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
 * Loading placeholder with the table's footprint (header + rows) so the
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
