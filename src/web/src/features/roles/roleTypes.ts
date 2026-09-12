export type PermissionKind = 'read' | 'write'

/**
 * S12 (B013): the stable tenant permission keys. The permission **labels,
 * descriptions and grouping are not duplicated here** — they arrive from the
 * server catalog (`GET /api/permissions/catalog`) and are rendered as sent.
 * This union only pins the contract keys for compile-time checks.
 */
export type PermissionKey =
  | 'IAM.Roles.Manage'
  | 'IAM.Invitations.View'
  | 'IAM.Invitations.Create'
  | 'IAM.Audit.View'

export const ROLES_MANAGE_KEY = 'IAM.Roles.Manage'
export const INVITATIONS_VIEW_KEY = 'IAM.Invitations.View'
export const INVITATIONS_CREATE_KEY = 'IAM.Invitations.Create'
export const AUDIT_VIEW_KEY = 'IAM.Audit.View'

export type PermissionItem = {
  key: PermissionKey
  label: string
  description: string
  kind: PermissionKind
}

export type PermissionGroup = {
  id: string
  label: string
  description: string
  permissions: PermissionItem[]
}

export type PermissionCatalogResponse = {
  groups: PermissionGroup[]
}

export type TenantRoleKind = 'builtIn' | 'custom'

export type TenantRole = {
  id: string
  name: string
  description: string
  kind: TenantRoleKind
  permissionKeys: PermissionKey[]
  memberIds: string[]
  createdAtUtc: string
  updatedAtUtc: string
}

export type TenantRoleListResponse = {
  roles: TenantRole[]
}

export type CreateTenantRoleRequest = {
  name: string
  permissionKeys: PermissionKey[]
}

export type UpdateTenantRoleRequest = {
  permissionKeys: PermissionKey[]
}

export type CurrentTenantPermissionsResponse = {
  permissions: PermissionKey[]
}

export class TenantRoleValidationError extends Error {
  fieldErrors: Partial<Record<keyof CreateTenantRoleRequest, string>>

  constructor(fieldErrors: Partial<Record<keyof CreateTenantRoleRequest, string>>) {
    super('درخواست نقش معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'TenantRoleValidationError'
  }
}

export class TenantRoleConflictError extends Error {
  constructor(message = 'نقشی با این نام در این مستأجر وجود دارد.') {
    super(message)
    this.name = 'TenantRoleConflictError'
  }
}

export class TenantRoleForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت نقش‌های این مستأجر را ندارید.') {
    super(message)
    this.name = 'TenantRoleForbiddenError'
  }
}

/**
 * S12 (B013): the server rejects a role update/unassignment whose proposed
 * final state would leave the tenant without any effective role
 * administrator (membership Owner or an assigned role containing
 * `IAM.Roles.Manage`).
 */
export class TenantRoleInvariantError extends Error {
  constructor(message = 'آخرین مدیر مؤثر نقش‌های مستأجر را نمی‌توان حذف یا بی‌اثر کرد.') {
    super(message)
    this.name = 'TenantRoleInvariantError'
  }
}
