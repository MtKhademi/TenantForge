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
  type PermissionCatalogResponse,
  type PermissionItem,
  type PermissionKey,
  type TenantRole,
  type TenantRoleListResponse,
  type UpdateTenantRoleRequest,
} from './roleTypes'

/**
 * S09 role and permission matrix — real API data source (F014), extended in
 * S12 (F019/B013).
 *
 * This adapter calls the B009/B013 tenant role endpoints with the current
 * bearer token and validates responses strictly so the page never renders
 * half-parsed role or assignment data. S12 adds the server permission catalog
 * (`GET /api/permissions/catalog`) as the only source of permission labels,
 * descriptions and grouping, and resolves the caller's current-tenant
 * permissions (`GET /api/tenants/{id}/me/permissions`) so navigation and
 * actions follow the server's decision. UI visibility is still only
 * presentation: B013 owns authorization and returns 403/409 for denied role
 * management and last-effective-role-administrator protection.
 */
export type RoleAdapter = {
  fetchPermissionCatalog(accessToken: string): Promise<PermissionCatalogResponse>
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

const CATALOG_PATH = '/api/permissions/catalog'

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

function parseCatalogPermission(payload: unknown): PermissionItem {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const permission = payload as Record<string, unknown>
  if (
    !isPermissionKey(permission.key) ||
    typeof permission.label !== 'string' ||
    permission.label.length === 0 ||
    typeof permission.description !== 'string' ||
    (permission.kind !== 'read' && permission.kind !== 'write')
  ) {
    throw new ApiUnavailableError()
  }
  return {
    key: permission.key,
    label: permission.label,
    description: permission.description,
    kind: permission.kind,
  }
}

function parseCatalog(payload: unknown): PermissionCatalogResponse {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.groups) || body.groups.length === 0) throw new ApiUnavailableError()
  return {
    groups: body.groups.map((group) => {
      if (typeof group !== 'object' || group === null) throw new ApiUnavailableError()
      const value = group as Record<string, unknown>
      if (
        typeof value.id !== 'string' ||
        value.id.length === 0 ||
        typeof value.label !== 'string' ||
        value.label.length === 0 ||
        typeof value.description !== 'string' ||
        !Array.isArray(value.permissions) ||
        value.permissions.length === 0
      ) {
        throw new ApiUnavailableError()
      }
      return {
        id: value.id,
        label: value.label,
        description: value.description,
        permissions: value.permissions.map(parseCatalogPermission),
      }
    }),
  }
}

/**
 * S12 (B013): a `409` on role create/update can mean either a duplicate role
 * name (server problem `Duplicate tenant role`) or the final-state
 * invariant — the mutation would remove the tenant's last effective role
 * administrator (plain `Results.Conflict()`). The body is the only signal,
 * so the UI can keep an unsaved draft on the invariant case and surface the
 * name conflict on the field.
 */
async function isDuplicateRoleConflict(response: Response): Promise<boolean> {
  try {
    const body = (await response.json()) as Record<string, unknown>
    return body?.title === 'Duplicate tenant role'
  } catch {
    return false
  }
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
  async fetchPermissionCatalog(accessToken) {
    const response = await request(CATALOG_PATH, {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseCatalog(await readJson(response))
  },

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
    if (response.status === 409) {
      if (await isDuplicateRoleConflict(response)) throw new TenantRoleConflictError()
      throw new TenantRoleInvariantError()
    }
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
    if (response.status === 409) {
      if (await isDuplicateRoleConflict(response)) throw new TenantRoleConflictError()
      // Built-in roles and the last-effective-administrator protection both
      // answer with a plain conflict; the UI keeps the draft in both cases.
      throw new TenantRoleInvariantError()
    }
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
