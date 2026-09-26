import { ImageOff } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ProductImage } from '@/features/shop/contracts/mediaContract'
import { useShopClients } from '@/features/shop/clients/ShopClientsProvider'
import { cn } from '@/lib/utils'

/**
 * S32 product media image tile (F044, extended by F054).
 *
 * Two source modes share one visual:
 *
 * - **public** (storefront, the default): `image.contentUrl` is the anonymous
 *   public byte route (`GET /api/shop/{tenantId}/media/{imageId}`), so a plain
 *   `<img src>` loads it.
 * - **protected** (admin gallery): B036's admin gallery read returns
 *   `contentUrl` as the token-protected route
 *   (`GET /api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content`),
 *   which a plain `<img>` cannot reach — a browser image element never sends
 *   the bearer token. The tile therefore fetches the bytes through
 *   `ShopMediaClient.getProtectedContent` (authenticated `shopFetchBlob`),
 *   renders them through a short-lived `URL.createObjectURL` URL, and revokes
 *   that URL whenever it is replaced or the tile unmounts, so previews never
 *   leak. A failed fetch or a failed decode falls through to the same
 *   "تصویر در دسترس نیست" fallback the public mode already had, so a broken
 *   protected payload surfaces the same way a broken public one does.
 *
 * This is the minimal material contract correction documented for F054: the
 * accepted F044 UI was built against a mock whose `contentUrl` was a directly
 * renderable data URL; the delivered B036 contract (whose own integration
 * tests fetch `contentUrl` with an authenticated client) requires the
 * protected fetch in the admin surface.
 */
export function ProductMediaImage({
  image,
  protected: protectedSource,
  className,
  imgClassName,
  showBrokenHint = true,
}: {
  image: ProductImage
  /** Admin gallery: contentUrl needs the bearer token, so fetch it protected. */
  protected?: { tenantId: string; productId: string }
  className?: string
  imgClassName?: string
  showBrokenHint?: boolean
}) {
  const { media } = useShopClients()
  const [blobUrl, setBlobUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  const label = image.altText || `تصویر ${image.width}×${image.height}`

  useEffect(() => {
    if (!protectedSource) return
    const controller = new AbortController()
    let objectUrl: string | null = null
    setFailed(false)
    setBlobUrl(null)
    void media
      .getProtectedContent(protectedSource.tenantId, protectedSource.productId, image.id, controller.signal)
      .then((blob) => {
        if (controller.signal.aborted) return
        objectUrl = URL.createObjectURL(blob)
        setBlobUrl(objectUrl)
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true)
      })
    return () => {
      controller.abort()
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
    // Re-fetch only when the image or the tenant/product scope changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [image.id, protectedSource?.tenantId, protectedSource?.productId])

  return (
    <div
      role="img"
      aria-label={label}
      className={cn('relative flex items-center justify-center overflow-hidden bg-muted', className)}
    >
      {failed ? (
        <div
          aria-hidden="true"
          className={cn('flex h-full w-full flex-col items-center justify-center gap-2 text-muted-foreground', imgClassName)}
        >
          <ImageOff className="size-8" />
          {showBrokenHint && <span className="text-xs font-medium">تصویر در دسترس نیست</span>}
        </div>
      ) : protectedSource ? (
        blobUrl ? (
          <img
            src={blobUrl}
            alt=""
            loading="eager"
            onError={() => setFailed(true)}
            className={cn('h-full w-full object-cover', imgClassName)}
          />
        ) : (
          <div aria-hidden="true" className={cn('h-full w-full animate-pulse bg-muted/70 motion-reduce:animate-none', imgClassName)} />
        )
      ) : (
        <img
          src={image.contentUrl}
          alt=""
          loading="eager"
          onError={() => setFailed(true)}
          className={cn('h-full w-full object-cover', imgClassName)}
        />
      )}
    </div>
  )
}
