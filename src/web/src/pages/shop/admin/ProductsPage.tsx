import { CircleCheck, Loader2, Package, Plus, RefreshCw, TriangleAlert, Trash2 } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useFieldArray, useForm, useWatch } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { mockCatalogAdapter } from '@/features/shop/mockCatalogAdapter'
import {
  ProductConflictError,
  type ShopCategory,
  type ShopProductSummary,
} from '@/features/shop/shopCatalogTypes'
import { cn } from '@/lib/utils'

/**
 * S26 admin catalog management — products (F028: mocked data).
 *
 * A tenant member lists, creates and edits shop products, including each
 * product's color/size variants (an inline repeatable-row editor) and its
 * size-guide table (a column/row editor whose two field arrays stay in sync:
 * adding a column appends an empty value to every existing row, removing a
 * column splices that index out of every row). The data source is the
 * in-memory `mockCatalogAdapter`; F029 swaps it for `httpShopCatalogAdapter`
 * (same method names) without touching this page's structure.
 *
 * States:
 * - list: initial skeleton, loaded table, empty panel (the mock starts
 *   empty), retryable error panel;
 * - form: idle, invalid (per-field errors, including the variant/size-guide
 *   minimums), submitting, success feedback, and a duplicate-slug conflict
 *   surfaced under the slug field.
 */

const productSchema = z.object({
  name: z.string().min(1, 'نام محصول الزامی است.'),
  slug: z.string().min(1, 'نامک الزامی است.'),
  description: z.string(),
  categoryId: z.string().min(1, 'دسته‌بندی الزامی است.'),
  basePrice: z.coerce.number().positive('قیمت پایه باید مثبت باشد.'),
  compareAtPrice: z.coerce.number().nullable(),
  isActive: z.boolean(),
  variants: z
    .array(
      z.object({
        color: z.string().min(1),
        size: z.string().min(1),
        sku: z.string().min(1),
        stockQuantity: z.coerce.number().int().min(0),
        priceOverride: z.coerce.number().nullable(),
      }),
    )
    .min(1, 'حداقل یک تنوع رنگ/سایز الزامی است.'),
  sizeGuideColumns: z.array(z.string()),
  sizeGuideRows: z.array(z.object({ sizeLabel: z.string(), values: z.array(z.string()) })),
})
type ProductFormValuesInput = z.infer<typeof productSchema>

const EMPTY_VARIANT = { color: '', size: '', sku: '', stockQuantity: 0, priceOverride: null }

const FORM_DEFAULTS: ProductFormValuesInput = {
  name: '',
  slug: '',
  description: '',
  categoryId: '',
  basePrice: 0,
  compareAtPrice: null,
  isActive: true,
  variants: [{ ...EMPTY_VARIANT }],
  sizeGuideColumns: [],
  sizeGuideRows: [],
}

