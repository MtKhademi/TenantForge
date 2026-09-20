import {
  useCallback,
  useEffect,
  useState,
  type ReactNode,
} from 'react'
import {
  ChevronDown,
  ChevronUp,
  ImagePlus,
  ImageOff,
  Loader2,
  Trash2,
  X,
} from 'lucide-react'
import { useForm } from 'react-hook-form'
import {
  Button,
  SecondaryButton,
} from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { TextInput } from '@/components/ui/TextInput'
import { cn } from '@/lib/utils'
import { useShopClients } from './ShopClientsProvider'
import { MediaImage } from './ProductMediaImage'
import type {
  ProductGallery,
  ProductImage,
  ReorderProductImagesRequest,
  ShopProductWithGallery,
} from './contracts/mediaContract'
import { ShopMediaError } from './contracts/mediaContract'

/**
 * S32 admin gallery editor (F044).
 *
 * A managed interaction for uploading, reordering and removing product images.
 *
 * - Implements the eight-image cap: upload control is disabled/hidden when full.
 * - Responsive layout: thumbnails wrap and stack at mobile viewports.
 * - Accessible reordering: keyboard-operable a-priori "Move Earlier/Later"
 *   controls for each image to avoid complex drag-and-drop edge cases.
 * - State handling: loading (without shift), success, and all B036 error
 *   statuses (400 validation, 403 forbidden, 409 conflict, 413 oversized, 415 corrupt).
 * - Object-URL cleanup: local file previews are revoked on unmount/replace.
 */
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
  const [isBusy, setIsBusy] = useState(false)
  const [error, setError] = useState<ShopMediaError | null>(null)
  const [uploadingFile, setUploadingFile] = useState<File | null>(null)
  const [altText, setAltText] = useState('')
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)

  // Sync with mockS32 data source
  useEffect(() => {
    void loadGallery()
  }, [productId, tenantId])

  async function loadGallery() {
    setIsBusy(true)
    setError(null)
    try {
      const product = await media.getProduct(tenantId, productId)
      setGallery({ images: product.images, galleryVersion: product.galleryVersion })
    } catch (e) {
      if (e instanceof ShopMediaError) setError(e)
      else setError(new ShopMediaError(500, 'خطای غیرمنتظره در بارگذاری گالری.'))
    } finally {
      setIsBusy(false)
    }
  }

  async function handleUpload(e: React.FormEvent) {
    e.preventDefault()
    if (!uploadingFile) return

    setIsBusy(true)
    setError(null)
    try {
      const result = await media.upload(
        tenantId,
        productId,
        uploadingFile,
        altText,
        gallery?.galleryVersion ?? 0,
      )
      setGallery(result)
      setUploadingFile(null)
      setAltText('')
      setPreviewUrl(null)
      onUpdate?.(result)
    } catch (e) {
      if (e instanceof ShopMediaError) setError(e)
      else setError(new ShopMediaError(500, 'خطا در بارگذاری تصویر.'))
    } finally {
      setIsBusy(false)
    }
  }

  async function handleRemove(imageId: string) {
    if (!gallery) return
    setIsBusy(true)
    setError(null)
    try {
      await media.remove(tenantId, productId, imageId, gallery.galleryVersion)
      // Refresh to get new order and version
      await loadGallery()
    } catch (e) {
      if (e instanceof ShopMediaError) setError(e)
      else setError(new ShopMediaError(500, 'حذف تصویر ناموفق بود.'))
    } finally {
      setIsBusy(false)
    }
  }

  async function handleMove(imageId: string, direction: 'earlier' | 'later') {
    if (!gallery) return
    const { images, galleryVersion } = gallery
    const index = images.findIndex((img) => img.id === imageId)
    if (index === -1) return

    const newIds = images.map((img) => img.id)
    const targetIndex = direction === 'earlier' ? index - 1 : index + 1
    if (targetIndex < 0 || targetIndex >= newIds.length) return

    [newIds[index], newIds[targetIndex]] = [newIds[targetIndex], newIds[index]]

    setIsBusy(true)
    setError(null)
    try {
      const result = await media.reorder(tenantId, productId, {
        imageIds: newIds,
        expectedGalleryVersion: galleryVersion,
      })
      setGallery(result)
      onUpdate?.(result)
    } catch (e) {
      if (e instanceof ShopMediaError) setError(e)
      else setError(new ShopMediaError(500, 'تغییر ترتیب ناموفق بود.'))
    } finally {
      setIsBusy(false)
    }
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    setUploadingFile(file)
    if (previewUrl) URL.revokeObjectURL(previewUrl)
    setPreviewUrl(URL.createObjectURL(file))
  }

  useEffect(() => {
    return () => {
      if (previewUrl) URL.revokeObjectURL(previewUrl)
    }
  }, [previewUrl])

  if (isBusy && !gallery) {
    return (
      <div className="flex h-40 items-center justify-center">
        <Loader2 aria-hidden="true" className="size-8 animate-spin text-muted-foreground" />
      </div>
    )
  }

  const isFull = (gallery?.images.length ?? 0) >= 8

  return (
    <div className="space-y-8">
      {/* Upload Section */}
      <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
        <h3 className="mb-4 text-sm font-semibold">افزودن تصویر</h3>
        {!isFull ? (
          <form onSubmit={handleUpload} className="grid gap-4 sm:grid-cols-3 items-end">
            <div className="space-y-2">
              <label className="text-xs font-medium text-muted-foreground" htmlFor="media-file">
                انتخاب فایل
              </label>
              <div className="relative overflow-hidden rounded-md border border-input bg-background px-3 py-2 text-sm">
                <input
                  id="media-file"
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  className="absolute inset-0 cursor-pointer opacity-0"
                  onChange={handleFileChange}
                  disabled={isBusy}
                />
                <div className="flex items-center gap-2 truncate">
                  {uploadingFile ? (
                    <span className="truncate">{uploadingFile.name}</span>
                  ) : (
                    <span className="text-muted-foreground">انتخاب کنید...</span>
                  )}
                </div>
              </div>
            </div>
            <div className="space-y-2">
              <label className="text-xs font-medium text-muted-foreground" htmlFor="media-alt">
                متن جایگزین (Alt Text)
              </label>
              <TextInput
                id="media-alt"
                value={altText}
                onChange={(e) => setAltText(e.target.value)}
                placeholder="توصیف تصویر..."
                disabled={isBusy}
              />
            </div>
            <Button type="submit" disabled={!uploadingFile || isBusy}>
              {isBusy && uploadingFile ? (
                <>
                  <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin" />
                  در حال آپلود...
                </>
              ) : (
                'بارگذاری'
              )}
            </Button>
          </form>
        ) : (
          <div className="flex items-center gap-2 text-sm font-medium text-amber-600 bg-amber-50 p-3 rounded-lg border border-amber-200">
            <X aria-hidden="true" className="size-4" />
            حداکثر ۸ تصویر در گالری مجاز است. برای افزودن تصویر جدید، یکی را حذف کنید.
          </div>
        )}
      </div>

      {/* Error Panel */}
      {error && (
        <StatePanel
          tone="destructive"
          icon={<ImageOff aria-hidden="true" className="size-5 text-destructive" />}
          title="خطا در عملیات رسانه"
          description={error.message}
          action={
            <Button type="button" className="mt-3 min-w-32" onClick={loadGallery}>
              تلاش دوباره
            </Button>
          }
        />
      )}

      {/* Gallery Grid */}
      <div className="space-y-4">
        <h3 className="text-sm font-semibold">گالری تصاویر</h3>
        {(!gallery || gallery.images.length === 0) ? (
          <div className="flex h-64 flex-col items-center justify-center rounded-xl border border-dashed border-border bg-surface text-muted-foreground">
            <ImagePlus aria-hidden="true" className="mb-2 size-10 opacity-20" />
            <p className="text-sm">هنوز تصویری بارگذاری نشده است.</p>
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
            {gallery.images.map((img, index) => (
              <div
                key={img.id}
                className={cn(
                  'group relative flex flex-col gap-2 rounded-lg border border-border bg-surface p-2 transition-colors hover:border-primary',
                  index === 0 && 'ring-2 ring-primary ring-offset-2'
                )}
              >
                {index === 0 && (
                  <span className="absolute -top-2 -start-2 z-10 rounded-full bg-primary px-1.5 py-0.5 text-[10px] font-bold text-primary-foreground">
                    اصلی
                  </span>
                )}
                <MediaImage
                  image={img}
                  className="aspect-square rounded-md"
                />
                <div className="flex items-center justify-between gap-1">
                  <div className="flex gap-1">
                    <SecondaryButton
                      type="button"
                      className="size-8 p-0"
                      disabled={index === 0 || isBusy}
                      onClick={() => handleMove(img.id, 'earlier')}
                      aria-label="انتقال به قبل"
                    >
                      <ChevronUp aria-hidden="true" className="size-4" />
                    </SecondaryButton>
                    <SecondaryButton
                      type="button"
                      className="size-8 p-0"
                      disabled={index === gallery.images.length - 1 || isBusy}
                      onClick={() => handleMove(img.id, 'later')}
                      aria-label="انتقال به بعد"
                    >
                      <ChevronDown aria-hidden="true" className="size-4" />
                    </SecondaryButton>
                  </div>
                  <SecondaryButton
                    type="button"
                    className="size-8 p-0 text-destructive hover:bg-destructive/10"
                    disabled={isBusy}
                    onClick={() => handleRemove(img.id)}
                    aria-label="حذف تصویر"
                  >
                    <Trash2 aria-hidden="true" className="size-4" />
                  </SecondaryButton>
                </div>
                <p className="truncate text-[11px] text-muted-foreground">
                  {img.altText || 'بدون متن جایگزین'}
                </p>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
