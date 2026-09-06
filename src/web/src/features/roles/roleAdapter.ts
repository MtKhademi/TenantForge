import { ApiUnavailableError, SessionExpiredError } from '@/features/auth/authTypes'
import { PERMISSION_KEYS, isPermissionKey } from './permissionCatalog'
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
 * S09 role and permission matrix — mock data source (F013).
 *
 * F013 freezes the B009 contract while using an in-tab sessionStorage-backed
 * mock. The adapter keeps roles per tenant id, simulates latency so loading and
 * saving states are visible, and models server-side authorization decisions:
 * Owner access succeeds, `?roleViewer=member` receives a non-leaking 403, and
 * unassigning the final Owner is rejected. F014 replaces this adapter with HTTP
 * calls without changing page code or request/response shapes.
 */
export type RoleAdapter = {
  listRoles(accessToken: string, tenantId: string): Promise<TenantRoleListResponse>
  createRole(accessToken: string, tenantId: string, request: CreateTenantRoleRequest): Promise<TenantRole>
  updateRole(accessToken: string, tenantId: string, roleId: string, request: UpdateTenantRoleRequest): Promise<TenantRole>
  replaceDemoRoles(accessToken: string, tenantId: string, roles: TenantRole[]): Promise<TenantRoleListResponse>
  assignRole(accessToken: string, tenantId: string, memberId: string, roleId: string): Promise<TenantRoleListResponse>
  unassignRole(accessToken: string, tenantId: string, memberId: string, roleId: string): Promise<TenantRoleListResponse>
}

const STORAGE_KEY = 'tenantforge.roles.v1'
const MOCK_LATENCY_MS = 450
const OWNER_ROLE_ID = 'role-owner'
const VIEWER_ROLE_ID = 'role-viewer'
const ROLE_NAME_MAX = 64

function delay() {
  return new Promise<void>((resolve) => window.setTimeout(resolve, MOCK_LATENCY_MS))
}

function assertAuthorized(accessToken: string) {
  if (accessToken.trim().length === 0) throw new SessionExpiredError()
  const mode = new URLSearchParams(window.location.search).get('roleViewer')
  if (mode === 'member' || mode === 'denied') throw new TenantRoleForbiddenError()
}

type RoleStore = Record<string, TenantRole[]>

function nowUtc() {
  return new Date().toISOString()
}

function cloneRole(role: TenantRole): TenantRole {
  return {
    ...role,
    permissionKeys: [...role.permissionKeys],
    memberIds: [...role.memberIds],
  }
}

function seededRoles(): TenantRole[] {
  const timestamp = '2030-01-01T00:00:00Z'
  return [
    {
      id: OWNER_ROLE_ID,
      name: 'Owner',
      description: 'نقش سیستمی مالک؛ تمام مجوزهای مستأجر را دارد و آخرین مالک مؤثر محافظت می‌شود.',
      kind: 'builtIn',
      permissionKeys: [...PERMISSION_KEYS],
      memberIds: ['owner-member'],
      createdAtUtc: timestamp,
      updatedAtUtc: timestamp,
    },
    {
      id: VIEWER_ROLE_ID,
      name: 'Viewer',
      description: 'نقش سیستمی مشاهده‌گر؛ برای نمایش تفاوت مجوزهای خواندن و نوشتن.',
      kind: 'builtIn',
      permissionKeys: ['IAM.Dashboard.View'],
      memberIds: ['viewer-member'],
      createdAtUtc: timestamp,
      updatedAtUtc: timestamp,
    },
  ]
}

function parseRole(payload: unknown): TenantRole {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const role = payload as Record<string, unknown>
  if (
    typeof role.id !== 'string' ||
    typeof role.name !== 'string' ||
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
  return cloneRole(role as TenantRole)
}

function readStore(): RoleStore {
  let raw: string | null
  try {
    raw = window.sessionStorage.getItem(STORAGE_KEY)
  } catch {
    return {}
  }
  if (raw === null) return {}
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    throw new ApiUnavailableError()
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new ApiUnavailableError()
  const store: RoleStore = {}
  for (const [tenantId, roles] of Object.entries(parsed)) {
    if (!Array.isArray(roles)) throw new ApiUnavailableError()
    store[tenantId] = roles.map(parseRole)
  }
  return store
}

function writeStore(store: RoleStore) {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(store))
  } catch {
    // Storage blocked: the current in-memory response remains valid.
  }
}

function readTenantRoles(tenantId: string): { store: RoleStore; roles: TenantRole[] } {
  const store = readStore()
  const roles = store[tenantId]?.map(cloneRole) ?? seededRoles()
  store[tenantId] = roles.map(cloneRole)
  writeStore(store)
  return { store, roles }
}

function persistTenantRoles(store: RoleStore, tenantId: string, roles: TenantRole[]) {
  store[tenantId] = roles.map(cloneRole)
  writeStore(store)
  return { roles: roles.map(cloneRole) }
}

