import { Minus, Package, Plus, ShoppingCart, TriangleAlert, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { mockStorefrontCart } from '@/features/shop/mockStorefrontCartState'
import { mockStorefrontCatalog } from '@/features/shop/mockStorefrontCatalog'
import type { StorefrontProductDetail } from '@/features/shop/storefrontTypes'
import { cn } from '@/lib/utils'

/**
 * S26 storefront (F030): the public product detail page.
 *
 * Data is mocked in `mockStorefrontCatalog` (F031 swaps the source only). The
 * gallery `imageUrls` are mock-only placeholders — rendered as neutral tiles,
 * never fake photos — with a fixed-overlay lightbox (no new dependency). The
 * color/size selectors reflect each combination's own stock; the quantity
 * stepper never exceeds the selected variant's mocked stock; the size-guide
 * table renders only when the product has one (absent otherwise, never an
 * empty table); add-to-cart writes to `mockStorefrontCart` (F032 builds the
 * cart page).
 */
export function ProductDetailPage() {
  const { tenantId = '', productSlug = '' } = useParams<{ tenantId: string; productSlug: string }>()
  const [product, setProduct] = useState<StorefrontProductDetail | null | undefined>(undefined)
  const [loadError, setLoadError] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const retry = () => setReloadKey((key) => key + 1)

  const [color, setColor] = useState<string | null>(null)
  const [size, setSize] = useState<string | null>(null)
  const [quantity, setQuantity] = useState(1)
  const [addedMessage, setAddedMessage] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setProduct(undefined)
    setLoadError(false)
    setColor(null)
    setSize(null)
    setQuantity(1)
    setAddedMessage(null)
    mockStorefrontCatalog
      .getProduct(tenantId, productSlug)
      .then((value) => {
        if (!cancelled) setProduct(value)
      })
      .catch(() => {
        if (!cancelled) setLoadError(true)
      })
    return () => {
      cancelled = true
    }
  }, [tenantId, productSlug, reloadKey])

  const colors = useMemo(
    () => [...new Set(product?.variants.map((v) => v.color) ?? [])],
    [product],
  )
  const sizes = useMemo(
    () => [...new Set(product?.variants.map((v) => v.size) ?? [])],
    [product],
  )
  const selectedVariant = useMemo(
    () => product?.variants.find((v) => v.color === color && v.size === size) ?? null,
    [product, color, size],
  )
  const soldOut = Boolean(color && size && (!selectedVariant || selectedVariant.stockQuantity === 0))
  const maxQuantity = selectedVariant?.stockQuantity ?? 1

  // Keep the quantity within the selected variant's stock when the selection
  // changes (never allow exceeding stock).
  useEffect(() => {
    setQuantity((current) => Math.max(1, Math.min(current, selectedVariant?.stockQuantity ?? 1)))
  }, [selectedVariant])

  const hasSizeGuide = (product?.sizeGuideColumns.length ?? 0) > 0
  const displayPrice = selectedVariant ? selectedVariant.effectivePrice : product?.basePrice ?? 0

  if (loadError) {
    return (
      <StatePanel
        icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
        title="محصول در دسترس نیست"
        description="هم‌اکنون نمی‌توانیم این محصول را بارگذاری کنیم. دوباره تلاش کنید."
        action={
          <Button type="button" className="mt-3 min-w-32" onClick={retry}>
            تلاش دوباره
          </Button>
        }
      />
    )
  }

  if (product === undefined) {
    return (
      <div aria-busy="true" className="grid gap-8 md:grid-cols-2">
        <div className="aspect-square animate-pulse rounded-xl bg-muted motion-reduce:animate-none" />
        <div className="space-y-4">
          <div className="h-6 w-2/3 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-full animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-4 w-1/2 animate-pulse rounded bg-muted motion-reduce:animate-none" />
          <div className="h-10 w-full animate-pulse rounded bg-muted motion-reduce:animate-none" />
        </div>
        <p className="sr-only">
          در حال بارگذاری محصول
          <span aria-hidden="true">…</span>
        </p>
      </div>
    )
  }

  if (product === null) {
    return (
      <section aria-label="محصول یافت نشد" className="space-y-6">
        <Link
          to={`/shop/${tenantId}`}
          className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
        >
          بازگشت به دسته‌بندی‌ها
        </Link>
        <StatePanel
          icon={<TriangleAlert aria-hidden="true" className="mt-0.5 size-5 shrink-0 text-destructive" />}
          title="محصول یافت نشد"
          description="محصولی با این نشانی پیدا نشد یا دیگر در دسترس نیست."
        />
      </section>
    )
  }

  function addToCart() {
    if (!selectedVariant) return
    mockStorefrontCart.addLine({
      variantId: selectedVariant.id,
      productName: product!.name,
      variantLabel: `${selectedVariant.color} / ${selectedVariant.size}`,
      unitPrice: selectedVariant.effectivePrice,
      quantity,
      imageUrl: '',
    })
    setAddedMessage('به سبد خرید افزوده شد.')
  }

  return (
    <section aria-label={product.name} className="grid gap-8 md:grid-cols-2">
      <div className="space-y-3">
        <Link
          to={`/shop/${tenantId}`}
          className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
        >
          بازگشت به دسته‌بندی‌ها
        </Link>
        <Gallery imageUrls={product.imageUrls} productName={product.name} />
      </div>

      <div className="space-y-6">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold">{product.name}</h1>
          <p className="text-muted-foreground">{product.description}</p>
          <div className="flex items-baseline gap-3 pt-1">
            <span className="text-xl font-semibold tabular-nums">
              {displayPrice.toLocaleString('fa-IR')} تومان
            </span>
            {product.compareAtPrice !== null && (
              <s className="text-sm text-muted-foreground">
                {product.compareAtPrice.toLocaleString('fa-IR')} تومان
              </s>
            )}
          </div>
        </div>

        <fieldset className="space-y-2">
          <legend className="mb-2 text-sm font-semibold">رنگ</legend>
          <div className="flex flex-wrap gap-2">
            {colors.map((option) => (
              <VariantOption
                key={option}
                label={option}
                selected={color === option}
                onSelect={() => setColor(option)}
              />
            ))}
          </div>
        </fieldset>

        <fieldset className="space-y-2">
          <legend className="mb-2 text-sm font-semibold">سایز</legend>
          <div className="flex flex-wrap gap-2">
            {sizes.map((option) => (
              <VariantOption
                key={option}
                label={option}
                selected={size === option}
                onSelect={() => setSize(option)}
              />
            ))}
          </div>
        </fieldset>

        {soldOut && (
          <p role="status" className="text-sm font-semibold text-destructive">
            این ترکیب رنگ و سایز موجود نیست.
          </p>
        )}

        {hasSizeGuide && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <caption className="mb-2 text-start font-semibold">راهنمای سایز</caption>
              <thead>
                <tr className="border-b border-border">
                  <th scope="col" className="px-2 py-2 text-start font-semibold">
                    سایز
                  </th>
                  {product.sizeGuideColumns.map((column) => (
                    <th key={column.id} scope="col" className="px-2 py-2 text-start font-semibold">
                      {column.name}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {product.sizeGuideRows.map((row) => (
                  <tr key={row.sizeLabel} className="border-b border-border last:border-b-0">
                    <th scope="row" className="px-2 py-2 text-start font-semibold">
                      {row.sizeLabel}
                    </th>
                    {product.sizeGuideColumns.map((column) => (
                      <td key={column.id} className="px-2 py-2 text-muted-foreground">
                        {row.cells.find((cell) => cell.columnId === column.id)?.value ?? ''}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <div className="flex items-center gap-3">
          <span className="text-sm font-semibold" id="quantity-label">
            تعداد
          </span>
          <div role="group" aria-labelledby="quantity-label" className="inline-flex items-center gap-2">
            <SecondaryButton
              type="button"
              aria-label="کاهش تعداد"
              className="px-3"
              disabled={!selectedVariant || soldOut || quantity <= 1}
              onClick={() => setQuantity((value) => Math.max(1, value - 1))}
            >
              <Minus aria-hidden="true" className="size-4" />
            </SecondaryButton>
            <span
              className="min-w-10 text-center text-sm font-semibold tabular-nums"
              aria-live="polite"
            >
              {quantity.toLocaleString('fa-IR')}
            </span>
            <SecondaryButton
              type="button"
              aria-label="افزایش تعداد"
              className="px-3"
              disabled={!selectedVariant || soldOut || quantity >= maxQuantity}
              onClick={() => setQuantity((value) => Math.min(maxQuantity, value + 1))}
            >
              <Plus aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        </div>

        <div className="space-y-3">
          <Button
            type="button"
            className="w-full"
            disabled={!selectedVariant || soldOut}
            onClick={addToCart}
          >
            <ShoppingCart aria-hidden="true" className="me-2 size-4" />
            افزودن به سبد خرید
          </Button>
          {addedMessage && (
            <p role="status" className="text-sm font-medium text-success">
              {addedMessage}
            </p>
          )}
        </div>
      </div>
    </section>
  )
}

/** A single color or size choice, with a clear selected state (aria-pressed). */
function VariantOption({
  label,
  selected,
  onSelect,
}: {
  label: string
  selected: boolean
  onSelect: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={cn(
        'rounded-md border px-3 py-1.5 text-sm transition-colors',
        selected
          ? 'border-primary bg-primary/10'
          : 'border-border bg-surface hover:bg-muted',
      )}
    >
      {label}
    </button>
  )
}

/**
 * The image gallery: an enlarged active tile plus a thumbnail strip. Images are
 * mock placeholders (neutral tiles, never fake photos). Clicking the active
 * tile opens a fixed-overlay lightbox.
 */
function Gallery({ imageUrls, productName }: { imageUrls: string[]; productName: string }) {
  const [activeIndex, setActiveIndex] = useState(0)
  const [lightboxOpen, setLightboxOpen] = useState(false)
  const mainRef = useRef<HTMLButtonElement>(null)
  const closeButtonRef = useRef<HTMLButtonElement>(null)

  const count = imageUrls.length
  const safeIndex = count > 0 ? Math.min(activeIndex, count - 1) : 0

  useEffect(() => {
    if (!lightboxOpen) return
    // The main tile is always mounted (the lightbox is a sibling overlay), so
    // capture the node now and focus it on close rather than re-reading the ref.
    const mainTile = mainRef.current
    closeButtonRef.current?.focus()
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') setLightboxOpen(false)
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      mainTile?.focus()
    }
  }, [lightboxOpen])

  if (count === 0) {
    return (
      <div className="flex aspect-square items-center justify-center rounded-xl border border-border bg-muted text-muted-foreground">
        <Package aria-hidden="true" className="size-10" />
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <button
        ref={mainRef}
        type="button"
        onClick={() => setLightboxOpen(true)}
        aria-label={`بزرگ‌نمایی تصویر ${productName}`}
        className="flex aspect-square w-full items-center justify-center rounded-xl border border-border bg-muted text-muted-foreground"
      >
        <PlaceholderImage index={safeIndex + 1} large />
      </button>

      <div className="grid grid-cols-4 gap-2">
        {imageUrls.map((_, index) => (
          <button
            key={index}
            type="button"
            onClick={() => setActiveIndex(index)}
            aria-label={`نمایش تصویر ${index + 1} از ${count}`}
            aria-current={index === safeIndex ? 'true' : undefined}
            className={cn(
              'flex aspect-square items-center justify-center rounded-md border bg-muted text-muted-foreground',
              index === safeIndex ? 'border-primary' : 'border-border hover:bg-muted/70',
            )}
          >
            <PlaceholderImage index={index + 1} />
          </button>
        ))}
      </div>

      {lightboxOpen && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
          onClick={(event) => {
            if (event.target === event.currentTarget) setLightboxOpen(false)
          }}
        >
          <div
            role="dialog"
            aria-modal="true"
            aria-label={`تصویر بزرگ‌نمایی‌شده ${productName}`}
            className="relative w-full max-w-lg"
            onClick={(event) => event.stopPropagation()}
          >
            <div className="flex aspect-square w-full items-center justify-center rounded-xl border border-border bg-muted text-muted-foreground shadow-raised">
              <PlaceholderImage index={safeIndex + 1} large />
            </div>
            <SecondaryButton
              ref={closeButtonRef}
              type="button"
              aria-label="بستن تصویر بزرگ‌نمایی‌شده"
              className="absolute -top-3 -end-3 rounded-full px-3 shadow-raised"
              onClick={() => setLightboxOpen(false)}
            >
              <X aria-hidden="true" className="size-4" />
            </SecondaryButton>
          </div>
        </div>
      )}
    </div>
  )
}

/** A neutral placeholder for a product image (mock-only, no network request). */
function PlaceholderImage({ index, large = false }: { index: number; large?: boolean }) {
  return (
    <span className="flex flex-col items-center justify-center gap-1">
      <Package aria-hidden="true" className={large ? 'size-12' : 'size-5'} />
      <span className={large ? 'text-sm' : 'text-xs'}>{index.toLocaleString('fa-IR')}</span>
    </span>
  )
}
