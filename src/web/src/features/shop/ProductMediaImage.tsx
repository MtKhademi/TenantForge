import { ImageOff } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ProductImage } from '@/features/shop/contracts/mediaContract'
import { cn } from '@/lib/utils'

/**
 * S32 (F044): one rendered product image with a designed broken-content
 * fallback.
 *
 * A corrupted or unreachable `contentUrl` makes the real `<img>` fire
 * `onerror`; instead of a broken-image glyph the tile then renders a neutral
 * placeholder (icon + hint) at the same footprint — no layout shift, and the
 * image's `altText` is still announced through the container's accessible
 * name so the content is not silently lost.
 *
 * The `onError`-driven fallback is local to the element; it never invents a
 * success state and never retries the URL.
 */
export function MediaImage({
  image,
  className,
  imgClassName,
  showBrokenHint = true,
}: {
  image: ProductImage
  /** Tile sizing (e.g. `aspect-square`); the image always fills the box. */
  className?: string
  /** Extra classes for the inner <img> (e.g. sizing within the tile). */
  imgClassName?: string
  /** Render the visible broken-hint copy (thumbnails can hide it). */
  showBrokenHint?: boolean
}) {
  const [failed, setFailed] = useState(false)

  // A new image (different id/url) resets the failure flag so re-selected or
  // re-uploaded content is not stuck on the old fallback.
  useEffect(() => {
    setFailed(false)
  }, [image.id, image.contentUrl])

  const label = image.altText || `تصویر ${image.width}×${image.height}`

  return (
    <div
      role="img"
      aria-label={label}
      className={cn('relative flex items-center justify-center overflow-hidden bg-muted', className)}
    >
      {failed ? (
        <div
          aria-hidden="true"
          className={cn(
            'flex h-full w-full flex-col items-center justify-center gap-2 text-muted-foreground',
            imgClassName,
          )}
        >
          <ImageOff className="size-8" />
          {showBrokenHint && <span className="text-xs font-medium">تصویر در دسترس نیست</span>}
        </div>
      ) : (
        <img
          src={image.contentUrl}
          alt=""
          loading="lazy"
          onError={() => setFailed(true)}
          className={cn('h-full w-full object-cover', imgClassName)}
        />
      )}
    </div>
  )
}
