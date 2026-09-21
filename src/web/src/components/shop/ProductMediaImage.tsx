import { ImageOff } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ProductImage } from '@/features/shop/contracts/mediaContract'
import { cn } from '@/lib/utils'

export function ProductMediaImage({
  image,
  className,
  imgClassName,
  showBrokenHint = true,
}: {
  image: ProductImage
  className?: string
  imgClassName?: string
  showBrokenHint?: boolean
}) {
  const [failed, setFailed] = useState(false)

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
          className={cn('flex h-full w-full flex-col items-center justify-center gap-2 text-muted-foreground', imgClassName)}
        >
          <ImageOff className="size-8" />
          {showBrokenHint && <span className="text-xs font-medium">تصویر در دسترس نیست</span>}
        </div>
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
