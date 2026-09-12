/**
 * S10 tenant invitations — the fixed request/response contract (F015).
 *
 * F015 mocks these exact fields; F016 connects the real B010 endpoints. The
 * shape here is the smallest contract B010 must implement, so it is the source
 * of truth both sides keep in sync.
 *
 * Security boundary: a pending invitation references a person by **email** and
 * the **role** they will be granted. The one-time acceptance token that a real
 * system would embed in the acceptance link is deliberately **absent** from this
 * contract and from the mock data: it is generated, stored hashed, and delivered
 * by email server-side (B010). The UI only ever shows the safe fields below —
 * never a token, hash, or any other secret.
 */

/**
 * The role an invitation grants. F020/S13 treats this as a validated non-empty
 * role-name string: built-in `Owner`/`Viewer` or an exact custom role name from
 * the selected tenant. The request deliberately stays name-based (not `roleId`)
 * so historical custom names remain displayable without a shape change.
 */
export type InvitationRole = string

export type BuiltInInvitationRole = 'Owner' | 'Viewer'

/** Invitation lifecycle. F015 only ever creates `Pending`. */
export type InvitationStatus = 'Pending'

export type Invitation = {
  /** Stable row key. */
  id: string
  /** Normalized (trimmed, lower-cased) invitee email. */
  email: string
  /** The role the invitee will receive on acceptance. */
  role: InvitationRole
  /** Current lifecycle state. */
  status: InvitationStatus
  /**
   * UTC timestamp when the invitation stops being valid (ISO 8601). The mock
   * uses a fixed 7-day window; B010 owns the real expiry policy.
   */
  expiresAtUtc: string
  /** UTC timestamp of creation (ISO 8601). */
  createdAtUtc: string
}

/** `GET /api/tenants/{tenantId}/invitations` — the tenant's active invitations. */
export type InvitationListResponse = {
  invitations: Invitation[]
}

/**
 * `POST /api/tenants/{tenantId}/invitations` request body. No token field:
 * the acceptance token is generated and stored server-side, never supplied by
 * the client.
 */
export type CreateInvitationRequest = {
  email: string
  role: InvitationRole
}

/**
 * Raised when the request body fails validation (HTTP 400 in F016). Carries
 * per-field messages so the form can surface them inline, mirroring F013's
 * {@link import('./roleTypes.ts').TenantRoleValidationError}.
 */
export class InvitationValidationError extends Error {
  fieldErrors: Partial<Record<keyof CreateInvitationRequest, string>>

  constructor(fieldErrors: Partial<Record<keyof CreateInvitationRequest, string>>) {
    super('درخواست دعوت معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'InvitationValidationError'
  }
}

/**
 * Raised when an active invitation for the same normalized email already
 * exists in this tenant (HTTP 409 in F016). The message is a stable,
 * user-facing contract string — no server internals.
 */
export class InvitationConflictError extends Error {
  constructor(message = 'دعوت فعالی برای این ایمیل از قبل وجود دارد.') {
    super(message)
    this.name = 'InvitationConflictError'
  }
}

/**
 * Raised when the caller lacks invitation permission in this tenant
 * (HTTP 403 in F016). Non-leaking: it reveals nothing about the tenant or the
 * invitation data.
 */
export class InvitationForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت دعوت‌های این مستأجر را ندارید.') {
    super(message)
    this.name = 'InvitationForbiddenError'
  }
}
