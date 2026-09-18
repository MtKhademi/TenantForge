/**
 * S27 cart-id persistence (F033). Mirrors httpAuthAdapter.ts's shape
 * (a small typed module, JSON-serialized, guarded by try/catch) but
 * deliberately uses localStorage, not sessionStorage: a shopping cart
 * should survive a closed tab/browser restart, unlike an auth session.
 */
const CART_ID_KEY = 'tenantforge:shop:cartId'

export function getCartId(): string | null {
  try {
    return window.localStorage.getItem(CART_ID_KEY)
  } catch {
    return null
  }
}

export function setCartId(cartId: string): void {
  try {
    window.localStorage.setItem(CART_ID_KEY, cartId)
  } catch {
    // Storage unavailable (private mode, quota, etc.) — the cart still
    // works for the current page's lifetime via in-memory adapter state;
    // it simply will not survive a reload. Silently ignored, matching
    // the design system's existing browser-storage guidance.
  }
}

export function clearCartId(): void {
  try {
    window.localStorage.removeItem(CART_ID_KEY)
  } catch {
    // See setCartId — best-effort only.
  }
}
