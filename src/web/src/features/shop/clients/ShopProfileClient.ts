import type { ShopProfile, SaveShopProfileRequest, PublicShopProfile } from '../contracts/shopProfileContract'

/**
 * Client interface for shop profile operations.
 * Every method is abort-aware via the optional signal parameter.
 */
export interface ShopProfileClient {
  /**
   * Fetch the admin profile for a tenant.
   * Returns null when no profile has been created yet (200 OK).
   */
  getAdmin(tenantId: string, signal?: AbortSignal): Promise<ShopProfile | null>

  /**
   * Save (create or update) the shop profile.
   * expectedVersion: null for create, current version for update.
   * Returns 409 Conflict when expectedVersion is stale.
   */
  save(tenantId: string, body: SaveShopProfileRequest, signal?: AbortSignal): Promise<ShopProfile>

  /**
   * Fetch the public profile for anonymous storefront.
   * Returns null when profile is missing or unpublished (404 becomes null).
   */
  getPublic(tenantId: string, signal?: AbortSignal): Promise<PublicShopProfile | null>
}
