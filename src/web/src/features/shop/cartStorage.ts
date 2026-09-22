/**
 * S27 cart-id persistence (F033), S36 tenant isolation (F048). Mirrors
 * httpAuthAdapter.ts's shape (a small typed module, guarded by try/catch) but
 * deliberately uses localStorage, not sessionStorage: a shopping cart should
 * survive a closed tab/browser restart, unlike an auth session.
 *
 * The key itself is shared, but the VALUE is a per-tenant record
 * (`{ "<tenantId>": "<cartId>" }`): two tenants in the same browser keep two
 * independent carts, and clearing one tenant's entry (cart expiry, F048)
 * leaves every other tenant's entry untouched. The record is rewritten
 * whole on every write, so there is no per-key bookkeeping to desync.
 */
const CART_ID_KEY = 'tenantforge:shop:cartId'

type CartIdRecord = Record<string, string>

function readRecord(): CartIdRecord {
  try {
    const raw = window.localStorage.getItem(CART_ID_KEY)
    if (!raw) return {}
    const parsed: unknown = JSON.parse(raw)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return {}
    const record: CartIdRecord = {}
    for (const [tenantId, cartId] of Object.entries(parsed as Record<string, unknown>)) {
      if (typeof cartId === 'string' && cartId.length > 0) record[tenantId] = cartId
    }
    return record
  } catch {
    // Corrupt or unavailable storage: treat as empty, matching the
    // best-effort guidance below.
    return {}
  }
}

function writeRecord(record: CartIdRecord): void {
  try {
    window.localStorage.setItem(CART_ID_KEY, JSON.stringify(record))
  } catch {
    // Storage unavailable (private mode, quota, etc.) — the cart still
    // works for the current page's lifetime via in-memory adapter state;
    // it simply will not survive a reload. Silently ignored, matching
    // the design system's existing browser-storage guidance.
  }
}

/** This tenant's stored cart id, or null when none is stored yet. */
export function getCartId(tenantId: string): string | null {
  return readRecord()[tenantId] ?? null
}

/**
 * Returns this tenant's stored cart id, minting and persisting one when none
 * exists yet. The id is an opaque 13-character handle (the real value comes
 * from B028's `POST /carts`; in the mock phase the client is the source of
 * truth and only needs a stable, tenant-unique handle). A fresh id per new
 * session is what lets the mock start a post-expiry session from a clean
 * slate instead of resurrecting the expired cart.
 */
// Crockford base32 alphabet (32 chars; excludes I, L, O, U so the string can
// never collide with a GUID-shaped or ambiguous reading). A canonical 13-char
// TSID on every transport boundary (TenantForge.BuildingBlocks.TsidId).
const CROCKFORD = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'

function toCrockford(value: number, length: number): string {
  let out = ''
  let n = value
  while (out.length < length && n > 0) {
    out = CROCKFORD[n % CROCKFORD.length] + out
    n = Math.floor(n / CROCKFORD.length)
  }
  return out.padStart(length, CROCKFORD[0])
}

let cartIdCounter = 0
export function getOrCreateCartId(tenantId: string): string {
  const existing = getCartId(tenantId)
  if (existing) return existing
  // Canonical 13-character TSID-style handle: 8 time chars + 5 monotonic
  // chars, both in the Crockford alphabet. Deterministic within a page
  // lifetime and unique per call.
  cartIdCounter += 1
  const fresh = toCrockford(Date.now(), 8) + toCrockford(cartIdCounter, 5)
  setCartId(tenantId, fresh)
  return fresh
}

/** Store this tenant's cart id, leaving every other tenant's entry intact. */
export function setCartId(tenantId: string, cartId: string): void {
  const record = readRecord()
  record[tenantId] = cartId
  writeRecord(record)
}

/**
 * Clear ONLY this tenant's cart id — every other tenant's stored cart is
 * left untouched. Used by the B040 expiry recovery flow (F048).
 */
export function clearCartId(tenantId: string): void {
  const record = readRecord()
  if (!(tenantId in record)) return
  delete record[tenantId]
  writeRecord(record)
}
