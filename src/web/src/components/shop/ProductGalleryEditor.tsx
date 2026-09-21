import { ChevronDown, ChevronUp, ImagePlus, Loader2, RefreshCw, Trash2, TriangleAlert, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { ShopClientError } from '@/features/shop/contracts/shopContract'
import { MAX_GALLERY_IMAGES, type ProductGallery, type ProductImage } from '@/features/shop/contracts/mediaContract'
import { cn } from '@/lib/utils'
import { ProductMediaImage } from './ProductMediaImage'

export function ProductGalleryEditor({
  productId,
  tenantId,
  onUpdate,
}: {
  productId: string
  tenantId: string
  onUpdate?: (gallery: ProductGallery) => void
}) {
  const { media } = useShopClients()
  const [gallery, setGallery] = useState<ProductGallery | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [isMutating, setIsMutating] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [error, setError] = useState<GalleryErrorState | null>(null)
  const [uploadingFile, setUploadingFile] = useState<File | null>(null)
  const [altText, setAltText] = useState('')
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    void loadGallery()
    return () => abortRef.current?.abort()
  }, [productId, tenantId])

  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl)
    }
  }, [previewUrl])

  async function loadGallery() {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    setIsLoading(true)
    setError(null)
    setNotice(null)
    try {
      const product = await media.getProduct(tenantId, productId, controller.signal)
      if (controller.signal.aborted) return
      setGallery({ images: product.images, galleryVersion: product.galleryVersion })
    } catch (caught) {
      if (isAbort(caught)) return
      setError(toGalleryError(caught))
      if (caught instanceof ApiUnavailableError) setGallery((current) => current ?? { images: [], galleryVersion: 1 })
    } finally {
      if (!controller.signal.aborted) setIsLoading(false)
    }
  }

  async function mutate(action: (signal: AbortSignal) => Promise<ProductGallery | void>, successMessage?: string) {
    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller
    setIsMutating(true)
    setError(null)
    setNotice(null)
    try {
      const result = await action(controller.signal)
      if (controller.signal.aborted) return
      if (result) {
        setGallery(result)
        onUpdate?.(result)
      }
      if (successMessage) setNotice(successMessage)
    } catch (caught) {
      if (!isAbort(caught)) setError(toGalleryError(caught))
    } finally {
      if (!controller.signal.aborted) setIsMutating(false)
    }
  }

  async function handleUpload(event: React.FormEvent) {
    event.preventDefault()
    if (!uploadingFile || !gallery) return
    await mutate(
      (signal) => media.upload(tenantId, productId, uploadingFile, altText, gallery.galleryVersion, signal),
      'تصویر پس از پاسخ موفق mock به گالری اضافه شد.',
    )
    setUploadingFile(null)
    setAltText('')
    setPreviewUrl((current) => {
      if (current) URL.revokeObjectURL(current)
      return null
    })
  }

  async function handleRemove(imageId: string) {
    if (!gallery) return
    await mutate(async (signal) => {
      await media.remove(tenantId, productId, imageId, gallery.galleryVersion, signal)
      const product = await media.getProduct(tenantId, productId, signal)
      return { images: product.images, galleryVersion: product.galleryVersion }
    }, 'تصویر پس از پاسخ موفق mock حذف شد.')
  }

  async function handleMove(imageId: string, direction: 'earlier' | 'later') {
    if (!gallery) return
    const index = gallery.images.findIndex((img) => img.id === imageId)
    const targetIndex = direction === 'earlier' ? index - 1 : index + 1
    if (index < 0 || targetIndex < 0 || targetIndex >= gallery.images.length) return
    const ids = gallery.images.map((img) => img.id)
    ;[ids[index], ids[targetIndex]] = [ids[targetIndex], ids[index]]
    await mutate(
      (signal) => media.reorder(tenantId, productId, { imageIds: ids, expectedGalleryVersion: gallery.galleryVersion }, signal),
      'ترتیب تصاویر پس از پاسخ موفق mock به‌روزرسانی شد.',
    )
  }

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    setUploadingFile(file)
    setPreviewUrl((current) => {
      if (current) URL.revokeObjectURL(current)
      return URL.createObjectURL(file)
    })
  }

  const isFull = (gallery?.images.length ?? 0) >= MAX_GALLERY_IMAGES
  const isBusy = isLoading || isMutating

  return (
    <section aria-label="مدیریت گالری محصول" className="space-y-6">
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-base font-semibold">گالری محصول</h3>
            <p className="mt-1 text-sm text-muted-foreground">
              تصویر اول بر اساس ترتیب نمایش، تصویر اصلی کارت و صفحه محصول است.
            </p>
          </div>
          <SecondaryButton type="button" disabled={isBusy} onClick={() => void loadGallery()}>
            <RefreshCw aria-hidden="true" className={cn('me-2 size-4', isLoading && 'animate-spin motion-reduce:animate-none')} />
            به‌روزرسانی گالری
          </SecondaryButton>
        </div>

        {isLoading && !gallery && <GallerySkeleton />}

        {gallery && (
          <div className="mt-5 space-y-5">
            {gallery.images.length === 0 ? (
              <div className="flex min-h-48 flex-col items-center justify-center rounded-xl border border-dashed border-border bg-muted/40 p-6 text-center text-muted-foreground">
                <ImagePlus aria-hidden="true" className="mb-2 size-9 opacity-40" />
                <p className="text-sm font-semibold text-foreground">هنوز تصویری بارگذاری نشده است.</p>
                <p className="mt-1 text-sm">این وضعیت خالی موفق است، نه خطا.</p>
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
                {gallery.images.map((image, index) => (
                  <GalleryTile
                    key={image.id}
                    image={image}
                    index={index}
                    count={gallery.images.length}
                    busy={isBusy}
                    onMove={handleMove}
                    onRemove={handleRemove}
                  />
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <h3 className="text-sm font-semibold">افزودن تصویر</h3>
        {isFull ? (
          <div className="mt-4 flex items-start gap-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm font-medium text-warning">
            <X aria-hidden="true" className="mt-0.5 size-4" />
            <p>حداکثر ۸ تصویر در گالری مجاز است. برای افزودن تصویر جدید، یکی را حذف کنید.</p>
          </div>
        ) : (
          <form onSubmit={(event) => void handleUpload(event)} className="mt-4 grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] lg:items-end" noValidate>
            <div className="space-y-2">
              <label className="text-xs font-medium text-muted-foreground" htmlFor="media-file">
                انتخاب فایل JPEG، PNG یا WebP
              </label>
              <input
                id="media-file"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                className="block w-full rounded-md border border-input bg-surface px-3 py-2 text-sm file:me-3 file:rounded-md file:border-0 file:bg-muted file:px-3 file:py-1.5 file:text-sm file:font-semibold file:text-foreground focus-visible:border-ring disabled:opacity-60"
                onChange={handleFileChange}
                disabled={isBusy}
              />
              {previewUrl && <img src={previewUrl} alt="پیش‌نمایش فایل انتخاب‌شده" className="mt-2 aspect-video max-h-28 rounded-md object-cover" />}
            </div>
            <div className="space-y-2">
              <label className="text-xs font-medium text-muted-foreground" htmlFor="media-alt">
                متن جایگزین تصویر
              </label>
              <TextInput
                id="media-alt"
                value={altText}
                onChange={(event) => setAltText(event.target.value)}
                placeholder="مثلاً نمای جلوی محصول"
                aria-invalid={Boolean(error?.fieldErrors.altText)}
                aria-describedby={error?.fieldErrors.altText ? 'media-alt-error' : undefined}
                disabled={isBusy}
              />
              {error?.fieldErrors.altText && <p id="media-alt-error" className="text-sm text-destructive">{error.fieldErrors.altText}</p>}
            </div>
            <Button type="submit" disabled={!uploadingFile || isBusy || !gallery}>
              {isMutating ? <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin motion-reduce:animate-none" /> : <ImagePlus aria-hidden="true" className="me-2 size-4" />}
              {isMutating ? 'در حال آپلود' : 'بارگذاری'}
            </Button>
          </form>
        )}
      </div>

      {error && <ErrorPanel error={error} onRetry={() => void loadGallery()} />}
      {notice && <p role="status" className="rounded-lg border border-success/40 bg-success/10 px-4 py-2 text-sm font-medium text-success">{notice}</p>}
    </section>
  )
}

function GalleryTile({
  image,
  index,
  count,
  busy,
  onMove,
  onRemove,
}: {
  image: ProductImage
  index: number
  count: number
  busy: boolean
  onMove: (imageId: string, direction: 'earlier' | 'later') => void
  onRemove: (imageId: string) => void
}) {
  return (
    <article className={cn('relative rounded-lg border border-border bg-background p-2', index === 0 && 'ring-2 ring-primary ring-offset-2 ring-offset-surface')}>
      {index === 0 && <span className="absolute -top-2 start-2 z-10 rounded-full bg-primary px-2 py-0.5 text-[11px] font-bold text-primary-foreground">اصلی</span>}
      <ProductMediaImage image={image} className="aspect-square rounded-md" />
      <p className="mt-2 truncate text-xs text-muted-foreground">{image.altText || 'بدون متن جایگزین'}</p>
      <div className="mt-2 flex items-center justify-between gap-1">
        <div className="flex gap-1">
          <SecondaryButton type="button" className="size-8 p-0" disabled={index === 0 || busy} onClick={() => onMove(image.id, 'earlier')} aria-label={`انتقال تصویر ${index + 1} به قبل`}>
            <ChevronUp aria-hidden="true" className="size-4" />
          </SecondaryButton>
          <SecondaryButton type="button" className="size-8 p-0" disabled={index === count - 1 || busy} onClick={() => onMove(image.id, 'later')} aria-label={`انتقال تصویر ${index + 1} به بعد`}>
            <ChevronDown aria-hidden="true" className="size-4" />
          </SecondaryButton>
        </div>
        <SecondaryButton type="button" className="size-8 p-0 text-destructive hover:bg-destructive/10" disabled={busy} onClick={() => onRemove(image.id)} aria-label={`حذف تصویر ${index + 1}`}>
          <Trash2 aria-hidden="true" className="size-4" />
        </SecondaryButton>
      </div>
    </article>
  )
}

type GalleryErrorState = { title: string; message: string; fieldErrors: Record<string, string>; retryAfterSeconds?: number }

function toGalleryError(caught: unknown): GalleryErrorState {
  if (caught instanceof ApiUnavailableError) return { title: 'رسانه‌ها در دسترس نیستند', message: 'اتصال برقرار نشد. دوباره تلاش کنید.', fieldErrors: {} }
  if (caught instanceof ShopClientError) return problemToError(caught.problem.status, caught.message, caught.problem.errors, caught.problem.retryAfterSeconds)
  if (caught instanceof Error && 'status' in caught && typeof (caught as { status: unknown }).status === 'number') {
    const media = caught as { status: number; message: string; fieldErrors?: Record<string, string>; retryAfterSeconds?: number | null }
    return problemToError(media.status, media.message, media.fieldErrors, media.retryAfterSeconds ?? undefined)
  }
  return { title: 'خطا در عملیات رسانه', message: 'عملیات رسانه کامل نشد.', fieldErrors: {} }
}

function problemToError(status: number, message: string, fields?: Record<string, string | string[]>, retryAfterSeconds?: number): GalleryErrorState {
  const fieldErrors: Record<string, string> = {}
  for (const [key, value] of Object.entries(fields ?? {})) fieldErrors[key] = Array.isArray(value) ? value[0] ?? '' : value
  const titles: Record<number, string> = {
    400: 'اطلاعات تصویر معتبر نیست',
    403: 'دسترسی مدیریت رسانه مجاز نیست',
    404: 'محصول یا تصویر پیدا نشد',
    409: 'نسخه گالری قدیمی است',
    410: 'رسانه دیگر در دسترس نیست',
    413: 'حجم تصویر بیش از حد مجاز است',
    415: 'فرمت یا محتوای تصویر پشتیبانی نمی‌شود',
    429: 'درخواست‌ها بیش از حد مجاز است',
  }
  const suffix = status === 429 && retryAfterSeconds ? ` ${retryAfterSeconds.toLocaleString('fa-IR')} ثانیه بعد دوباره تلاش کنید.` : ''
  return { title: titles[status] ?? 'خطا در عملیات رسانه', message: `${message}${suffix}`, fieldErrors, retryAfterSeconds }
}

function ErrorPanel({ error, onRetry }: { error: GalleryErrorState; onRetry: () => void }) {
  return (
    <StatePanel
      tone="destructive"
      icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
      title={error.title}
      description={error.message}
      action={<Button type="button" className="mt-3 min-w-32" onClick={onRetry}>تلاش دوباره</Button>}
    />
  )
}

function GallerySkeleton() {
  return (
    <div aria-busy="true" className="mt-5 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5">
      <p className="sr-only">در حال بارگذاری گالری</p>
      {[0, 1, 2, 3].map((index) => (
        <div key={index} className="rounded-lg border border-border bg-background p-2">
          <div className="aspect-square animate-pulse rounded-md bg-muted motion-reduce:animate-none" />
          <div className="mt-2 h-3 w-3/4 animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
      ))}
    </div>
  )
}

function isAbort(caught: unknown) {
  return caught instanceof DOMException && caught.name === 'AbortError'
}
