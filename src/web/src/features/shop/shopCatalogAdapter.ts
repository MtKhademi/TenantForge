import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import type { ShopCatalogAdapter } from './shopCatalogTypes'
import {
  CategoryConflictError,
  ProductConflictError,
  type CreateCategoryRequest,
  type ProductFormValues,
  type ShopCategory,
  type ShopProduct,
  type ShopProductSummary,
  type UpdateCategoryRequest,
} from './shopCatalogTypes'

/**
 * S26 admin catalog management — real API data source (F029), replacing
 * F028's mock. Calls B026's tenant-scoped admin endpoints with the
 * current session bearer token.
 */
const REQUEST_TIMEOUT_MS = 8_000

export class ShopForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت فروشگاه این مستأجر را ندارید.') {
    super(message)
    this.name = 'ShopForbiddenError'
  }
}

export class ShopValidationError extends Error {
  fieldErrors: Record<string, string>

  constructor(fieldErrors: Record<string, string>) {
    super('درخواست معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'ShopValidationError'
  }
}

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

async function request(path: string, init: RequestInit): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    return await fetch(path, { ...init, signal: abort.signal })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }
}

function mapServerValidation(payload: unknown): Record<string, string> {
  const fallback = { _: 'مقدار واردشده معتبر نیست.' }
  if (typeof payload !== 'object' || payload === null) return fallback
  const errors = (payload as Record<string, unknown>).errors
  if (typeof errors !== 'object' || errors === null) return fallback
  const mapped: Record<string, string> = {}
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
  }
  return Object.keys(mapped).length > 0 ? mapped : fallback
}

async function handleCommonErrors(response: Response): Promise<void> {
  if (response.status === 401) throw new SessionExpiredError()
  if (response.status === 403) throw new ShopForbiddenError()
}

/**
 * Every call needs the tenant's own accessToken, obtained by the calling
 * page from `useAuth()` — see this Spec's Context for where that comes
 * from. Pages pass it as the adapter's first argument, exactly mirroring
 * `httpUserAdapter.listUsers(accessToken, page)`'s own parameter order —
 * except the Shop catalog adapter's own `ShopCatalogAdapter` type (from
 * F028's `shopCatalogTypes.ts`) does not carry an accessToken parameter,
 * so this file's exported object wraps it: build one bound instance per
 * render with `createShopCatalogAdapter(accessToken)` below.
 */
export function createShopCatalogAdapter(accessToken: string): ShopCatalogAdapter {
  const headers = { ...authHeaders(accessToken), 'Content-Type': 'application/json' }

  return {
    async listCategories(tenantId) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories?pageNumber=1&pageSize=100`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { categories: ShopCategory[] }
      return body.categories
    },

    async createCategory(tenantId, req: CreateCategoryRequest) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories`, {
        method: 'POST',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new CategoryConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopCategory
    },

    async updateCategory(tenantId, categoryId, req: UpdateCategoryRequest) {
      const response = await request(`/api/tenants/${tenantId}/shop/categories/${categoryId}`, {
        method: 'PUT',
        headers,
        body: JSON.stringify(req),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new CategoryConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopCategory
    },

    async listProducts(tenantId) {
      const response = await request(`/api/tenants/${tenantId}/shop/products?pageNumber=1&pageSize=100`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (!response.ok) throw new ApiUnavailableError()
      const body = (await readJson(response)) as { products: ShopProductSummary[] }
      return body.products
    },

    async getProduct(tenantId, productId) {
      const response = await request(`/api/tenants/${tenantId}/shop/products/${productId}`, {
        method: 'GET',
        headers: authHeaders(accessToken),
      })
      await handleCommonErrors(response)
      if (response.status === 404) throw new ApiUnavailableError()
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },

    async createProduct(tenantId, values: ProductFormValues) {
      const response = await request(`/api/tenants/${tenantId}/shop/products`, {
        method: 'POST',
        headers,
        body: JSON.stringify(values),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new ProductConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },

    async updateProduct(tenantId, productId, values: ProductFormValues) {
      const response = await request(`/api/tenants/${tenantId}/shop/products/${productId}`, {
        method: 'PUT',
        headers,
        body: JSON.stringify(values),
      })
      await handleCommonErrors(response)
      if (response.status === 409) throw new ProductConflictError()
      if (response.status === 400) throw new ShopValidationError(mapServerValidation(await readJson(response)))
      if (!response.ok) throw new ApiUnavailableError()
      return (await readJson(response)) as ShopProduct
    },
  }
}
