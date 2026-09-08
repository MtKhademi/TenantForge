/**
 * S07 tenant membership — the fixed request/response contract (F010).
 *
 * F010 mocks these exact fields; F011 connects the real B007 endpoints. The
 * shape here is the smallest contract B007 must implement, so it is the
 * source of truth both sides keep in sync.
 */

/**
 * Tenant lifecycle at this stage. F010 only ever creates `Active`;
 * `Suspended` exists in the contract so B007 can introduce it without a
 * shape change.
 */
export type TenantStatus = 'Active' | 'Suspended'

/**
 * A platform-scoped tenant summary. Deliberately contains **no member
 * details** — just the membership count; member records are a later slice.
 */
export type TenantSummary = {
  id: string
  name: string
  /** Normalized (trimmed, lower-cased, dash-joined) slug. Unique per platform. */
  slug: string
  status: TenantStatus
  /** How many memberships (users) the tenant holds. */
  memberCount: number
  /** UTC timestamp of creation (ISO 8601). */
  createdAtUtc: string
}

/** `GET /api/platform/tenants` — first-page collection. */
export type TenantListResponse = {
  tenants: TenantSummary[]
}

/** `POST /api/platform/tenants` request body. */
export type CreateTenantRequest = {
  name: string
  /** Must already be normalized client-side; the server re-normalizes. */
  slug: string
  /**
   * The existing platform user who becomes the tenant's first Owner.
   * B007 creates the tenant and this membership atomically.
   */
  ownerUserId: string
}

/**
 * Raised on a duplicate normalized-slug conflict (HTTP 409 in F011). The
 * message is a stable, user-facing contract string — no server internals.
 */
export class TenantConflictError extends Error {
  constructor(message = 'مستأجری با این شناسه از قبل وجود دارد.') {
    super(message)
    this.name = 'TenantConflictError'
  }
}

/**
 * S11 tenant discovery — the fixed contract (F018, connected to B012).
 *
 * `GET /api/auth/me/tenants` returns the **caller's own** active-tenant
 * memberships. These DTOs are deliberately separate from the richer platform
 * `TenantSummary` above: they carry only the fields the switcher and the
 * in-shell membership chooser need — no `memberCount`, no `createdAtUtc`. A
 * platform administrator receives exactly their own memberships here, never a
 * bypass to the full platform list.
 */

/** The caller's own membership kind for a tenant. */
export type MembershipRole = 'Owner' | 'Member'

/** One row of the caller's membership list. */
export type MyTenant = {
  id: string
  name: string
  slug: string
  /** Always `Active` — suspended tenants are excluded, not returned. */
  status: 'Active'
  /** The caller's own membership kind for this tenant. */
  membershipRole: MembershipRole
}

/** `GET /api/auth/me/tenants` — the caller's active memberships (may be `[]`). */
export type MyTenantListResponse = {
  tenants: MyTenant[]
}

/**
 * S08 tenant isolation — the fixed request/response contract (F012).
 *
 * `GET /api/tenants/{tenantId}/members` returns the requested tenant's context
 * plus its member list, but **only** when the authenticated user holds an
 * active membership in that tenant. `tenantId` is the tenant's Guid; there is
 * no "my memberships" endpoint, so the client can prove access only by calling
 * this endpoint. `200` carries the data below; `401` is missing/invalid auth;
 * `403` covers missing membership, an unknown/inactive tenant, or a malformed
 * id — all non-leaking and indistinguishable from the client's side.
 */

/**
 * Membership role in a tenant's member list. The S11 discovery contract
 * standardizes the membership kind as `Owner | Member`, so the member list
 * uses the same union (B008 issues `Owner`; a `Member` membership is now a
 * valid, expected value rather than an open-ended string).
 */
export type TenantMemberRole = MembershipRole

/** The requested tenant's identity, as confirmed by the server. */
export type TenantContext = {
  id: string
  name: string
  slug: string
  status: TenantStatus
}

/** One row of a tenant's member list. `userId` is the platform account id. */
export type TenantMember = {
  /** Membership record id (stable row key). */
  id: string
  userId: string
  email: string
  displayName: string
  role: TenantMemberRole
  /** UTC timestamp of the membership (ISO 8601). */
  createdAtUtc: string
}

/** `GET /api/tenants/{tenantId}/members` — authenticated, authorized response. */
export type TenantMembersResponse = {
  tenant: TenantContext
  members: TenantMember[]
}
