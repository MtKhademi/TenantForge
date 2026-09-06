/**
 * S10 audit log — the fixed request/response contract (F015).
 *
 * F015 mocks these exact fields; F016 connects the real B010 endpoints. The
 * shape here is the smallest contract B010 must implement, so it is the source
 * of truth both sides keep in sync.
 *
 * Security boundary: an audit event is an **immutable record of what a user
 * did**, captured server-side around sensitive commands. It must never carry
 * an invitation acceptance token (raw or hashed), a password, or any other
 * secret. The fields below are all safe to render: the actor's identity, a
 * stable action key, a human-readable target, and a free-text detail that the
 * server controls.
 */

/**
 * Stable action keys an audit event can hold. F015's mock emits the
 * invitation and role actions that exist by the end of S09/S10; B010 owns the
 * authoritative set and may add more without a shape change.
 */
export type AuditAction =
  | 'Invitation.Created'
  | 'Role.Created'
  | 'Role.Updated'
  | 'Role.Assigned'
  | 'Role.Unassigned'

/** The person the event is about, as a short human-readable label. */
export type AuditEvent = {
  /** Stable row key. */
  id: string
  /** Display name of the user who performed the action. */
  actor: string
  /** Normalized email of the user who performed the action. */
  actorEmail: string
  /** Which action happened (see {@link AuditAction}). */
  action: AuditAction
  /** What the action was applied to — an email, a role name, a member, etc. */
  target: string
  /**
   * One-line, human-readable detail. Server-authored; the mock fills it with
   * safe prose. Never a token, hash or password.
   */
  details: string
  /** UTC timestamp of when the event was recorded (ISO 8601). */
  createdAtUtc: string
}

/**
 * `GET /api/tenants/{tenantId}/audit?action=&fromUtc=` — the requested
 * tenant's audit events, newest first, already bounded by the server. The
 * mock honors the same `action` and `fromUtc` filters.
 */
export type AuditListResponse = {
  events: AuditEvent[]
}

/**
 * Optional filters the audit page can apply. Both are optional; omitting both
 * returns the tenant's most recent events.
 */
export type AuditQuery = {
  /** Restrict to a single action key. */
  action?: AuditAction
  /** Restrict to events recorded at or after this UTC ISO timestamp. */
  fromUtc?: string
}

/**
 * Raised when the caller lacks audit permission in this tenant
 * (HTTP 403 in F016). Non-leaking: it reveals nothing about the tenant or the
 * audit data.
 */
export class AuditForbiddenError extends Error {
  constructor(message = 'شما اجازه مشاهده گزارش فعالیت این مستأجر را ندارید.') {
    super(message)
    this.name = 'AuditForbiddenError'
  }
}
