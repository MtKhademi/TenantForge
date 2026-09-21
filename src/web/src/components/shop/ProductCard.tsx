import { Package } from 'lucide-react'
import { Link } from 'react-router-dom'
import type { StorefrontProductSummary } from '@/features/shop/contracts/discoveryContract'

/**
 * S33 storefront discovery product card (F045).
 *
 * Renders a single `StorefrontProductSummary` from the discovery client. The
 * card links to the product detail page (the detail route still uses the B027
 * `productSlug`). It never re-sorts or re-filters — it only displays the exact
 * summary the (mock) server returned. `thumbnailUrl` may be null (B037); in
 * that case a neutral placeholder is shown rather than a broken image.
 */
export type ProductCardProps = {
  tenantId: string
  product: StorefrontProductSummary
}

export function ProductCard({ tenantId, product }: ProductCardProps) {
  const hasSale = product.isOnSale && product.compareAtPrice !== null && product.compareAtPrice > product.effectivePrice

  return (
    <Link
      to={`/shop/${tenantId}/products/${product.slug}`}
      aria-label={product.isSoldOut ? `${product.name} — ناموجود` : product.name}
      className="group overflow-hidden rounded-xl border border-border bg-surface shadow-soft transition-colors hover:bg-muted focus-visible:bg-muted"
    >
      {product.thumbnailUrl ? (
        <img
          src={product.thumbnailUrl}
          alt=""
          aria-hidden="true"
          loading="lazy"
          className="aspect-square w-full object-cover"
        />
      ) : (
        <div
          className="flex aspect-square items-center justify-center bg-muted text-muted-foreground"
          role="img"
          aria-label={`تصویر ${product.name}`}
        >
          <Package aria-hidden="true" className="size-8" />
        </div>
      )}

      <div className="space-y-1 p-4">
        <p className="line-clamp-2 font-semibold">{product.name}</p>
        <div className="flex items-baseline gap-2">
          <span className="text-sm font-medium tabular-nums">
            {product.effectivePrice.toLocaleString('fa-IR')} تومان
          </span>
          {hasSale && (
            <s className="text-sm text-muted-foreground tabular-nums">
              {product.compareAtPrice!.toLocaleString('fa-IR')}
            </s>
          )}
        </div>

        <div className="flex flex-wrap items-center gap-1.5 pt-1">
          {hasSale && (
            <span className="inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-xs font-semibold text-primary">
              تخفیف
            </span>
          )}
          {product.isSoldOut && (
            <span className="inline-flex items-center rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-semibold text-destructive">
              ناموجود
            </span>
          )}
        </div>
      </div>
    </Link>
  )
}
