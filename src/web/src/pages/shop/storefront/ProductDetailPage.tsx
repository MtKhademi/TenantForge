import { Minus, Package, Plus, ShoppingCart, TriangleAlert } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { StatePanel } from '@/components/ui/StatePanel'
import { cartAdapter } from '@/features/shop/cartAdapter'
import { storefrontAdapter } from '@/features/shop/storefrontAdapter'
import type { StorefrontProductDetail } from '@/features/shop/storefrontTypes'
import { cn } from '@/lib/utils'

/**
 * S26 storefront (F030, F031): the public product detail page.
 *
 * F031 loads real, anonymous data from B027 via `storefrontAdapter`. The
 * gallery is a plain placeholder tile — B027's response carries no image
 * fields, so there is no real image to show (no lightbox). The color/size
 * selectors reflect each combination's own live stock; the quantity stepper
 * never exceeds the selected variant's stock; the size-guide table renders
 * only when the product has one (absent otherwise, never an empty table);
 * add-to-cart calls B028's real cart API through `cartAdapter` (F033),
 * persisting the cart id in localStorage across page loads.
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
    storefrontAdapter
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

  return (
    <section aria-label={product.name} className="grid gap-8 md:grid-cols-2">
      <div className="space-y-3">
        <Link
          to={`/shop/${tenantId}`}
          className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
        >
          بازگشت به دسته‌بندی‌ها
        </Link>
        <div
          className="flex aspect-square w-full items-center justify-center rounded-xl border border-border bg-muted text-muted-foreground"
          role="img"
          aria-label={`تصویر ${product.name}`}
        >
          <Package aria-hidden="true" className="size-10" />
        </div>
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
            onClick={async () => {
              if (!selectedVariant) return
              try {
                await cartAdapter.addItem(tenantId, selectedVariant.id, quantity)
                setAddedMessage('به سبد خرید افزوده شد.')
              } catch (error) {
                setAddedMessage(error instanceof Error ? error.message : 'افزودن به سبد خرید ممکن نشد.')
              }
            }}
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
