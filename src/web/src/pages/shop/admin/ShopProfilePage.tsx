import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { zodResolver } from '@hookform/resolvers/zod'
import { useForm, useWatch, type FieldErrors, type Path, type UseFormRegister } from 'react-hook-form'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import type { SaveShopProfileRequest, ShopProfile } from '@/features/shop/contracts/shopProfileContract'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { useAuth } from '@/features/auth/AuthContext'
import { cn } from '@/lib/utils'
import { Camera, CircleCheck, Loader2, Phone, RefreshCw, TriangleAlert } from 'lucide-react'

/**
 * F047: Shop profile settings page (S35).
 *
 * Plain-text settings form covering every field of `SaveShopProfileRequest`:
 * name, tagline, supportPhone, instagramUrl, aboutText, shippingPolicy,
 * paymentPolicy, returnPolicy, privacyPolicy and the `isPublished` toggle.
 * Live character counters, a live storefront header/footer preview and the
 * B039 states: null-first setup (admin GET → null), success, 400 field
 * validation, 403, 409 stale version and unavailable-with-retry.
 *
 * The data source is the `profile` slot of `ShopClientsProvider` — the
 * deterministic B039 mock now, the HTTP client after F057. This page never
 * imports fixtures and never calls `fetch`.
 */

const FIELD_LIMITS = {
  name: 100,
  tagline: 180,
  supportPhone: 30,
  instagramUrl: 300,
  aboutText: 4000,
  shippingPolicy: 6000,
  paymentPolicy: 6000,
  returnPolicy: 6000,
  privacyPolicy: 6000,
} as const

