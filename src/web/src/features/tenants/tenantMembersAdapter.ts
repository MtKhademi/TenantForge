import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import {
  type TenantContext,
  type TenantMember,
  type TenantMembersResponse,
  type TenantStatus,
} from './tenantTypes'

/**
 * S08 tenant isolation — real API data source (F012).
 *
 * The tenant-scoped page calls B008's `GET /api/tenants/{tenantId}/members`
 * with the current session bearer token. B008 enforces isolation on the
 * server: the request returns the tenant's members only when the
 * authenticated account holds an active membership in that tenant.
 * `isPlatformAdmin` is deliberately NOT a bypass, and a 403 never reveals
 * whether the tenant exists or who belongs to it — the client can only prove
 * access by calling the endpoint.
 */
export type TenantMembersAdapter = {
  getTenantMembers(accessToken: string, tenantId: string): Promise<TenantMembersResponse>
}

const REQUEST_TIMEOUT_MS = 8_000

/**
 * Raised when B008 answers `403` for a tenant scope: missing membership, an
 * unknown/inactive tenant, or a malformed tenant id. The message is the
 * stable, non-leaking contract string shown by the designed 403 state.
 */
export class TenantAccessDeniedError extends Error {
  constructor(message = 'دسترسی به این محدوده مجاز نیست.') {
    super(message)
    this.name = 'TenantAccessDeniedError'
  }
}

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

function isTenantStatus(value: unknown): value is TenantStatus {
  return value === 'Active' || value === 'Suspended'
}

/**
 * B008 emits `createdAtUtc` as a .NET "O" UTC value. The value is UTC by
 * contract; if it ever arrives as a bare date-time with no zone, mark the zone
 * explicitly so `Date.parse` does not read it as local time.
 */
function normalizeUtcTimestamp(value: string): string {
  if (/(Z|[+-]\d{2}:?\d{2})$/i.test(value)) return value
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/.test(value)) {
    return `${value}Z`
  }
  return value
}

function parseTenantContext(payload: unknown): TenantContext {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const tenant = payload as Record<string, unknown>
  if (
    typeof tenant.id !== 'string' ||
    tenant.id.length === 0 ||
    typeof tenant.name !== 'string' ||
    tenant.name.length === 0 ||
    typeof tenant.slug !== 'string' ||
    tenant.slug.length === 0 ||
    !isTenantStatus(tenant.status)
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: tenant.id,
    name: tenant.name,
    slug: tenant.slug,
    status: tenant.status,
  }
}

function parseMember(payload: unknown): TenantMember {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const member = payload as Record<string, unknown>
  if (
    typeof member.id !== 'string' ||
    member.id.length === 0 ||
    typeof member.userId !== 'string' ||
    member.userId.length === 0 ||
    typeof member.email !== 'string' ||
    member.email.length === 0 ||
    typeof member.displayName !== 'string' ||
    member.displayName.length === 0 ||
    typeof member.role !== 'string' ||
    member.role.length === 0 ||
    typeof member.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(member.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: member.id,
    userId: member.userId,
    email: member.email,
    displayName: member.displayName,
    role: member.role as TenantMember['role'],
    createdAtUtc: normalizeUtcTimestamp(member.createdAtUtc),
  }
}

function parseMembersResponse(payload: unknown): TenantMembersResponse {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.members)) {
    throw new ApiUnavailableError()
  }
  return { tenant: parseTenantContext(body.tenant), members: body.members.map(parseMember) }
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

export const httpTenantMembersAdapter: TenantMembersAdapter = {
  async getTenantMembers(accessToken, tenantId) {
    const path = `/api/tenants/${encodeURIComponent(tenantId)}/members`
    const response = await request(path, {
      method: 'GET',
      headers: { Authorization: `Bearer ${accessToken}` },
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new TenantAccessDeniedError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseMembersResponse(await readJson(response))
  },
}
