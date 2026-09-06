export type PermissionKind = 'read' | 'write'

export type PermissionKey =
  | 'IAM.Dashboard.View'
  | 'IAM.Users.View'
  | 'IAM.Users.Create'
  | 'IAM.Tenants.View'
  | 'IAM.Tenants.Create'

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

export class TenantRoleInvariantError extends Error {
  constructor(message = 'آخرین مالک مؤثر مستأجر را نمی‌توان حذف یا بی‌اثر کرد.') {
    super(message)
    this.name = 'TenantRoleInvariantError'
  }
}