export function ProductsPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [products, setProducts] = useState<ShopProductSummary[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [listFailure, setListFailure] = useState<unknown>(null)
  const [categoryOptions, setCategoryOptions] = useState<ShopCategory[]>([])
  const [formOpen, setFormOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [success, setSuccess] = useState<string | null>(null)

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const nameInputRef = useRef<HTMLInputElement | null>(null)
  const slugInputRef = useRef<HTMLInputElement | null>(null)

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    clearErrors,
    getValues,
    setValue,
    formState: { errors },
  } = useForm({
    resolver: zodResolver(productSchema),
    defaultValues: FORM_DEFAULTS,
  })

  const {
    fields: variantFields,
    append: appendVariant,
    remove: removeVariant,
  } = useFieldArray({ control, name: 'variants' })
  // `sizeGuideColumns` is a plain `string[]`; react-hook-form 7.86 does not
  // accept a primitive array as a `useFieldArray` name (its `FieldArrayPath`
  // type only covers arrays of objects). The columns are therefore read with
  // `useWatch` and written with `setValue`, which keeps the same store, the
  // same submitted payload and the same add/remove row synchronization as the
  // spec's two-field-array design — only the columns' field-array hook is
  // replaced.
  const sizeGuideColumns = useWatch({ control, name: 'sizeGuideColumns' }) ?? []
  const {
    fields: rowFields,
    append: appendRow,
    remove: removeRow,
  } = useFieldArray({ control, name: 'sizeGuideRows' })

  const loadProducts = useCallback(() => {
    setIsBusy(true)
    setListFailure(null)
    return mockCatalogAdapter
      .listProducts(tenantId)
      .then(setProducts)
      .catch((error) => setListFailure(error))
      .finally(() => setIsBusy(false))
  }, [tenantId])

  // The category picker is populated from the same (mock) catalog source.
  useEffect(() => {
    void mockCatalogAdapter.listCategories(tenantId).then(setCategoryOptions)
  }, [tenantId])

  useEffect(() => {
    void loadProducts()
  }, [loadProducts])

  // Focus moves into the form when it opens (back to the toggle on close).
  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => nameInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const closeForm = useCallback(() => {
    setFormOpen(false)
    setEditingId(null)
    reset(FORM_DEFAULTS)
    clearErrors()
    setSuccess(null)
    toggleButtonRef.current?.focus()
  }, [reset, clearErrors])

  const openCreateForm = useCallback(() => {
    setEditingId(null)
    reset(FORM_DEFAULTS)
    clearErrors()
    setSuccess(null)
    setFormOpen(true)
  }, [reset, clearErrors])

  const startEdit = useCallback(
    async (product: ShopProductSummary) => {
      try {
        const full = await mockCatalogAdapter.getProduct(tenantId, product.id)
        setEditingId(full.id)
        reset({
          name: full.name,
          slug: full.slug,
          description: full.description,
          categoryId: full.categoryId,
          basePrice: full.basePrice,
          compareAtPrice: full.compareAtPrice,
          isActive: full.isActive,
          variants: full.variants.map((variant) => ({
            color: variant.color,
            size: variant.size,
            sku: variant.sku,
            stockQuantity: variant.stockQuantity,
            priceOverride: variant.priceOverride,
          })),
          sizeGuideColumns: full.sizeGuideColumns.map((column) => column.name),
          sizeGuideRows: full.sizeGuideRows.map((row) => ({
            sizeLabel: row.sizeLabel,
            values: row.cells.map((cell) => cell.value),
          })),
        })
        clearErrors()
        setSuccess(null)
        setFormOpen(true)
      } catch {
        setListFailure(new Error('Failed to load product'))
      }
    },
    [tenantId, reset, clearErrors],
  )

  const onSubmit = useCallback(
    async (values: ProductFormValuesInput) => {
      setIsSubmitting(true)
      setSuccess(null)
      try {
        if (editingId) {
          const updated = await mockCatalogAdapter.updateProduct(tenantId, editingId, values)
          setSuccess(`محصول ${updated.name} به‌روزرسانی شد.`)
        } else {
          const created = await mockCatalogAdapter.createProduct(tenantId, values)
          setSuccess(`محصول ${created.name} ایجاد شد.`)
        }
        reset(FORM_DEFAULTS)
        setFormOpen(false)
        setEditingId(null)
        void loadProducts()
      } catch (error) {
        if (error instanceof ProductConflictError) {
          setError('slug', { message: error.message })
          slugInputRef.current?.focus()
        }
      } finally {
        setIsSubmitting(false)
      }
    },
    [editingId, tenantId, reset, setError, loadProducts],
  )

  const isLoading = products === null && isBusy
  const listError = products === null && !isBusy && listFailure !== null
  const isEmpty = products !== null && products.length === 0
  const nameField = register('name')
  const slugField = register('slug')
  const categoryField = register('categoryId')
  const basePriceField = register('basePrice')
  const compareAtPriceField = register('compareAtPrice')

  return (
    <DashboardShell>
      <section aria-label="محصولات فروشگاه" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">محصولات</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              محصولات فروشگاه را همراه با تنوع‌های رنگ/سایز و جدول راهنمای سایز مدیریت کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {products !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست محصولات"
                className="px-3"
                disabled={isBusy}
                onClick={() => void loadProducts()}
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
              aria-expanded={formOpen}
              aria-controls="product-form"
              disabled={isSubmitting}
              onClick={() => (formOpen ? closeForm() : openCreateForm())}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <Package aria-hidden="true" className="me-2 size-4" />
                  ایجاد محصول
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
            id="product-form"
            className="space-y-6 rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleSubmit(onSubmit)(event)}
            noValidate
          >
            <div>
              <h3 className="text-base font-semibold">
                {editingId ? 'ویرایش محصول' : 'ایجاد محصول جدید'}
              </h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {editingId
                  ? 'اطلاعات محصول را ویرایش کنید.'
                  : 'مشخصات محصول، تنوع‌ها و جدول راهنمای سایز را وارد کنید.'}
              </p>
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-name">
                  نام
                </label>
                <TextInput
                  id="product-name"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.name)}
                  aria-describedby={errors.name ? 'product-name-error' : undefined}
                  {...nameField}
                  ref={(node) => {
                    nameField.ref(node)
                    nameInputRef.current = node
                  }}
                />
                {errors.name && (
                  <p className="mt-2 text-sm text-destructive" id="product-name-error">
                    {errors.name.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-slug">
                  نامک
                </label>
                <TextInput
                  id="product-slug"
                  autoComplete="off"
                  aria-invalid={Boolean(errors.slug)}
                  aria-describedby={errors.slug ? 'product-slug-error' : undefined}
                  {...slugField}
                  ref={(node) => {
                    slugField.ref(node)
                    slugInputRef.current = node
                  }}
                />
                {errors.slug && (
                  <p className="mt-2 text-sm text-destructive" id="product-slug-error">
                    {errors.slug.message}
                  </p>
                )}
              </div>
              <div className="md:col-span-2">
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-description">
                  توضیحات
                </label>
                <textarea
                  id="product-description"
                  rows={3}
                  className="w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors placeholder:text-muted-foreground focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
                  {...register('description')}
                />
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-category">
                  دسته‌بندی
                </label>
                <select
                  id="product-category"
                  className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm"
                  aria-invalid={Boolean(errors.categoryId)}
                  aria-describedby={errors.categoryId ? 'product-category-error' : undefined}
                  {...categoryField}
                >
                  <option value="" disabled>
                    دسته‌بندی را انتخاب کنید
                  </option>
                  {categoryOptions.map((category) => (
                    <option key={category.id} value={category.id}>
                      {category.name}
                    </option>
                  ))}
                </select>
                {errors.categoryId && (
                  <p className="mt-2 text-sm text-destructive" id="product-category-error">
                    {errors.categoryId.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-base-price">
                  قیمت پایه
                </label>
                <TextInput
                  id="product-base-price"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  aria-invalid={Boolean(errors.basePrice)}
                  aria-describedby={errors.basePrice ? 'product-base-price-error' : undefined}
                  {...basePriceField}
                />
                {errors.basePrice && (
                  <p className="mt-2 text-sm text-destructive" id="product-base-price-error">
                    {errors.basePrice.message}
                  </p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="product-compare-price">
                  قیمت قبل از تخفیف (اختیاری)
                </label>
                <TextInput
                  id="product-compare-price"
                  type="number"
                  inputMode="numeric"
                  min={0}
                  {...compareAtPriceField}
                />
              </div>
              <div className="flex items-end">
                <label
                  className="flex cursor-pointer items-center gap-2 text-sm font-semibold"
                  htmlFor="product-active"
                >
                  <input
                    id="product-active"
                    type="checkbox"
                    className="size-4 rounded border-input bg-surface accent-[var(--primary)] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                    {...register('isActive')}
                  />
                  فعال (در فروشگاه نمایش داده می‌شود)
                </label>
              </div>
            </div>

            {/* Variant rows: an inline repeatable editor (color / size / SKU /
                stock / price override) driven by a single useFieldArray. */}
            <fieldset className="min-w-0 space-y-3 rounded-lg border border-border p-4">
              <legend className="px-1 text-sm font-semibold">تنوع‌ها (رنگ/سایز)</legend>

              {variantFields.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  هنوز تنوعی اضافه نشده است.
                </p>
              )}

              {variantFields.map((variantField, variantIndex) => (
                <div
                  key={variantField.id}
                  className="grid gap-2 sm:grid-cols-2 lg:grid-cols-[repeat(5,1fr)_auto]"
                >
                  <TextInput
                    aria-label={`تنوع ${variantIndex + 1} — رنگ`}
                    placeholder="رنگ"
                    aria-invalid={Boolean(errors.variants?.[variantIndex]?.color)}
                    {...register(`variants.${variantIndex}.color`)}
                  />
                  <TextInput
                    aria-label={`تنوع ${variantIndex + 1} — سایز`}
                    placeholder="سایز"
                    aria-invalid={Boolean(errors.variants?.[variantIndex]?.size)}
                    {...register(`variants.${variantIndex}.size`)}
                  />
                  <TextInput
                    aria-label={`تنوع ${variantIndex + 1} — SKU`}
                    placeholder="SKU"
                    dir="ltr"
                    className="text-start"
                    aria-invalid={Boolean(errors.variants?.[variantIndex]?.sku)}
                    {...register(`variants.${variantIndex}.sku`)}
                  />
                  <TextInput
                    aria-label={`تنوع ${variantIndex + 1} — موجودی`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    placeholder="موجودی"
                    {...register(`variants.${variantIndex}.stockQuantity`)}
                  />
                  <TextInput
                    aria-label={`تنوع ${variantIndex + 1} — قیمت جایگزین (اختیاری)`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    placeholder="قیمت جایگزین (اختیاری)"
                    {...register(`variants.${variantIndex}.priceOverride`)}
                  />
                  <SecondaryButton
                    type="button"
                    className="px-3"
                    aria-label={`حذف تنوع ${variantIndex + 1}`}
                    onClick={() => removeVariant(variantIndex)}
                  >
                    <Trash2 aria-hidden="true" className="size-4" />
                  </SecondaryButton>
                </div>
              ))}

              {errors.variants && (
                <p className="text-sm text-destructive">{errors.variants.message}</p>
              )}

              <div>
                <SecondaryButton
                  type="button"
                  onClick={() => appendVariant({ ...EMPTY_VARIANT })}
                >
                  <Plus aria-hidden="true" className="me-2 size-4" />
                  افزودن تنوع
                </SecondaryButton>
              </div>
            </fieldset>

            {/* Size guide: two independent useFieldArrays (columns, rows) kept
                in sync — adding/removing a column appends/splices every row's
                matching value so the two stay aligned. */}
            <fieldset className="min-w-0 space-y-3 rounded-lg border border-border p-4">
              <legend className="px-1 text-sm font-semibold">جدول راهنمای سایز</legend>

              {sizeGuideColumns.length === 0 && rowFields.length === 0 ? (
                <p className="text-sm text-muted-foreground">
                  این محصول هنوز جدول راهنمای سایز ندارد. ستون‌ها (مثلاً «دور سینه») و سطر‌ها
                  (مثلاً «S»، «M») را اضافه کنید.
                </p>
              ) : (
                // A numeric measurement grid reads left→right (universal for
                // data tables). dir="ltr" on the scroller also stops Chromium
                // from reporting this RTL nested scroll container's extent as
                // document-level horizontal overflow, which would otherwise
                // widen the viewport on mobile.
                <div className="overflow-x-auto" dir="ltr">
                  <table className="w-full min-w-[32rem] text-sm">
                    <caption className="sr-only">جدول راهنمای سایز محصول</caption>
                    <thead>
                      <tr className="border-b border-border">
                        <th scope="col" className="px-2 py-2 text-start font-semibold">
                          سایز
                        </th>
                        {sizeGuideColumns.map((_column, columnIndex) => (
                          <th key={columnIndex} className="px-2 py-2 text-start font-semibold">
                            <div className="flex items-center gap-1">
                              <TextInput
                                aria-label={`ستون ${columnIndex + 1} — نام`}
                                placeholder="نام ستون"
                                className="min-h-9"
                                {...register(`sizeGuideColumns.${columnIndex}`)}
                              />
                              <SecondaryButton
                                type="button"
                                className="size-9 shrink-0 px-0"
                                aria-label={`حذف ستون ${columnIndex + 1}`}
                                onClick={() => handleRemoveColumn(columnIndex)}
                              >
                                <Trash2 aria-hidden="true" className="size-4" />
                              </SecondaryButton>
                            </div>
                          </th>
                        ))}
                        <th scope="col" className="px-2 py-2">
                          <span className="sr-only">عملیات سطر</span>
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {rowFields.map((rowField, rowIndex) => (
                        <tr key={rowField.id} className="border-b border-border last:border-b-0">
                          <td className="px-2 py-2">
                            <TextInput
                              aria-label={`سطر ${rowIndex + 1} — سایز`}
                              placeholder="سایز"
                              className="min-h-9"
                              {...register(`sizeGuideRows.${rowIndex}.sizeLabel`)}
                            />
                          </td>
                          {sizeGuideColumns.map((_column, columnIndex) => (
                            <td key={columnIndex} className="px-2 py-2">
                              <TextInput
                                aria-label={`سطر ${rowIndex + 1} — ستون ${columnIndex + 1}`}
                                className="min-h-9"
                                {...register(`sizeGuideRows.${rowIndex}.values.${columnIndex}`)}
                              />
                            </td>
                          ))}
                          <td className="px-2 py-2">
                            <SecondaryButton
                              type="button"
                              className="size-9 shrink-0 px-0"
                              aria-label={`حذف سطر ${rowIndex + 1}`}
                              onClick={() => removeRow(rowIndex)}
                            >
                              <Trash2 aria-hidden="true" className="size-4" />
                            </SecondaryButton>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <div className="flex flex-wrap gap-2">
                <SecondaryButton type="button" onClick={handleAddColumn}>
                  <Plus aria-hidden="true" className="me-2 size-4" />
                  افزودن ستون
                </SecondaryButton>
                <SecondaryButton type="button" onClick={handleAddRow}>
                  <Plus aria-hidden="true" className="me-2 size-4" />
                  افزودن سطر
                </SecondaryButton>
              </div>
            </fieldset>

            <div className="flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ذخیره
                  </>
                ) : editingId ? (
                  'ذخیره تغییرات'
                ) : (
                  'ایجاد محصول'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeForm} disabled={isSubmitting}>
                لغو
              </SecondaryButton>
            </div>
          </form>
        )}

        {isLoading && <ProductsSkeleton />}

        {listError && (
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title="فهرست محصولات در دسترس نیست"
            description="هم‌اکنون نمی‌توانیم محصولات را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید."
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={() => void loadProducts()}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </Button>
            }
          />
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <Package aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">محصولی موجود نیست</p>
            <p className="mt-1 text-sm text-muted-foreground">
              نخستین محصول فروشگاه را با دکمه «ایجاد محصول» بسازید.
            </p>
          </div>
        )}

        {products !== null && products.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[44rem] text-sm">
                <caption className="sr-only">فهرست محصولات فروشگاه</caption>
                <thead>
                  <tr className="border-b border-border text-start">
                    <th scope="col" className="px-4 py-3 text-start font-semibold">نام</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">نامک</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">دسته‌بندی</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">قیمت پایه</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">تعداد تنوع</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                    <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                  </tr>
                </thead>
                <tbody>
                  {products.map((product) => (
                    <tr key={product.id} className="border-b border-border last:border-b-0">
                      <td className="px-4 py-3">
                        <bdi className="font-medium">{product.name}</bdi>
                      </td>
                      <td className="px-4 py-3">
                        <bdi>{product.slug}</bdi>
                      </td>
                      <td className="px-4 py-3">
                        <bdi>{categoryName(product.categoryId)}</bdi>
                      </td>
                      <td className="px-4 py-3">{formatPrice(product.basePrice)}</td>
                      <td className="px-4 py-3 text-muted-foreground">{product.variantCount}</td>
                      <td className="px-4 py-3">
                        <StatusBadge active={product.isActive} />
                      </td>
                      <td className="px-4 py-3">
                        <SecondaryButton
                          type="button"
                          disabled={isSubmitting}
                          onClick={() => void startEdit(product)}
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

  function categoryName(categoryId: string) {
    return categoryOptions.find((category) => category.id === categoryId)?.name ?? '—'
  }

  /**
   * Adding a column: append the column, then push an empty value onto every
   * existing row's `values` so the two stay in sync.
   */
  function handleAddColumn() {
    setValue('sizeGuideColumns', [...sizeGuideColumns, ''])
    const rows = getValues('sizeGuideRows')
    setValue(
      'sizeGuideRows',
      rows.map((row) => ({ ...row, values: [...row.values, ''] })),
    )
  }

  /**
   * Removing a column: splice that index out of every row's `values`, then
   * remove the column — so the column's value disappears from every row.
   */
  function handleRemoveColumn(index: number) {
    const rows = getValues('sizeGuideRows')
    setValue(
      'sizeGuideRows',
      rows.map((row) => ({
        ...row,
        values: row.values.filter((_, columnIndex) => columnIndex !== index),
      })),
    )
    setValue(
      'sizeGuideColumns',
      sizeGuideColumns.filter((_, columnIndex) => columnIndex !== index),
    )
  }

  function handleAddRow() {
    const columnCount = sizeGuideColumns.length
    appendRow({ sizeLabel: '', values: Array(columnCount).fill('') })
  }
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

/** Persian (fa-IR) currency rendering of a price. */
function formatPrice(value: number) {
  if (!Number.isFinite(value)) return '—'
  return `${new Intl.NumberFormat('fa-IR').format(value)} تومان`
}

/**
 * Loading placeholder with the table's footprint (header + rows) so the
 * transition to data causes no layout shift.
 */
function ProductsSkeleton() {
  return (
    <div
      aria-busy="true"
      className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft"
    >
      <p className="sr-only">
        در حال بارگذاری فهرست محصولات
        <span aria-hidden="true">…</span>
      </p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-48 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-32 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-28 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}