const profileSchema = z.object({
  name: z.string().min(1, 'نام فروشگاه الزامی است.').max(FIELD_LIMITS.name, `حداکثر ${FIELD_LIMITS.name} کاراکتر`),
  tagline: z.string().max(FIELD_LIMITS.tagline, `حداکثر ${FIELD_LIMITS.tagline} کاراکتر`),
  supportPhone: z
    .string()
    .max(FIELD_LIMITS.supportPhone, `حداکثر ${FIELD_LIMITS.supportPhone} کاراکتر`)
    .refine((v) => !v || /^[\d\s+\-()۰-۹]+$/.test(v), 'فقط ارقام، فاصله، +، - و پرانتز مجاز است.'),
  instagramUrl: z
    .string()
    .max(FIELD_LIMITS.instagramUrl, `حداکثر ${FIELD_LIMITS.instagramUrl} کاراکتر`)
    .refine(
      (v) => !v || /^https:\/\/([a-z0-9-]+\.)?instagram\.com(\/[\w\-./?#=&%@+,:;~!]*)?$/i.test(v),
      'آدرس باید HTTPS و دامنه instagram.com باشد.'
    ),
  aboutText: z.string().max(FIELD_LIMITS.aboutText, `حداکثر ${FIELD_LIMITS.aboutText} کاراکتر`),
  shippingPolicy: z.string().max(FIELD_LIMITS.shippingPolicy, `حداکثر ${FIELD_LIMITS.shippingPolicy} کاراکتر`),
  paymentPolicy: z.string().max(FIELD_LIMITS.paymentPolicy, `حداکثر ${FIELD_LIMITS.paymentPolicy} کاراکتر`),
  returnPolicy: z.string().max(FIELD_LIMITS.returnPolicy, `حداکثر ${FIELD_LIMITS.returnPolicy} کاراکتر`),
  privacyPolicy: z.string().max(FIELD_LIMITS.privacyPolicy, `حداکثر ${FIELD_LIMITS.privacyPolicy} کاراکتر`),
  isPublished: z.boolean(),
})

type ProfileFormValues = z.infer<typeof profileSchema>
type TextFieldName = Exclude<keyof ProfileFormValues, 'isPublished'>

const EMPTY_VALUES: ProfileFormValues = {
  name: '',
  tagline: '',
  supportPhone: '',
  instagramUrl: '',
  aboutText: '',
  shippingPolicy: '',
  paymentPolicy: '',
  returnPolicy: '',
  privacyPolicy: '',
  isPublished: false,
}

function toFormValues(profile: ShopProfile): ProfileFormValues {
  return {
    name: profile.name,
    tagline: profile.tagline,
    supportPhone: profile.supportPhone,
    instagramUrl: profile.instagramUrl ?? '',
    aboutText: profile.aboutText,
    shippingPolicy: profile.shippingPolicy,
    paymentPolicy: profile.paymentPolicy,
    returnPolicy: profile.returnPolicy,
    privacyPolicy: profile.privacyPolicy,
    isPublished: profile.isPublished,
  }
}

/** True when the client rejected with an AbortError (superseded request). */
function isAbortError(error: unknown): boolean {
  return error instanceof DOMException && error.name === 'AbortError'
}

/** Live counter: `<n> / <limit>` plus a "remaining" hint near the limit. */
function CharacterCounter({ length, limit }: { length: number; limit: number }) {
  const remaining = limit - length
  return (
    <span
      className={cn(
        'text-xs',
        remaining < 0
          ? 'font-semibold text-destructive'
          : remaining <= Math.max(10, limit * 0.05)
            ? 'font-medium text-warning'
            : 'text-muted-foreground'
      )}
      aria-live="polite"
    >
      {length.toLocaleString('fa-IR')} از {limit.toLocaleString('fa-IR')} کاراکتر
    </span>
  )
}

interface ProfileFieldProps {
  name: TextFieldName
  label: string
  limit: number
  placeholder?: string
  rows?: number
  dirLtr?: boolean
  register: UseFormRegister<ProfileFormValues>
  value: string
  error?: FieldErrors<ProfileFormValues>[TextFieldName]
}

/** One labelled plain-text field with a live character counter. */
function ProfileField({
  name,
  label,
  limit,
  placeholder,
  rows,
  dirLtr,
  register,
  value,
  error,
}: ProfileFieldProps) {
  const id = `profile-${name}`
  const descriptionId = `${id}-counter`
  const hasError = Boolean(error?.message)
  const inputClasses = cn(
    'w-full rounded-md border bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors placeholder:text-muted-foreground focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm',
    hasError && 'border-destructive'
  )

  return (
    <div className="space-y-1.5">
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      {rows !== undefined ? (
        <textarea
          id={id}
          rows={rows}
          placeholder={placeholder}
          aria-describedby={descriptionId}
          aria-invalid={hasError || undefined}
          className={cn(inputClasses, 'min-h-[84px] resize-y')}
          {...register(name)}
        />
      ) : (
        <input
          id={id}
          placeholder={placeholder}
          dir={dirLtr ? 'ltr' : undefined}
          aria-describedby={descriptionId}
          aria-invalid={hasError || undefined}
          className={cn(inputClasses, 'min-h-11 text-start')}
          {...register(name)}
        />
      )}
      <div id={descriptionId} className="flex items-center justify-between gap-2">
        {hasError ? (
          <p className="text-xs font-medium text-destructive">{error?.message}</p>
        ) : (
          <span aria-hidden="true" />
        )}
        <CharacterCounter length={value.length} limit={limit} />
      </div>
    </div>
  )
}

interface StorePreviewProps {
  name: string
  tagline: string
  supportPhone: string
  instagramUrl: string
  isPublished: boolean
}

/** Live preview of how the storefront header and footer will look. */
function StorePreview({ name, tagline, supportPhone, instagramUrl, isPublished }: StorePreviewProps) {
  const displayName = name.trim() || 'نام فروشگاه'
  const displayTagline = tagline.trim()
  const displayPhone = supportPhone.trim()
  const displayInstagram = instagramUrl.trim()

  if (!isPublished) {
    return (
      <div className="space-y-2 rounded-lg border border-border bg-surface p-4">
        <h3 className="text-sm font-semibold">پیش‌نمایش فروشگاه</h3>
        <div className="rounded-md border border-dashed border-border bg-muted/40 p-4 text-center">
          <p className="text-sm font-medium">این فروشگاه هنوز آماده‌سازی نشده است.</p>
          <p className="mt-1 text-xs text-muted-foreground">بعد از انتشار، این بخش ظاهر فروشگاه را نشان می‌دهد.</p>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-2 rounded-lg border border-border bg-surface p-4">
      <h3 className="text-sm font-semibold">پیش‌نمایش فروشگاه</h3>
      {/* header */}
      <div className="rounded-md border border-border bg-muted/40 p-3">
        <p className="text-base font-bold leading-snug">{displayName}</p>
        {displayTagline && <p className="mt-0.5 text-sm text-muted-foreground">{displayTagline}</p>}
      </div>
      {/* footer */}
      <div className="space-y-2 rounded-md border border-border bg-muted/20 p-3 text-xs">
        <p className="font-medium">{displayName}</p>
        {displayTagline && <p className="text-muted-foreground">{displayTagline}</p>}
        <ul className="space-y-1 text-muted-foreground">
          {displayPhone && (
            <li className="flex items-center gap-1.5">
              <Phone aria-hidden="true" className="size-3.5 shrink-0" />
              <span dir="ltr">{displayPhone}</span>
            </li>
          )}
          {displayInstagram && (
            <li className="flex items-center gap-1.5">
              <Camera aria-hidden="true" className="size-3.5 shrink-0" />
              <span dir="ltr">instagram.com</span>
            </li>
          )}
        </ul>
      </div>
    </div>
  )
}

export function ShopProfilePage() {
  const { signOut } = useAuth()
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const { profile: profileClient } = useShopClients()

  const [profile, setProfile] = useState<ShopProfile | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [loadFailure, setLoadFailure] = useState<'unavailable' | 'forbidden' | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [submitFailure, setSubmitFailure] = useState<{
    kind: 'conflict' | 'forbidden' | 'unavailable'
  } | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  const signOutRef = useRef(signOut)
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const loadAbortRef = useRef<AbortController | null>(null)

  const handleLoadError = useCallback((error: unknown) => {
    if (isAbortError(error)) return
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    if (error instanceof ShopClientError && error.problem.status === 403) {
      setLoadFailure('forbidden')
      return
    }
    setLoadFailure('unavailable')
  }, [])

  const loadProfile = useCallback(() => {
    loadAbortRef.current?.abort()
    const controller = new AbortController()
    loadAbortRef.current = controller

    setIsLoading(true)
    setLoadFailure(null)

    profileClient
      .getAdmin(tenantId, controller.signal)
      .then((result) => {
        setProfile(result)
        setSuccess(null)
        setIsLoading(false)
      })
      .catch((error: unknown) => {
        handleLoadError(error)
        setIsLoading(false)
      })
  }, [tenantId, handleLoadError, profileClient])

  useEffect(() => {
    loadProfile()
    return () => loadAbortRef.current?.abort()
  }, [loadProfile])

  const form = useForm<ProfileFormValues>({
    resolver: zodResolver(profileSchema),
    defaultValues: EMPTY_VALUES,
  })

  const register = form.register
  const fieldErrors = form.formState.errors
  // `useWatch` (not `form.watch`) is the react-hook-form subscription that the
  // React Compiler can memoize; it re-renders only when a watched field changes.
  const values = useWatch({ control: form.control }) as ProfileFormValues
  const isPublished = values.isPublished

  // Populate (or clear) the form whenever the loaded profile changes.
  useEffect(() => {
    form.reset(profile ? toFormValues(profile) : EMPTY_VALUES)
  }, [profile, form])

  const onSubmit = useCallback(
    async (values: ProfileFormValues) => {
      setIsSubmitting(true)
      setSubmitFailure(null)
      setSuccess(null)

      const controller = new AbortController()
      const body: SaveShopProfileRequest = {
        ...values,
        instagramUrl: values.instagramUrl.trim() === '' ? null : values.instagramUrl,
        expectedVersion: profile ? profile.version : null,
      }

      try {
        const saved = await profileClient.save(tenantId, body, controller.signal)
        setProfile(saved)
        setSuccess('تنظیمات با موفقیت ذخیره شد.')
      } catch (error) {
        if (isAbortError(error)) return
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        if (error instanceof ShopClientError) {
          const problem = error.problem
          if (problem.status === 409 && problem.type === 'stale_version') {
            setSubmitFailure({ kind: 'conflict' })
          } else if (problem.status === 403) {
            setSubmitFailure({ kind: 'forbidden' })
          } else if (problem.status === 400 && problem.errors) {
            Object.entries(problem.errors).forEach(([field, messages]) => {
              if (field in values) {
                form.setError(field as Path<ProfileFormValues>, {
                  type: 'server',
                  message: messages.join(' '),
                })
              }
            })
          } else {
            setSubmitFailure({ kind: 'unavailable' })
          }
          return
        }
        setSubmitFailure({ kind: 'unavailable' })
      } finally {
        setIsSubmitting(false)
      }
    },
    [profile, tenantId, form, profileClient]
  )

  if (isLoading) {
    // Fixed-height skeleton so the finished form does not shift the layout.
    return (
      <DashboardShell>
        <div className="min-h-[480px] space-y-6" aria-busy="true" aria-live="polite">
          <div className="space-y-2">
            <div className="h-7 w-56 animate-pulse rounded-md bg-muted" />
            <div className="h-4 w-80 animate-pulse rounded-md bg-muted" />
          </div>
          <div className="grid gap-6 lg:grid-cols-2">
            <div className="space-y-4">
              {Array.from({ length: 5 }).map((_, i) => (
                <div key={i} className="space-y-2">
                  <div className="h-4 w-32 animate-pulse rounded-md bg-muted" />
                  <div className="h-11 animate-pulse rounded-md bg-muted" />
                </div>
              ))}
            </div>
            <div className="h-64 animate-pulse rounded-lg bg-muted" />
          </div>
        </div>
      </DashboardShell>
    )
  }

  if (loadFailure === 'forbidden') {
    return (
      <DashboardShell>
        <section aria-label="دسترسی غیرمجاز">
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 text-destructive" />}
            title="دسترسی غیرمجاز"
            description="شما دسترسی مدیریت هویت فروشگاه را ندارید. برای ادامه با مدیر فروشگاه تماس بگیرید."
            tone="destructive"
            density="compact"
          />
        </section>
      </DashboardShell>
    )
  }

  if (loadFailure === 'unavailable') {
    return (
      <DashboardShell>
        <section aria-label="خطا در بارگذاری">
          <StatePanel
            icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 text-destructive" />}
            title="بارگذاری تنظیمات ممکن نبود"
            description="اتصال به فروشگاه برقرار نشد. دوباره تلاش کنید."
            action={
              <SecondaryButton type="button" onClick={loadProfile} className="mt-2">
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش مجدد
              </SecondaryButton>
            }
            tone="destructive"
            density="compact"
          />
        </section>
      </DashboardShell>
    )
  }

  return (
    <DashboardShell>
      <div className="space-y-6">
        <header className="space-y-1">
          <h1 className="text-2xl font-bold">هویت و سیاست‌های فروشگاه</h1>
          <p className="text-sm text-muted-foreground">
            {profile
              ? 'اطلاعات هویت فروشگاه و متن صفحات اطلاعات مشتریان را ویرایش کنید.'
              : 'هنوز هویتی برای فروشگاه ثبت نشده است. اطلاعات زیر را تکمیل و ذخیره کنید.'}
          </p>
        </header>

        {success && (
          <div
            role="status"
            className="flex items-center gap-2 rounded-lg border border-success/40 bg-success/10 px-4 py-2 text-sm font-medium text-success"
          >
            <CircleCheck aria-hidden="true" className="size-4 shrink-0" />
            {success}
          </div>
        )}

        {submitFailure?.kind === 'conflict' && (
          <div
            role="alert"
            className="flex items-start gap-3 rounded-lg border border-destructive/40 bg-destructive/10 p-4"
          >
            <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
            <div className="space-y-1">
              <p className="text-sm font-semibold">تغییرات دیگری قبلاً ذخیره شده است</p>
              <p className="text-sm leading-6 text-muted-foreground">
                نسخه‌ای که در دست ویرایش دارید قدیمی است. برای ادامه تغییرات جدید را بارگذاری و دوباره ذخیره کنید.
              </p>
              <SecondaryButton type="button" onClick={loadProfile} className="mt-2">
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                بارگذاری آخرین تغییرات
              </SecondaryButton>
            </div>
          </div>
        )}

        {submitFailure?.kind === 'forbidden' && (
          <div
            role="alert"
            className="flex items-start gap-3 rounded-lg border border-destructive/40 bg-destructive/10 p-4"
          >
            <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
            <div className="space-y-1">
              <p className="text-sm font-semibold">ذخیره‌سازی امکان‌پذیر نیست</p>
              <p className="text-sm leading-6 text-muted-foreground">
                شما دسترسی مدیریت هویت فروشگاه را ندارید.
              </p>
            </div>
          </div>
        )}

        {submitFailure?.kind === 'unavailable' && (
          <div
            role="alert"
            className="flex items-start gap-3 rounded-lg border border-destructive/40 bg-destructive/10 p-4"
          >
            <TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />
            <div className="space-y-1">
              <p className="text-sm font-semibold">ذخیره‌سازی ممکن نبود</p>
              <p className="text-sm leading-6 text-muted-foreground">اتصال برقرار نشد. دوباره تلاش کنید.</p>
              <SecondaryButton type="button" onClick={() => void form.handleSubmit(onSubmit)()} className="mt-2">
                <RefreshCw aria-hidden="true" className="me-2 size-4" />
                تلاش مجدد
              </SecondaryButton>
            </div>
          </div>
        )}

        <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
          <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6" noValidate>
            <section className="space-y-4" aria-labelledby="identity-heading">
              <h2 id="identity-heading" className="text-base font-semibold">
                هویت فروشگاه
              </h2>
              <ProfileField
                name="name"
                label="نام فروشگاه"
                limit={FIELD_LIMITS.name}
                placeholder="مثلاً: فروشگاه نور"
                register={register}
                value={values.name}
                error={fieldErrors.name}
              />
              <ProfileField
                name="tagline"
                label="شعار (اختیاری)"
                limit={FIELD_LIMITS.tagline}
                placeholder="یک جمله کوتاه درباره فروشگاه"
                register={register}
                value={values.tagline}
                error={fieldErrors.tagline}
              />
              <ProfileField
                name="supportPhone"
                label="تلفن پشتیبانی (اختیاری)"
                limit={FIELD_LIMITS.supportPhone}
                placeholder="۰۲۱-۱۲۳۴۵۶۷۸"
                dirLtr
                register={register}
                value={values.supportPhone}
                error={fieldErrors.supportPhone}
              />
              <ProfileField
                name="instagramUrl"
                label="نشانی اینستاگرام (اختیاری)"
                limit={FIELD_LIMITS.instagramUrl}
                placeholder="https://www.instagram.com/example"
                dirLtr
                register={register}
                value={values.instagramUrl}
                error={fieldErrors.instagramUrl}
              />
            </section>

            <section className="space-y-4" aria-labelledby="publish-heading">
              <h2 id="publish-heading" className="text-base font-semibold">
                انتشار
              </h2>
              <div className="flex items-center justify-between gap-3 rounded-lg border border-border bg-surface p-4">
                <div className="space-y-0.5">
                  <label htmlFor="profile-isPublished" className="text-sm font-medium">
                    انتشار فروشگاه
                  </label>
                  <p className="text-xs text-muted-foreground">
                    تا زمانی که غیرفعال است، هویت فروشگاه و صفحات اطلاعات مشتریان برای مشتریان قابل مشاهده
                    نیست.
                  </p>
                </div>
                <span
                  className={cn(
                    'relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full transition-colors duration-150 ease-admin',
                    isPublished ? 'bg-primary' : 'bg-muted-foreground/30'
                  )}
                >
                  <input
                    id="profile-isPublished"
                    type="checkbox"
                    role="switch"
                    aria-label="انتشار فروشگاه"
                    className="peer absolute inset-0 size-full cursor-pointer appearance-none rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                    {...register('isPublished')}
                  />
                  <span
                    aria-hidden="true"
                    className={cn(
                      'pointer-events-none absolute top-0.5 start-0.5 size-5 rounded-full bg-surface shadow transition-transform duration-150 ease-admin',
                      isPublished && '-translate-x-[1.25rem] rtl:translate-x-[1.25rem]'
                    )}
                  />
                </span>
              </div>
            </section>

            <section className="space-y-4" aria-labelledby="policy-heading">
              <h2 id="policy-heading" className="text-base font-semibold">
                صفحات اطلاعات مشتریان
              </h2>
              <ProfileField
                name="aboutText"
                label="درباره ما"
                limit={FIELD_LIMITS.aboutText}
                rows={4}
                placeholder="معرفی فروشگاه برای مشتریان"
                register={register}
                value={values.aboutText}
                error={fieldErrors.aboutText}
              />
              <ProfileField
                name="shippingPolicy"
                label="شرایط ارسال"
                limit={FIELD_LIMITS.shippingPolicy}
                rows={4}
                register={register}
                value={values.shippingPolicy}
                error={fieldErrors.shippingPolicy}
              />
              <ProfileField
                name="paymentPolicy"
                label="شرایط پرداخت"
                limit={FIELD_LIMITS.paymentPolicy}
                rows={4}
                register={register}
                value={values.paymentPolicy}
                error={fieldErrors.paymentPolicy}
              />
              <ProfileField
                name="returnPolicy"
                label="شرایط مرجوعی"
                limit={FIELD_LIMITS.returnPolicy}
                rows={4}
                register={register}
                value={values.returnPolicy}
                error={fieldErrors.returnPolicy}
              />
              <ProfileField
                name="privacyPolicy"
                label="حریم خصوصی"
                limit={FIELD_LIMITS.privacyPolicy}
                rows={4}
                register={register}
                value={values.privacyPolicy}
                error={fieldErrors.privacyPolicy}
              />
            </section>

            <div className="flex flex-wrap items-center gap-3">
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin" />
                    در حال ذخیره…
                  </>
                ) : (
                  'ذخیره تغییرات'
                )}
              </Button>
              <SecondaryButton type="button" onClick={loadProfile} disabled={isSubmitting}>
                لغو تغییرات
              </SecondaryButton>
            </div>
          </form>

          <aside className="space-y-3" aria-label="پیش‌نمایش فروشگاه">
            <StorePreview
              name={values.name}
              tagline={values.tagline}
              supportPhone={values.supportPhone}
              instagramUrl={values.instagramUrl}
              isPublished={isPublished}
            />
            <p className="text-xs leading-5 text-muted-foreground">
              پیش‌نمایش به‌صورت زنده با مقادیر فرم به‌روز می‌شود و دقیقاً همان چیزی است که مشتریان در سربرگ و
              پانوشت فروشگاه می‌بینند.
            </p>
          </aside>
        </div>
      </div>
    </DashboardShell>
  )
}