function normalizeRoleName(value: string) {
  return value.trim().replace(/\s+/g, ' ')
}

function uniquePermissionKeys(keys: PermissionKey[]) {
  return [...new Set(keys)].filter(isPermissionKey)
}

function validateCreate(request: CreateTenantRoleRequest, existing: TenantRole[]) {
  const fieldErrors: Partial<Record<keyof CreateTenantRoleRequest, string>> = {}
  const name = normalizeRoleName(request.name)
  if (name.length === 0) fieldErrors.name = 'نام نقش الزامی است.'
  else if (name.length > ROLE_NAME_MAX) fieldErrors.name = `نام نقش نباید بیشتر از ${ROLE_NAME_MAX} نویسه باشد.`
  else if (existing.some((role) => role.name.toLowerCase() === name.toLowerCase())) {
    throw new TenantRoleConflictError()
  }

  if (!Array.isArray(request.permissionKeys) || request.permissionKeys.some((key) => !isPermissionKey(key))) {
    fieldErrors.permissionKeys = 'مجوزهای انتخاب‌شده معتبر نیستند.'
  }

  if (Object.keys(fieldErrors).length > 0) throw new TenantRoleValidationError(fieldErrors)
}

function findRole(roles: TenantRole[], roleId: string) {
  const role = roles.find((item) => item.id === roleId)
  if (!role) throw new ApiUnavailableError()
  return role
}

function assertNotLastOwnerChange(roles: TenantRole[], memberId: string, roleId: string) {
  if (roleId !== OWNER_ROLE_ID) return
  const ownerRole = findRole(roles, OWNER_ROLE_ID)
  if (ownerRole.memberIds.length <= 1 || !ownerRole.memberIds.includes(memberId)) {
    throw new TenantRoleInvariantError()
  }
}

export const mockRoleAdapter: RoleAdapter = {
  async listRoles(accessToken, tenantId) {
    await delay()
    assertAuthorized(accessToken)
    return { roles: readTenantRoles(tenantId).roles.map(cloneRole) }
  },

  async createRole(accessToken, tenantId, request) {
    await delay()
    assertAuthorized(accessToken)
    const { store, roles } = readTenantRoles(tenantId)
    validateCreate(request, roles)
    const created: TenantRole = {
      id: crypto.randomUUID(),
      name: normalizeRoleName(request.name),
      description: 'نقش سفارشی مستأجر؛ قابل ویرایش و قابل انتساب به اعضا.',
      kind: 'custom',
      permissionKeys: uniquePermissionKeys(request.permissionKeys),
      memberIds: [],
      createdAtUtc: nowUtc(),
      updatedAtUtc: nowUtc(),
    }
    persistTenantRoles(store, tenantId, [...roles, created])
    return cloneRole(created)
  },

  async updateRole(accessToken, tenantId, roleId, request) {
    await delay()
    assertAuthorized(accessToken)
    const { store, roles } = readTenantRoles(tenantId)
    const role = findRole(roles, roleId)
    if (role.kind === 'builtIn') throw new TenantRoleConflictError('مجوزهای نقش سیستمی قابل تغییر نیست.')
    if (!Array.isArray(request.permissionKeys) || request.permissionKeys.some((key) => !isPermissionKey(key))) {
      throw new TenantRoleValidationError({ permissionKeys: 'مجوزهای انتخاب‌شده معتبر نیستند.' })
    }
    const updated = {
      ...role,
      permissionKeys: uniquePermissionKeys(request.permissionKeys),
      updatedAtUtc: nowUtc(),
    }
    const next = roles.map((item) => (item.id === roleId ? updated : item))
    persistTenantRoles(store, tenantId, next)
    return cloneRole(updated)
  },

  async replaceDemoRoles(accessToken, tenantId, roles) {
    await delay()
    assertAuthorized(accessToken)
    const store = readStore()
    return persistTenantRoles(store, tenantId, roles)
  },

  async assignRole(accessToken, tenantId, memberId, roleId) {
    await delay()
    assertAuthorized(accessToken)
    const { store, roles } = readTenantRoles(tenantId)
    const role = findRole(roles, roleId)
    const next = roles.map((item) =>
      item.id === role.id
        ? { ...item, memberIds: [...new Set([...item.memberIds, memberId])], updatedAtUtc: nowUtc() }
        : item,
    )
    return persistTenantRoles(store, tenantId, next)
  },

  async unassignRole(accessToken, tenantId, memberId, roleId) {
    await delay()
    assertAuthorized(accessToken)
    const { store, roles } = readTenantRoles(tenantId)
    assertNotLastOwnerChange(roles, memberId, roleId)
    const role = findRole(roles, roleId)
    const next = roles.map((item) =>
      item.id === role.id
        ? { ...item, memberIds: item.memberIds.filter((id) => id !== memberId), updatedAtUtc: nowUtc() }
        : item,
    )
    return persistTenantRoles(store, tenantId, next)
  },
}
