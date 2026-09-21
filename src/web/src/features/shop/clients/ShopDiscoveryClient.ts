import type { DiscoveryQuery, StorefrontProductList } from '../contracts/discoveryContract'

/**
 * S33 storefront discovery — the one client port the UI consumes (F045 mock
 * phase; F055 binds HTTP).
 *
 * The UI receives this interface through `ShopClientsProvider` and never
 * imports a fixture or calls `fetch` directly. The mock implementation and the
 * later HTTP implementation must both satisfy this exact method signature;
 * the optional `AbortSignal` makes the call abort-aware so a superseded
 * request can be cancelled instead of writing stale data back into the page.
 */
export interface ShopDiscoveryClient {
  listProducts(tenantId: string, query: DiscoveryQuery, signal?: AbortSignal): Promise<StorefrontProductList>
}
