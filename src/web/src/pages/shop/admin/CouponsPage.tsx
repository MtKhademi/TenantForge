import {
  BadgePercent,
  CircleCheck,
  Lock,
  Loader2,
  Pencil,
  RefreshCw,
  TriangleAlert,
} from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { PaginationControls } from '@/components/ui/PaginationControls'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import {
  type Coupon,
  type CouponListResponse,
} from '@/features/shop/contracts/couponRulesContract'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import {
  DEFAULT_PAGE_SIZE,
  type PageSizeOption,
} from '@/features/pagination/paginationTypes'
import { cn } from '@/lib/utils'

/**
 * S37 (F049): admin coupon management with B041's advanced rules — minimum
 * subtotal, maximum discount cap, redemption limit/usage, and optimistic
 * concurrency. Consumes the `coupons` client slot from `useShopClients()`
 * (the deterministic mock now, the HTTP implementation after F059) — it never
 * imports fixtures and never calls `fetch` directly.
 *
 * `null` renders as «نامحدود» (unlimited) for both the cap and the redemption
 * limit; usage shows `redeemedCount` vs `redemptionLimit`; expired and
 * inactive coupons are styled distinctly; a coupon with any redemption
 * (redeemedCount > 0) has its code and discount type locked. Every money
 * input is labelled with its unit (تومان) and normalizes Persian digits to
 * standard digits before validating/submitting.
 */

// ---- Persian-digit normalization (inputs accept ۰-۹ / ٠-٩, we store 0-9) ----
const DIGIT_MAP: Record<string, string> = {
  '۰': '0', '۱': '1', '۲': '2', '۳': '3', '۴': '4', '۵': '5', '۶': '6', '۷': '7', '۸': '8', '۹': '9',
  '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4', '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9',
}
function normalizeDigits(raw: string): string {
  return raw.replace(/[۰-۹٠-٩]/g, (d) => DIGIT_MAP[d] ?? d)
}

/**
 * Numeric form fields stay plain strings so the resolver's input/output types
 * match (no `.transform`). The submit handler runs `parseMoney` (Persian-digit
 * normalization + numeric parse) and `validateMoney` (B041 range rules) before
 * building the request, surfacing field errors via `setError`. This keeps the
 * "normalize Persian digits before validating/submitting" rule explicit and
 * testable without fighting react-hook-form's resolver typing.
 */
/** Returns `null` for an empty value; otherwise the normalized number (or `NaN`). */
function parseMoney(raw: string): number | null {
  const norm = normalizeDigits(raw).trim()
  if (norm === '') return null
  return Number(norm)
}

/** B041 range check for a parsed value. Returns an error message or `null` if valid. */
function validateMoney(
  value: number | null,
  opts: { required?: boolean; integer?: boolean; min?: number; max?: number },
): string | null {
  const { required = false, integer = false, min, max } = opts
  if (value === null) return required ? 'این فیلد الزامی است.' : null
  if (typeof value !== 'number' || Number.isNaN(value)) return 'مقدار باید عددی معتبر باشد.'
  if (integer && !Number.isInteger(value)) return 'مقدار باید عدد صحیح باشد.'
  if (min !== undefined && value < min) return 'مقدار کمتر از حد مجاز است.'
  if (max !== undefined && value > max) return 'مقدار بیشتر از حد مجاز است.'
  return null
}

function isoToDateInput(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toISOString().slice(0, 10)
}

const createSchema = z.object({
  code: z.string().min(1, 'کد تخفیف الزامی است.').max(40),
  discountType: z.enum(['Percentage', 'FixedAmount']),
  discountValue: z.string(),
  minimumSubtotal: z.string(),
  maximumDiscountAmount: z.string(),
  redemptionLimit: z.string(),
  expiresAtUtc: z.string(),
})
type CreateFormValues = z.infer<typeof createSchema>

const editSchema = z.object({
  discountValue: z.string(),
  minimumSubtotal: z.string(),
  maximumDiscountAmount: z.string(),
  redemptionLimit: z.string(),
  expiresAtUtc: z.string(),
  isActive: z.boolean(),
})
type EditFormValues = z.infer<typeof editSchema>

