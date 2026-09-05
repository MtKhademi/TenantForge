/**
 * S06 user management — the fixed request/response contract (F008).
 *
 * F008 mocks these exact fields; F009 connects the real endpoints. The
 * shape here is the smallest contract B006 must implement, so it is the
 * source of truth both sides keep in sync.
 */

/** Account lifecycle at this stage: a user is created active. */
export type UserStatus = 'Active' | 'Disabled'

/**
 * A platform-scoped user record. Mirrors what the real API returns —
 * deliberately contains **no secret material** (no password, no hash,
 * no reset tokens).
 */
export type PlatformUser = {
  id: string
  /** Normalized (trimmed, lower-cased) email. */
  email: string
  displayName: string
  status: UserStatus
  isPlatformAdmin: boolean
  /** UTC timestamp of creation (ISO 8601). */
  createdAtUtc: string
}

/** `GET /api/platform/users` — first-page collection. */
export type UserListResponse = {
  items: PlatformUser[]
}

/** `POST /api/platform/users` request body. */
export type CreateUserRequest = {
  email: string
  displayName: string
  /** Initial password — consumed by the API (hashed), never echoed back. */
  password: string
}

/**
 * Raised on a duplicate-email conflict (HTTP 409 in F009). The message is
 * a stable, user-facing contract string — no server internals.
 */
export class UserConflictError extends Error {
  constructor(message = 'کاربری با این ایمیل از قبل وجود دارد.') {
    super(message)
    this.name = 'UserConflictError'
  }
}
