import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import { isPermissionKey } from './permissionCatalog'
import {
  TenantRoleConflictError,
  TenantRoleForbiddenError,
  TenantRoleInvariantError,
  TenantRoleValidationError,
  type CreateTenantRoleRequest,
  type PermissionKey,
  type TenantRole,
  type TenantRoleListResponse,
  type UpdateTenantRoleRequest,
} from './roleTypes'

/**
 * S09 role and permission matrix — real API data source (F014).
 *
 * F013's sessionStorage mock is gone. This adapter calls the B009 tenant role
 * endpoints with the current bearer token and validates responses strictly so
 * the page never renders half-parsed role or assignment data. UI visibility is
 * still only presentation: B009 owns authorization and returns 403/409 for
 * denied role management and last-effective-Owner protection.
 */
export type RoleAdapter = {
  listRoles(accessToken: string, tenantId: string): Promise<TenantRoleListResponse>
  createRole(accessToken: string, tenantId: string, request: CreateTenantRoleRequest): Promise<TenantRole>
  updateRole(accessToken: string, tenantId: string, roleId: string, request: UpdateTenantRoleRequest): Promise<TenantRole>
  assignRole(accessToken: string, tenantId: string, memberId: string, roleId: string): Promise<TenantRoleListResponse>
  unassignRole(accessToken: string, tenantId: string, memberId: string, roleId: string): Promise<TenantRoleListResponse>
  getCurrentTenantPermissions(accessToken: string, tenantId: string): Promise<{ permissions: PermissionKey[] }>
}

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

function rolePath(tenantId: string) {
  return `/api/tenants/${encodeURIComponent(tenantId)}/roles`
}

function roleDetailPath(tenantId: string, roleId: string) {
  return `${rolePath(tenantId)}/${encodeURIComponent(roleId)}`
}

function assignmentPath(tenantId: string, memberId: string, roleId: string) {
  return `/api/tenants/${encodeURIComponent(tenantId)}/members/${encodeURIComponent(memberId)}/roles/${encodeURIComponent(roleId)}`
}

function currentPermissionsPath(tenantId: string) {
  return `/api/tenants/${encodeURIComponent(tenantId)}/me/permissions`
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
}

async function request(path: string, init: RequestInit) {
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

function parseRole(payload: unknown): TenantRole {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const role = payload as Record<string, unknown>
  if (
    typeof role.id !== 'string' ||
    role.id.length === 0 ||
    typeof role.name !== 'string' ||
    role.name.length === 0 ||
    typeof role.description !== 'string' ||
    (role.kind !== 'builtIn' && role.kind !== 'custom') ||
    !Array.isArray(role.permissionKeys) ||
    !role.permissionKeys.every(isPermissionKey) ||
    !Array.isArray(role.memberIds) ||
    !role.memberIds.every((value) => typeof value === 'string') ||
    typeof role.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(role.createdAtUtc)) ||
    typeof role.updatedAtUtc !== 'string' ||
    Number.isNaN(Date.parse(role.updatedAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: role.id,
    name: role.name,
    description: role.description,
    kind: role.kind,
    permissionKeys: role.permissionKeys,
    memberIds: role.memberIds,
    createdAtUtc: role.createdAtUtc,
    updatedAtUtc: role.updatedAtUtc,
  }
}

function parseRoleList(payload: unknown): TenantRoleListResponse {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.roles)) throw new ApiUnavailableError()
  return { roles: body.roles.map(parseRole) }
}

function parseCurrentPermissions(payload: unknown): { permissions: PermissionKey[] } {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.permissions) || !body.permissions.every(isPermissionKey)) {
    throw new ApiUnavailableError()
  }
  return { permissions: body.permissions }
}

function mapServerValidation(payload: unknown): Partial<Record<keyof CreateTenantRoleRequest, string>> {
  const fallback = 'مقدار واردشده معتبر نیست.'
  if (typeof payload !== 'object' || payload === null) return { name: fallback }
  const body = payload as Record<string, unknown>
  const errors = body.errors
  if (typeof errors !== 'object' || errors === null) return { name: fallback }
  const mapped: Partial<Record<keyof CreateTenantRoleRequest, string>> = {}
  for (const field of ['name', 'permissionKeys'] as const) {
    const value = (errors as Record<string, unknown>)[field]
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
    else if (typeof value === 'string') mapped[field] = value
  }
  return Object.keys(mapped).length > 0 ? mapped : { name: fallback }
}

async function mapRoleResponse(response: Response): Promise<never | null> {
  if (response.status === 401) throw new SessionExpiredError()
  if (response.status === 403) throw new TenantRoleForbiddenError()
  if (response.status === 409) throw new TenantRoleInvariantError()
  if (!response.ok) throw new ApiUnavailableError()
  return null
}

export const httpRoleAdapter: RoleAdapter = {
  async listRoles(accessToken, tenantId) {
    const response = await request(rolePath(tenantId), {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    await mapRoleResponse(response)
    return parseRoleList(await readJson(response))
  },

  async createRole(accessToken, tenantId, body) {
    const response = await request(rolePath(tenantId), {
      method: 'POST',
      headers: { ...authHeaders(accessToken), 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (response.status === 400) throw new TenantRoleValidationError(mapServerValidation(await readJson(response)))
    if (response.status === 409) throw new TenantRoleConflictError()
    await mapRoleResponse(response)
    return parseRole(await readJson(response))
  },

  async updateRole(accessToken, tenantId, roleId, body) {
    const response = await request(roleDetailPath(tenantId, roleId), {
      method: 'PUT',
      headers: { ...authHeaders(accessToken), 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (response.status === 400) throw new TenantRoleValidationError(mapServerValidation(await readJson(response)))
    if (response.status === 409) throw new TenantRoleConflictError('مجوزهای نقش سیستمی قابل تغییر نیست.')
    await mapRoleResponse(response)
    return parseRole(await readJson(response))
  },

  async assignRole(accessToken, tenantId, memberId, roleId) {
    const response = await request(assignmentPath(tenantId, memberId, roleId), {
      method: 'PUT',
      headers: authHeaders(accessToken),
    })
    await mapRoleResponse(response)
    return parseRoleList(await readJson(response))
  },

  async unassignRole(accessToken, tenantId, memberId, roleId) {
    const response = await request(assignmentPath(tenantId, memberId, roleId), {
      method: 'DELETE',
      headers: authHeaders(accessToken),
    })
    await mapRoleResponse(response)
    return parseRoleList(await readJson(response))
  },

  async getCurrentTenantPermissions(accessToken, tenantId) {
    const response = await request(currentPermissionsPath(tenantId), {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    await mapRoleResponse(response)
    return parseCurrentPermissions(await readJson(response))
  },
}