export function CouponsPage() {
  const { session, signOut } = useAuth()
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { coupons: couponsClient } = useShopClients()

  const [coupons, setCoupons] = useState<Coupon[] | null>(null)
  const [pagination, setPagination] = useState<CouponListResponse['pagination'] | null>(null)
  const [pageNumber, setPageNumber] = useState(1)
  const [pageSize, setPageSize] = useState<PageSizeOption>(DEFAULT_PAGE_SIZE)
  const [isBusy, setIsBusy] = useState(true)
  const [listFailure, setListFailure] = useState<'forbidden' | 'unavailable' | null>(null)
  const [banner, setBanner] = useState<{ tone: 'success' | 'error'; text: string } | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [createSubmitting, setCreateSubmitting] = useState(false)

  const [editingId, setEditingId] = useState<string | null>(null)
  const [editSubmitting, setEditSubmitting] = useState(false)
  const [editConflict, setEditConflict] = useState(false)

  const [deactivatingId, setDeactivatingId] = useState<string | null>(null)

  const controllerRef = useRef<AbortController | null>(null)
  const signOutRef = useRef(signOut)
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])
  // `session` is read so the page re-renders on auth changes; the mock client
  // ignores the token, but keeping it in the dep set mirrors the connected page.
  void session

  const load = useCallback(() => {
    // Superseded-request safety: abort the in-flight read, then ignore its
    // result (and any error it raises) once a newer read has started.
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    setIsBusy(true)
    setListFailure(null)
    couponsClient
      .list(tenantId, { pageNumber, pageSize }, controller.signal)
      .then((res) => {
        if (controller.signal.aborted) return
        setCoupons(res.coupons)
        setPagination(res.pagination)
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setListFailure(error instanceof ShopClientError && error.problem.status === 403 ? 'forbidden' : 'unavailable')
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsBusy(false)
      })
  }, [couponsClient, tenantId, pageNumber, pageSize])

  useEffect(() => {
    void load()
  }, [load])
  useEffect(() => () => controllerRef.current?.abort(), [])

  // ---- create form ----
  const {
    register,
    control,
    handleSubmit,
    reset: resetCreate,
    setError: setCreateError,
    clearErrors: clearCreateErrors,
    formState: { errors: createErrors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
    defaultValues: {
      code: '',
      discountType: 'Percentage',
      discountValue: '',
      minimumSubtotal: '',
      maximumDiscountAmount: '',
      redemptionLimit: '',
      expiresAtUtc: '',
    },
  })
  // `useWatch` (not render-phase `watch`) is the project idiom: it reads the
  // same react-hook-form store but is React-Compiler-memoizable, so the unit
  // label swaps without an `incompatible-library` lint warning.
  const createType = useWatch({ control, name: 'discountType' })

  const toggleButtonRef = useRef<HTMLButtonElement | null>(null)
  const codeInputRef = useRef<HTMLInputElement | null>(null)
  useEffect(() => {
    if (!formOpen) return
    const frame = requestAnimationFrame(() => codeInputRef.current?.focus())
    return () => cancelAnimationFrame(frame)
  }, [formOpen])

  const openCreate = useCallback(() => {
    resetCreate()
    clearCreateErrors()
    setBanner(null)
    setFormOpen(true)
  }, [resetCreate, clearCreateErrors])

  const closeCreate = useCallback(() => {
    setFormOpen(false)
    resetCreate()
    clearCreateErrors()
    setBanner(null)
    toggleButtonRef.current?.focus()
  }, [resetCreate, clearCreateErrors])

  const onCreateSubmit = useCallback(
    async (values: CreateFormValues) => {
      setCreateSubmitting(true)
      setBanner(null)
      // Persian digits are normalized to standard digits before validating /
      // submitting (step 13). An empty money field is its default (0 for the
      // required subtotal/discount, null for the optional cap/limit).
      const discountValue = parseMoney(values.discountValue)
      const minimumSubtotal = parseMoney(values.minimumSubtotal) ?? 0
      const maximumDiscountAmount = parseMoney(values.maximumDiscountAmount)
      const redemptionLimit = parseMoney(values.redemptionLimit)

      const dvErr =
        discountValue === null || Number.isNaN(discountValue)
          ? 'مقدار تخفیف الزامی است.'
          : discountValue <= 0
            ? 'مقدار تخفیف باید بزرگ‌تر از صفر باشد.'
            : values.discountType === 'Percentage' && discountValue > 100
              ? 'درصد تخفیف باید بین ۱ تا ۱۰۰ باشد.'
              : null
      const minErr = validateMoney(minimumSubtotal, { required: true, min: 0 })
      const capErr = validateMoney(maximumDiscountAmount, { min: 0 })
      const limitErr = validateMoney(redemptionLimit, { integer: true, min: 1, max: 1_000_000 })

      if (dvErr || minErr || capErr || limitErr) {
        if (dvErr) setCreateError('discountValue', { message: dvErr })
        if (minErr) setCreateError('minimumSubtotal', { message: minErr })
        if (capErr) setCreateError('maximumDiscountAmount', { message: capErr })
        if (limitErr) setCreateError('redemptionLimit', { message: limitErr })
        setCreateSubmitting(false)
        return
      }

      try {
        const created = await couponsClient.create(tenantId, {
          code: values.code.trim(),
          discountType: values.discountType,
          discountValue: discountValue as number,
          minimumSubtotal,
          maximumDiscountAmount,
          redemptionLimit,
          expiresAtUtc: values.expiresAtUtc ? new Date(values.expiresAtUtc).toISOString() : null,
        })
        setBanner({ tone: 'success', text: `کد تخفیف ${created.code} ایجاد شد.` })
        setFormOpen(false)
        resetCreate()
        void load()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof ShopClientError && error.problem.status === 400 && error.problem.errors) {
          for (const [field, messages] of Object.entries(error.problem.errors)) {
            const message = messages[0] ?? 'مقدار معتبر نیست.'
            if (['code', 'discountValue', 'minimumSubtotal', 'maximumDiscountAmount', 'redemptionLimit'].includes(field)) {
              setCreateError(field as keyof CreateFormValues, { message })
            }
          }
          codeInputRef.current?.focus()
        } else if (error instanceof ShopClientError && error.problem.status === 409) {
          setCreateError('code', { message: 'کد تخفیفی با همین کد از قبل وجود دارد.' })
          codeInputRef.current?.focus()
        } else {
          setBanner({ tone: 'error', text: error instanceof Error ? error.message : 'هم‌اکنون نمی‌توانیم کد تخفیف را ایجاد کنیم. دوباره تلاش کنید.' })
        }
      } finally {
        setCreateSubmitting(false)
      }
    },
    [couponsClient, tenantId, resetCreate, setCreateError, load],
  )

  // ---- edit form ----
  const editingCoupon = editingId ? coupons?.find((c) => c.id === editingId) ?? null : null
  const {
    register: registerEdit,
    handleSubmit: handleEditSubmit,
    reset: resetEdit,
    setError: setEditError,
    clearErrors: clearEditErrors,
    formState: { errors: editErrors },
  } = useForm<EditFormValues>({
    resolver: zodResolver(editSchema),
    defaultValues: {
      discountValue: '',
      minimumSubtotal: '',
      maximumDiscountAmount: '',
      redemptionLimit: '',
      expiresAtUtc: '',
      isActive: true,
    },
  })

  useEffect(() => {
    if (!editingId) return
    const target = coupons?.find((c) => c.id === editingId)
    if (target) {
      resetEdit({
        discountValue: String(target.discountValue),
        minimumSubtotal: String(target.minimumSubtotal),
        maximumDiscountAmount: target.maximumDiscountAmount === null ? '' : String(target.maximumDiscountAmount),
        redemptionLimit: target.redemptionLimit === null ? '' : String(target.redemptionLimit),
        expiresAtUtc: isoToDateInput(target.expiresAtUtc),
        isActive: target.isActive,
      })
    }
    clearEditErrors()
    setEditConflict(false)
  }, [editingId, coupons, resetEdit, clearEditErrors])

  const closeEdit = useCallback(() => {
    setEditingId(null)
    resetEdit()
    clearEditErrors()
    setEditConflict(false)
    setBanner(null)
  }, [resetEdit, clearEditErrors])

  const onEditSubmit = useCallback(
    async (values: EditFormValues) => {
      if (!editingCoupon) return
      setEditSubmitting(true)
      setEditConflict(false)
      setBanner(null)
      // Persian digits normalized to standard digits before validating/submitting.
      const discountValue = parseMoney(values.discountValue)
      const minimumSubtotal = parseMoney(values.minimumSubtotal) ?? 0
      const maximumDiscountAmount = parseMoney(values.maximumDiscountAmount)
      const redemptionLimit = parseMoney(values.redemptionLimit)

      const dvErr =
        discountValue === null || Number.isNaN(discountValue)
          ? 'مقدار تخفیف الزامی است.'
          : discountValue <= 0
            ? 'مقدار تخفیف باید بزرگ‌تر از صفر باشد.'
            : editingCoupon.discountType === 'Percentage' && discountValue > 100
              ? 'درصد تخفیف باید بین ۱ تا ۱۰۰ باشد.'
              : null
      const minErr = validateMoney(minimumSubtotal, { required: true, min: 0 })
      const capErr = validateMoney(maximumDiscountAmount, { min: 0 })
      // B041: the new limit can never be set below the current redeemed count.
      const limitErr =
        validateMoney(redemptionLimit, { integer: true, min: 1, max: 1_000_000 }) ??
        (redemptionLimit !== null && redemptionLimit < editingCoupon.redeemedCount
          ? 'سقف استفاده نمی‌تواند کمتر از تعداد استفاده‌ی فعلی باشد.'
          : null)

      if (dvErr || minErr || capErr || limitErr) {
        if (dvErr) setEditError('discountValue', { message: dvErr })
        if (minErr) setEditError('minimumSubtotal', { message: minErr })
        if (capErr) setEditError('maximumDiscountAmount', { message: capErr })
        if (limitErr) setEditError('redemptionLimit', { message: limitErr })
        setEditSubmitting(false)
        return
      }

      try {
        await couponsClient.update(tenantId, editingCoupon.id, {
          discountValue: discountValue as number,
          minimumSubtotal,
          maximumDiscountAmount,
          redemptionLimit,
          expiresAtUtc: values.expiresAtUtc ? new Date(values.expiresAtUtc).toISOString() : null,
          isActive: values.isActive,
          expectedVersion: editingCoupon.version,
        })
        setBanner({ tone: 'success', text: `کد تخفیف ${editingCoupon.code} به‌روزرسانی شد.` })
        closeEdit()
        void load()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
        } else if (error instanceof ShopClientError && error.problem.status === 409) {
          // Stale expectedVersion — the exact B041 conflict state.
          setEditConflict(true)
        } else if (error instanceof ShopClientError && error.problem.status === 404) {
          setBanner({ tone: 'error', text: 'کد تخفیف یافت نشد. فهرست را بارگذاری کنید.' })
          closeEdit()
        } else if (error instanceof ShopClientError && error.problem.status === 400 && error.problem.errors) {
          for (const [field, messages] of Object.entries(error.problem.errors)) {
            if (['discountValue', 'minimumSubtotal', 'maximumDiscountAmount', 'redemptionLimit'].includes(field)) {
              setEditError(field as keyof EditFormValues, { message: messages[0] ?? 'مقدار معتبر نیست.' })
            }
          }
        } else {
          setBanner({ tone: 'error', text: error instanceof Error ? error.message : 'هم‌اکنون نمی‌توانیم تغییرات را ذخیره کنیم. دوباره تلاش کنید.' })
        }
      } finally {
        setEditSubmitting(false)
      }
    },
    [couponsClient, tenantId, editingCoupon, closeEdit, load, setEditError],
  )

  // ---- deactivate ----
  const onDeactivate = useCallback(
    async (couponId: string, code: string) => {
      setDeactivatingId(couponId)
      setBanner(null)
      try {
        const updated = await couponsClient.deactivate(tenantId, couponId)
        setBanner({ tone: 'success', text: `کد تخفیف ${updated.code} غیرفعال شد.` })
        void load()
      } catch (error) {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setBanner({
          tone: 'error',
          text:
            error instanceof ShopClientError && error.problem.status === 404
              ? `کد تخفیف ${code} یافت نشد. فهرست را بارگذاری کنید.`
              : `هم‌اکنون نمی‌توانیم ${code} را غیرفعال کنیم. دوباره تلاش کنید.`,
        })
      } finally {
        setDeactivatingId(null)
      }
    },
    [couponsClient, tenantId, load],
  )

  const isLoading = coupons === null && isBusy
  const isRefreshing = coupons !== null && isBusy
  const listError = coupons === null && !isBusy && listFailure !== null
  const isEmpty = coupons !== null && coupons.length === 0
  const codeField = register('code')

  return (
    <DashboardShell>
      <section aria-label="کدهای تخفیف" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">کدهای تخفیف</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              کدهای تخفیف را با حداقل جمع، سقف تخفیف و سقف استفاده مدیریت کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            {coupons !== null && (
              <SecondaryButton
                type="button"
                aria-label="به‌روزرسانی فهرست کدهای تخفیف"
                className="px-3"
                disabled={isBusy}
                onClick={() => void load()}
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
              aria-controls="coupon-form"
              disabled={createSubmitting}
              onClick={() => (formOpen ? closeCreate() : openCreate())}
            >
              {formOpen ? (
                <span className="text-sm font-semibold">بستن</span>
              ) : (
                <>
                  <BadgePercent aria-hidden="true" className="me-2 size-4" />
                  ایجاد کد تخفیف
                </>
              )}
            </Button>
          </div>
        </div>

        {banner && (
          <div
            role={banner.tone === 'error' ? 'alert' : 'status'}
            className={cn(
              'flex items-center gap-2 rounded-lg border px-4 py-2 text-sm font-medium',
              banner.tone === 'error'
                ? 'border-destructive/40 bg-destructive/10 text-destructive'
                : 'border-success/40 bg-success/10 text-success',
            )}
          >
            {banner.tone === 'error' ? (
              <TriangleAlert aria-hidden="true" className="size-4" />
            ) : (
              <CircleCheck aria-hidden="true" className="size-4" />
            )}
            {banner.text}
          </div>
        )}

        {formOpen && (
          <form
            id="coupon-form"
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleSubmit(onCreateSubmit)(event)}
            noValidate
          >
            <h3 className="text-base font-semibold">ایجاد کد تخفیف جدید</h3>
            <p className="mt-1 text-sm text-muted-foreground">کد تخفیف با وضعیت «فعال» و نسخه‌ی ۱ ایجاد می‌شود.</p>

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-code">کد</label>
                <TextInput
                  id="coupon-code"
                  autoComplete="off"
                  dir="ltr"
                  className="text-start"
                  aria-invalid={Boolean(createErrors.code)}
                  aria-describedby={createErrors.code ? 'coupon-code-error' : undefined}
                  {...codeField}
                  ref={(node) => {
                    codeField.ref(node)
                    codeInputRef.current = node
                  }}
                />
                {createErrors.code && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-code-error">{createErrors.code.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-discount-type">نوع تخفیف</label>
                <select
                  id="coupon-discount-type"
                  {...register('discountType')}
                  className="min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-sm text-foreground focus-visible:border-ring"
                >
                  <option value="Percentage">درصدی</option>
                  <option value="FixedAmount">مقدار ثابت</option>
                </select>
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-discount-value">
                  مقدار تخفیف ({createType === 'Percentage' ? 'درصد' : 'تومان'})
                </label>
                <TextInput
                  id="coupon-discount-value"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(createErrors.discountValue)}
                  aria-describedby={createErrors.discountValue ? 'coupon-discount-value-error' : undefined}
                  {...register('discountValue')}
                />
                {createErrors.discountValue && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-discount-value-error">{createErrors.discountValue.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-min-subtotal">حداقل جمع سبد (تومان)</label>
                <TextInput
                  id="coupon-min-subtotal"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(createErrors.minimumSubtotal)}
                  aria-describedby={createErrors.minimumSubtotal ? 'coupon-min-subtotal-error' : undefined}
                  {...register('minimumSubtotal')}
                />
                {createErrors.minimumSubtotal && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-min-subtotal-error">{createErrors.minimumSubtotal.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-max-discount">سقف تخفیف (تومان — خالی = نامحدود)</label>
                <TextInput
                  id="coupon-max-discount"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(createErrors.maximumDiscountAmount)}
                  aria-describedby={createErrors.maximumDiscountAmount ? 'coupon-max-discount-error' : undefined}
                  {...register('maximumDiscountAmount')}
                />
                {createErrors.maximumDiscountAmount && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-max-discount-error">{createErrors.maximumDiscountAmount.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-redemption-limit">سقف استفاده (تعداد — خالی = نامحدود)</label>
                <TextInput
                  id="coupon-redemption-limit"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(createErrors.redemptionLimit)}
                  aria-describedby={createErrors.redemptionLimit ? 'coupon-redemption-limit-error' : undefined}
                  {...register('redemptionLimit')}
                />
                {createErrors.redemptionLimit && (
                  <p className="mt-2 text-sm text-destructive" id="coupon-redemption-limit-error">{createErrors.redemptionLimit.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="coupon-expires">تاریخ انقضا (اختیاری)</label>
                <TextInput id="coupon-expires" type="date" {...register('expiresAtUtc')} />
              </div>
            </div>

            <div className="mt-5 flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={createSubmitting}>
                {createSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ذخیره
                  </>
                ) : (
                  'ایجاد کد تخفیف'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeCreate} disabled={createSubmitting}>لغو</SecondaryButton>
            </div>
          </form>
        )}

        {isLoading && <CouponsSkeleton />}

        {listError && (
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
            title={listFailure === 'forbidden' ? 'دسترسی مدیریت فروشگاه مجاز نیست' : 'فهرست کدهای تخفیف در دسترس نیست'}
            description={
              listFailure === 'forbidden'
                ? 'حساب فعلی مجوز مدیریت کدهای تخفیف این مستأجر را ندارد.'
                : 'هم‌اکنون نمی‌توانیم کدهای تخفیف را بارگذاری کنیم. اتصال را بررسی کنید و دوباره تلاش کنید.'
            }
            action={
              <Button type="button" className="mt-3 min-w-32" onClick={() => void load()}>
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش دوباره
              </Button>
            }
          />
        )}

        {isEmpty && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <span className="mx-auto inline-flex size-12 items-center justify-center rounded-lg bg-muted text-muted-foreground">
              <BadgePercent aria-hidden="true" className="size-6" />
            </span>
            <p className="mt-3 text-sm font-semibold">کد تخفیفی موجود نیست</p>
            <p className="mt-1 text-sm text-muted-foreground">نخستین کد تخفیف را با دکمه «ایجاد کد تخفیف» بسازید.</p>
          </div>
        )}

        {coupons !== null && coupons.length > 0 && (
          <>
            {/* Desktop / tablet table */}
            <div className="hidden overflow-hidden rounded-xl border border-border bg-surface shadow-soft md:block">
              <div className="overflow-x-auto">
                <table className="w-full min-w-[64rem] text-sm">
                  <caption className="sr-only">فهرست کدهای تخفیف</caption>
                  <thead>
                    <tr className="border-b border-border text-start">
                      <th scope="col" className="px-4 py-3 text-start font-semibold">کد</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">نوع</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">مقدار</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">حداقل جمع</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">سقف تخفیف</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">استفاده</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">انقضا</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                      <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                    </tr>
                  </thead>
                  <tbody>
                    {coupons.map((coupon) => (
                      <CouponRow
                        key={coupon.id}
                        coupon={coupon}
                        isDeactivating={deactivatingId === coupon.id}
                        onEdit={() => {
                          setEditingId(coupon.id)
                          setBanner(null)
                        }}
                        onDeactivate={() => void onDeactivate(coupon.id, coupon.code)}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
              {pagination && (
                <PaginationControls
                  meta={pagination}
                  label="کدهای تخفیف"
                  disabled={isRefreshing}
                  onPageChange={setPageNumber}
                  onPageSizeChange={(size) => {
                    setPageSize(size)
                    setPageNumber(1)
                  }}
                />
              )}
            </div>

            {/* Mobile cards */}
            <ul className="space-y-3 md:hidden" aria-label="فهرست کدهای تخفیف">
              {coupons.map((coupon) => (
                <CouponCard
                  key={coupon.id}
                  coupon={coupon}
                  isDeactivating={deactivatingId === coupon.id}
                  onEdit={() => {
                    setEditingId(coupon.id)
                    setBanner(null)
                  }}
                  onDeactivate={() => void onDeactivate(coupon.id, coupon.code)}
                />
              ))}
            </ul>
          </>
        )}

        {editingCoupon && (
          <form
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={(event) => void handleEditSubmit(onEditSubmit)(event)}
            noValidate
            aria-label={`ویرایش کد تخفیف ${editingCoupon.code}`}
          >
            <div className="flex items-center justify-between">
              <h3 className="text-base font-semibold">
                ویرایش کد <bdi dir="ltr">{editingCoupon.code}</bdi>
              </h3>
              <span className="text-xs text-muted-foreground">نسخه‌ی فعلی: {editingCoupon.version.toLocaleString('fa-IR')}</span>
            </div>

            {editingCoupon.redeemedCount > 0 && (
              <p className="mt-2 flex items-center gap-1.5 rounded-md bg-muted px-3 py-2 text-xs font-medium text-muted-foreground">
                <Lock aria-hidden="true" className="size-3.5" />
                این کد تخفیف استفاده شده؛ کد و نوع تخفیف قفل هستند و قابل تغییر نیستند.
              </p>
            )}

            {editConflict && (
              <div
                role="alert"
                className="mt-3 flex items-start gap-2 rounded-lg border border-warning/40 bg-warning/10 px-4 py-3 text-sm font-medium text-warning"
              >
                <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
                <span>
                  این کد تخفیف پیش از اعمال تغییرات شما به‌روزرسانی شده است. فهرست را بارگذاری کنید و دوباره تلاش کنید.
                </span>
              </div>
            )}

            <div className="mt-4 grid gap-4 md:grid-cols-2">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-code">
                  کد {editingCoupon.redeemedCount > 0 ? '(قفل)' : ''}
                </label>
                <TextInput id="edit-code" dir="ltr" className="text-start" value={editingCoupon.code} disabled />
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-type">
                  نوع تخفیف {editingCoupon.redeemedCount > 0 ? '(قفل)' : ''}
                </label>
                <TextInput id="edit-type" value={editingCoupon.discountType === 'Percentage' ? 'درصدی' : 'مقدار ثابت'} disabled />
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-discount-value">
                  مقدار تخفیف ({editingCoupon.discountType === 'Percentage' ? 'درصد' : 'تومان'})
                </label>
                <TextInput
                  id="edit-discount-value"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(editErrors.discountValue)}
                  aria-describedby={editErrors.discountValue ? 'edit-discount-value-error' : undefined}
                  {...registerEdit('discountValue')}
                />
                {editErrors.discountValue && (
                  <p className="mt-2 text-sm text-destructive" id="edit-discount-value-error">{editErrors.discountValue.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-min-subtotal">حداقل جمع سبد (تومان)</label>
                <TextInput
                  id="edit-min-subtotal"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(editErrors.minimumSubtotal)}
                  aria-describedby={editErrors.minimumSubtotal ? 'edit-min-subtotal-error' : undefined}
                  {...registerEdit('minimumSubtotal')}
                />
                {editErrors.minimumSubtotal && (
                  <p className="mt-2 text-sm text-destructive" id="edit-min-subtotal-error">{editErrors.minimumSubtotal.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-max-discount">سقف تخفیف (تومان — خالی = نامحدود)</label>
                <TextInput
                  id="edit-max-discount"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(editErrors.maximumDiscountAmount)}
                  aria-describedby={editErrors.maximumDiscountAmount ? 'edit-max-discount-error' : undefined}
                  {...registerEdit('maximumDiscountAmount')}
                />
                {editErrors.maximumDiscountAmount && (
                  <p className="mt-2 text-sm text-destructive" id="edit-max-discount-error">{editErrors.maximumDiscountAmount.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-redemption-limit">سقف استفاده (تعداد — خالی = نامحدود)</label>
                <TextInput
                  id="edit-redemption-limit"
                  type="text"
                  inputMode="numeric"
                  aria-invalid={Boolean(editErrors.redemptionLimit)}
                  aria-describedby={editErrors.redemptionLimit ? 'edit-redemption-limit-error' : undefined}
                  {...registerEdit('redemptionLimit')}
                />
                {editErrors.redemptionLimit && (
                  <p className="mt-2 text-sm text-destructive" id="edit-redemption-limit-error">{editErrors.redemptionLimit.message}</p>
                )}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="edit-expires">تاریخ انقضا (اختیاری)</label>
                <TextInput id="edit-expires" type="date" {...registerEdit('expiresAtUtc')} />
              </div>
              <div className="flex items-end">
                <label className="flex items-center gap-2 text-sm font-semibold">
                  <input
                    type="checkbox"
                    {...registerEdit('isActive')}
                    className="size-4 rounded border-input"
                  />
                  فعال
                </label>
              </div>
            </div>

            <div className="mt-5 flex flex-wrap items-center gap-2">
              <Button type="submit" disabled={editSubmitting}>
                {editSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" />
                    در حال ذخیره
                  </>
                ) : (
                  'ذخیره‌ی تغییرات'
                )}
              </Button>
              <SecondaryButton type="button" onClick={closeEdit} disabled={editSubmitting}>لغو</SecondaryButton>
            </div>
          </form>
        )}
      </section>
    </DashboardShell>
  )
}

// ---- shared display pieces ----

function formatMoney(value: number): string {
  return value.toLocaleString('fa-IR')
}

function discountLabel(coupon: Coupon): string {
  return coupon.discountType === 'Percentage'
    ? `${coupon.discountValue.toLocaleString('fa-IR')}٪`
    : `${formatMoney(coupon.discountValue)} تومان`
}

/** Three distinct, honest states: active, inactive, expired (active but past). */
function statusOf(coupon: Coupon): 'active' | 'inactive' | 'expired' {
  if (!coupon.isActive) return 'inactive'
  if (coupon.expiresAtUtc !== null && Date.parse(coupon.expiresAtUtc) < Date.now()) return 'expired'
  return 'active'
}

function StatusBadge({ coupon }: { coupon: Coupon }) {
  const status = statusOf(coupon)
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        status === 'active' && 'bg-success/10 text-success',
        status === 'inactive' && 'bg-muted text-muted-foreground',
        status === 'expired' && 'bg-warning/10 text-warning',
      )}
    >
      <span
        aria-hidden="true"
        className={cn(
          'size-1.5 rounded-full',
          status === 'active' && 'bg-success',
          status === 'inactive' && 'bg-muted-foreground',
          status === 'expired' && 'bg-warning',
        )}
      />
      {status === 'active' ? 'فعال' : status === 'inactive' ? 'غیرفعال' : 'منقضی'}
    </span>
  )
}

/** Usage: `redeemedCount` vs `redemptionLimit`; `null` limit is «نامحدود»; exhausted is highlighted. */
function UsageText({ coupon }: { coupon: Coupon }) {
  if (coupon.redemptionLimit === null) {
    return <span>{coupon.redeemedCount.toLocaleString('fa-IR')} استفاده — نامحدود</span>
  }
  const exhausted = coupon.redeemedCount >= coupon.redemptionLimit
  return (
    <span className={cn(exhausted && 'font-semibold text-warning')}>
      {coupon.redeemedCount.toLocaleString('fa-IR')} از {coupon.redemptionLimit.toLocaleString('fa-IR')}
      {exhausted ? ' — سقف رسیده' : ''}
    </span>
  )
}

function CouponRow({
  coupon,
  isDeactivating,
  onEdit,
  onDeactivate,
}: {
  coupon: Coupon
  isDeactivating: boolean
  onEdit: () => void
  onDeactivate: () => void
}) {
  return (
    <tr className="border-b border-border last:border-b-0">
      <td className="px-4 py-3"><bdi dir="ltr" className="font-medium">{coupon.code}</bdi></td>
      <td className="px-4 py-3 text-muted-foreground">{coupon.discountType === 'Percentage' ? 'درصدی' : 'مقدار ثابت'}</td>
      <td className="px-4 py-3 text-muted-foreground">{discountLabel(coupon)}</td>
      <td className="px-4 py-3 text-muted-foreground">{formatMoney(coupon.minimumSubtotal)} تومان</td>
      <td className="px-4 py-3 text-muted-foreground">
        {coupon.maximumDiscountAmount === null ? 'نامحدود' : `${formatMoney(coupon.maximumDiscountAmount)} تومان`}
      </td>
      <td className="px-4 py-3 text-muted-foreground"><UsageText coupon={coupon} /></td>
      <td className="px-4 py-3 text-muted-foreground">
        {coupon.expiresAtUtc ? <bdi dir="ltr">{new Date(coupon.expiresAtUtc).toLocaleDateString('fa-IR')}</bdi> : 'بدون انقضا'}
      </td>
      <td className="px-4 py-3"><StatusBadge coupon={coupon} /></td>
      <td className="px-4 py-3">
        <div className="flex items-center gap-2">
          <SecondaryButton type="button" className="px-2.5" onClick={onEdit} aria-label={`ویرایش ${coupon.code}`}>
            <Pencil aria-hidden="true" className="size-4" />
            <span className="hidden lg:inline">ویرایش</span>
          </SecondaryButton>
          <SecondaryButton
            type="button"
            className="px-2.5"
            disabled={!coupon.isActive || isDeactivating}
            onClick={onDeactivate}
            aria-label={`غیرفعال‌سازی ${coupon.code}`}
          >
            {isDeactivating ? (
              <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
            ) : (
              'غیرفعال‌سازی'
            )}
          </SecondaryButton>
        </div>
      </td>
    </tr>
  )
}

function CouponCard({
  coupon,
  isDeactivating,
  onEdit,
  onDeactivate,
}: {
  coupon: Coupon
  isDeactivating: boolean
  onEdit: () => void
  onDeactivate: () => void
}) {
  return (
    <li className="rounded-xl border border-border bg-surface p-4 shadow-soft">
      <div className="flex items-center justify-between gap-2">
        <bdi dir="ltr" className="text-sm font-semibold">{coupon.code}</bdi>
        <StatusBadge coupon={coupon} />
      </div>
      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <div><dt className="text-muted-foreground">نوع</dt><dd>{coupon.discountType === 'Percentage' ? 'درصدی' : 'مقدار ثابت'}</dd></div>
        <div><dt className="text-muted-foreground">مقدار</dt><dd>{discountLabel(coupon)}</dd></div>
        <div><dt className="text-muted-foreground">حداقل جمع</dt><dd>{formatMoney(coupon.minimumSubtotal)} تومان</dd></div>
        <div>
          <dt className="text-muted-foreground">سقف تخفیف</dt>
          <dd>{coupon.maximumDiscountAmount === null ? 'نامحدود' : `${formatMoney(coupon.maximumDiscountAmount)} تومان`}</dd>
        </div>
        <div><dt className="text-muted-foreground">استفاده</dt><dd><UsageText coupon={coupon} /></dd></div>
        <div>
          <dt className="text-muted-foreground">انقضا</dt>
          <dd>{coupon.expiresAtUtc ? <bdi dir="ltr">{new Date(coupon.expiresAtUtc).toLocaleDateString('fa-IR')}</bdi> : 'بدون انقضا'}</dd>
        </div>
      </dl>
      <div className="mt-4 flex items-center gap-2">
        <SecondaryButton type="button" className="px-3" onClick={onEdit} aria-label={`ویرایش ${coupon.code}`}>
          <Pencil aria-hidden="true" className="size-4" />
          ویرایش
        </SecondaryButton>
        <SecondaryButton
          type="button"
          className="px-3"
          disabled={!coupon.isActive || isDeactivating}
          onClick={onDeactivate}
          aria-label={`غیرفعال‌سازی ${coupon.code}`}
        >
          {isDeactivating ? (
            <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
          ) : (
            'غیرفعال‌سازی'
          )}
        </SecondaryButton>
      </div>
    </li>
  )
}

/** Loading placeholder with the table's footprint so data causes no layout shift. */
function CouponsSkeleton() {
  return (
    <div
      aria-busy="true"
      className="hidden overflow-hidden rounded-xl border border-border bg-surface shadow-soft md:block"
    >
      <p className="sr-only">در حال بارگذاری فهرست کدهای تخفیف<span aria-hidden="true">…</span></p>
      <div className="border-b border-border px-4 py-3">
        <div className="h-4 w-40 animate-pulse rounded bg-muted motion-reduce:animate-none" />
      </div>
      {[0, 1, 2].map((index) => (
        <div key={index} className="flex items-center gap-4 border-b border-border px-4 py-4 last:border-b-0">
          <div className="h-4 w-24 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-20 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-16 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-5 w-16 animate-pulse rounded-full bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}
