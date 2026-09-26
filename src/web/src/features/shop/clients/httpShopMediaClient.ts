import {
  productGallerySchema,
  shopProductWithGallerySchema,
} from '../contracts/mediaContract'
import { shopFetch, shopFetchBlob } from './shopFetch'
import type { ShopMediaClient } from './ShopMediaClient'

/**
 * S32 product media — HTTP implementation of `ShopMediaClient` (F054).
 *
 * Binds the accepted F044 gallery UI to B036's delivered admin routes. Every
 * path segment is URL-encoded, every query string is built with
 * `URLSearchParams`, the caller's `AbortSignal` is passed straight through,
 * and every successful JSON payload is parsed through the SAME F044 Zod
 * schemas the mock client satisfies. Every non-2xx response is normalized by
 * the shared `shopFetch` failure path into the single `ShopClientError`
 * (400/403/404/409/413/415, …) — there is no per-status branching here.
 *
 * All five routes are admin routes, so they all go through the authenticated
 * `shopFetch`/`shopFetchBlob` helpers (bearer token from the session). This
 * task binds the `media` slot only; every other capability slot stays on its
 * mock until its own connection task. No `any`, no unchecked cast, no
 * component-owned `fetch`.
 */

const productPath = (tenantId: string, productId: string) =>
  `/api/tenants/${encodeURIComponent(tenantId)}/shop/products/${encodeURIComponent(productId)}`

export const httpShopMediaClient: ShopMediaClient = {
  async getProduct(tenantId, productId, signal) {
    return shopProductWithGallerySchema.parse(
      await shopFetch(productPath(tenantId, productId), { signal }),
    )
  },

  async getProtectedContent(tenantId, productId, imageId, signal) {
    return shopFetchBlob(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}/content`,
      { signal },
    )
  },

  async upload(tenantId, productId, file, altText, expectedGalleryVersion, signal) {
    const body = new FormData()
    body.set('file', file)
    body.set('altText', altText)
    body.set('expectedGalleryVersion', String(expectedGalleryVersion))
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images`,
      { method: 'POST', body, signal },
    ))
  },

  async reorder(tenantId, productId, body, signal) {
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images/order`,
      { method: 'PUT', json: body, signal },
    ))
  },

  async remove(tenantId, productId, imageId, expectedGalleryVersion, signal) {
    const query = new URLSearchParams({ expectedGalleryVersion: String(expectedGalleryVersion) })
    await shopFetch(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}?${query}`,
      { method: 'DELETE', signal },
    )
  },
}
