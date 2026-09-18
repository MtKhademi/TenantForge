import { ApiUnavailableError } from '@/features/auth/authTypes'
import { clearCartId, getCartId, setCartId } from './cartStorage'
import { InsufficientStockError, type CartResponse } from './cartTypes'

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return { signal: controller.signal, clear: () => window.clearTimeout(timeoutId) }
}

async function request(path: string, init?: RequestInit): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    return await fetch(path, { ...init, signal: abort.signal })
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

async function createCart(tenantId: string): Promise<string> {
  const response = await request(`/api/shop/${tenantId}/carts`, { method: 'POST' })
  if (!response.ok) throw new ApiUnavailableError()
  const body = (await readJson(response)) as { cartId: string }
  setCartId(body.cartId)
  return body.cartId
}

/**
 * Resolves a usable cart id: the stored one if present, otherwise a
 * freshly created one (also persisting it). Every mutating call below
 * goes through this first.
 */
async function ensureCartId(tenantId: string): Promise<string> {
  const stored = getCartId()
  if (stored) return stored
  return createCart(tenantId)
}

async function parseCartResponse(response: Response): Promise<CartResponse> {
  if (response.status === 409) throw new InsufficientStockError()
  if (!response.ok) throw new ApiUnavailableError()
  return (await readJson(response)) as CartResponse
}

export const cartAdapter = {
  /**
   * Adds a variant to the cart. If no cart id is stored yet, creates one
   * first. If the stored cart id is rejected (404 — e.g. it no longer
   * exists after a later task clears it post-order), clears it and
   * retries once against a freshly created cart, transparently.
   */
  async addItem(tenantId: string, productVariantId: string, quantity: number): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ productVariantId, quantity }),
    })
    if (response.status === 404) {
      clearCartId()
      const freshCartId = await createCart(tenantId)
      const retry = await request(`/api/shop/${tenantId}/carts/${freshCartId}/items`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ productVariantId, quantity }),
      })
      return parseCartResponse(retry)
    }
    return parseCartResponse(response)
  },

  async updateItemQuantity(tenantId: string, itemId: string, quantity: number): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items/${itemId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ quantity }),
    })
    return parseCartResponse(response)
  },

  async removeItem(tenantId: string, itemId: string): Promise<CartResponse> {
    const cartId = await ensureCartId(tenantId)
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}/items/${itemId}`, { method: 'DELETE' })
    return parseCartResponse(response)
  },

  async getCart(tenantId: string): Promise<CartResponse | null> {
    const cartId = getCartId()
    if (!cartId) return null
    const response = await request(`/api/shop/${tenantId}/carts/${cartId}`)
    if (response.status === 404) {
      clearCartId()
      return null
    }
    return parseCartResponse(response)
  },
}
