import type {
  ProductGallery,
  ReorderProductImagesRequest,
  ShopProductWithGallery,
} from '../contracts/mediaContract'

/**
 * S32 product media — the capability port consumed by the UI (F044).
 *
 * The mock phase (`mockShopMediaClient`) and the later connection phase
 * (`httpShopMediaClient`, F054) both satisfy this same interface, so
 * components receive identical parsed results in both phases and never call
 * `fetch` themselves. Every method is abort-aware: the caller may pass an
 * `AbortSignal` and an in-flight call must reject with a DOM `AbortError`
 * (name `'AbortError'`) so superseded requests never write stale UI.
 *
 * Method-to-route map (B036, `docs/design/shop/http-contracts.md` §B036):
 * - `getProduct`            → GET  /api/tenants/{tenantId}/shop/products/{productId} (gallery-extended)
 * - `getProtectedContent`   → GET  /api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content
 * - `upload`                → POST /api/tenants/{tenantId}/shop/products/{productId}/images
 * - `reorder`               → PUT  /api/tenants/{tenantId}/shop/products/{productId}/images/order
 * - `remove`                → DELETE /api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}
 */
export interface ShopMediaClient {
  getProduct(tenantId: string, productId: string, signal?: AbortSignal): Promise<ShopProductWithGallery>
  getProtectedContent(
    tenantId: string,
    productId: string,
    imageId: string,
    signal?: AbortSignal,
  ): Promise<Blob>
  upload(
    tenantId: string,
    productId: string,
    file: File,
    altText: string,
    expectedGalleryVersion: number,
    signal?: AbortSignal,
  ): Promise<ProductGallery>
  reorder(
    tenantId: string,
    productId: string,
    body: ReorderProductImagesRequest,
    signal?: AbortSignal,
  ): Promise<ProductGallery>
  remove(
    tenantId: string,
    productId: string,
    imageId: string,
    expectedGalleryVersion: number,
    signal?: AbortSignal,
  ): Promise<void>
}

/**
 * Shared abort-aware delay for mock Shop clients.
 *
 * Resolves after `ms` unless `signal` aborts first, in which case it rejects
 * with the browser's own `AbortError` shape (DOMException, name 'AbortError')
 * — the same shape a real `fetch` rejection produces, so callers handle mock
 * and HTTP aborts identically. Used by every mock client in
 * `features/shop/clients/` so no component writes its own timer logic.
 */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const timer = window.setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    function onAbort() {
      window.clearTimeout(timer)
      reject(new DOMException('Aborted', 'AbortError'))
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/** Decoded dimensions of a local image file (0×0 when the browser cannot decode it). */
export type ImageFileDimensions = {
  width: number
  height: number
  /** True for the image content types B036's validator accepts (JPEG/PNG/WebP). */
  isSupported: boolean
}

/**
 * Reads a local file's real image dimensions by drawing it to a canvas — the
 * mock phase's stand-in for B036's server-side `ShopImageValidator` decode.
 * Never trusts the file name; a non-image or undecodable file yields
 * `isSupported: false` so the UI can surface the 415-style state.
 */
export function readImageFile(file: File): Promise<ImageFileDimensions> {
  return new Promise((resolve) => {
    if (!file.type.startsWith('image/') || file.type === 'image/svg+xml') {
      resolve({ width: 0, height: 0, isSupported: false })
      return
    }
    const url = URL.createObjectURL(file)
    const image = new Image()
    image.onload = () => {
      URL.revokeObjectURL(url)
      resolve({ width: image.naturalWidth, height: image.naturalHeight, isSupported: true })
    }
    image.onerror = () => {
      URL.revokeObjectURL(url)
      resolve({ width: 0, height: 0, isSupported: false })
    }
    image.src = url
  })
}

/**
 * Re-encodes a supported image file to WebP in the browser — the mock phase's
 * stand-in for B036's server re-encode. Falls back to the original bytes when
 * the canvas codec is unavailable so a mock upload never hard-fails on
 * encode-only reasons (the validator scenario drives 415, not encoding).
 */
export function encodeImageAsWebP(file: File): Promise<Blob> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file)
    const image = new Image()
    image.onload = () => {
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth || 1
      canvas.height = image.naturalHeight || 1
      const context = canvas.getContext('2d')
      if (!context) {
        URL.revokeObjectURL(url)
        resolve(file)
        return
      }
      context.drawImage(image, 0, 0)
      URL.revokeObjectURL(url)
      canvas.toBlob(
        (blob) => resolve(blob ?? file),
        'image/webp',
        0.85,
      )
    }
    image.onerror = () => {
      URL.revokeObjectURL(url)
      resolve(file)
    }
    image.src = url
  })
}
