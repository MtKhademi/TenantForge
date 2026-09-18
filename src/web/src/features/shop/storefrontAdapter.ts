import { ApiUnavailableError } from '@/features/auth/authTypes'
import type { StorefrontCategory, StorefrontProductDetail, StorefrontProductSummary } from './storefrontTypes'

/**
 * S26 storefront browsing — real, anonymous API data source (F031),
 * replacing F030's mock. No Authorization header is ever sent from any
 * function in this file — the storefront works identically fully
 * signed out.
 */
const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

async function request(path: string): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    // No headers object at all — confirms no Authorization is ever attached.
    return await fetch(path, { method: 'GET', signal: abort.signal })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

export const storefrontAdapter = {
  async listCategories(tenantId: string): Promise<StorefrontCategory[]> {
    const response = await request(`/api/shop/${tenantId}/categories`)
    if (!response.ok) throw new ApiUnavailableError()
    const body = (await readJson(response)) as { categories: StorefrontCategory[] }
    return body.categories
  },

  async listProducts(tenantId: string, categorySlug: string): Promise<StorefrontProductSummary[]> {
    const response = await request(
      `/api/shop/${tenantId}/categories/${categorySlug}/products?pageNumber=1&pageSize=50`,
    )
    if (response.status === 404) return []
    if (!response.ok) throw new ApiUnavailableError()
    const body = (await readJson(response)) as { products: StorefrontProductSummary[] }
    return body.products
  },

  async getProduct(tenantId: string, productSlug: string): Promise<StorefrontProductDetail | null> {
    const response = await request(`/api/shop/${tenantId}/products/${productSlug}`)
    if (response.status === 404) return null
    if (!response.ok) throw new ApiUnavailableError()
    return (await readJson(response)) as StorefrontProductDetail
  },
}
