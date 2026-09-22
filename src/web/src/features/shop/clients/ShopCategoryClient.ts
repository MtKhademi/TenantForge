import type { AdminCategory, PublicCategory, SaveCategoryRequest } from '../contracts/categoryHierarchyContract'

/**
 * S34 category hierarchy — the one client port the UI consumes (F046 mock
 * phase; F056 binds HTTP).
 *
 * The UI receives this interface through `ShopClientsProvider` and never
 * imports a fixture or calls `fetch` directly. The mock implementation and the
 * later HTTP implementation must both satisfy this exact method signature;
 * the optional `AbortSignal` makes every call abort-aware so a superseded
 * request can be cancelled instead of writing stale data back into the page.
 *
 * `create` never accepts `isActive` (a new category's active state is decided
 * by the backend); `update` does, so an existing category can be toggled.
 */
export interface ShopCategoryClient {
  listAdmin(tenantId: string, signal?: AbortSignal): Promise<AdminCategory[]>
  create(tenantId: string, body: Omit<SaveCategoryRequest, 'isActive'>, signal?: AbortSignal): Promise<AdminCategory>
  update(tenantId: string, categoryId: string, body: SaveCategoryRequest, signal?: AbortSignal): Promise<AdminCategory>
  listPublic(tenantId: string, signal?: AbortSignal): Promise<PublicCategory[]>
}
