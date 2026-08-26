export type AuthUser = {
  id: string
  email: string
  displayName: string
  isPlatformAdmin: boolean
}

export type AuthSession = {
  user: AuthUser
  /** Signed JWT issued by the development login API. */
  accessToken: string
  /** UTC ISO timestamp for the token's expiry. */
  expiresAtUtc: string
}

export type LoginRequest = {
  email: string
  password: string
}

export interface AuthAdapter {
  getSession(): AuthSession | null
  login(request: LoginRequest): Promise<AuthSession>
  signOut(): void
  /**
   * S02: asks the API who the stored token belongs to. The response is the
   * source of truth for the displayed identity; the locally stored user is
   * only a pre-verification convenience.
   */
  fetchCurrentAccount(accessToken: string): Promise<AuthUser>
  /** Persist a session after it was created or verified. */
  saveSession(session: AuthSession): void
}

/** Raised when the API rejects the submitted credentials (HTTP 401). */
export class InvalidCredentialsError extends Error {
  constructor(message = 'The email or password is incorrect.') {
    super(message)
    this.name = 'InvalidCredentialsError'
  }
}

/**
 * Raised when the login API cannot be reached or answers with a response
 * that is not a valid login result. Deliberately distinct from
 * {@link InvalidCredentialsError} so the UI never presents a network or
 * server failure as "wrong password".
 */
export class ApiUnavailableError extends Error {
  constructor(message = 'The sign-in service is unavailable.') {
    super(message)
    this.name = 'ApiUnavailableError'
  }
}

/**
 * S02: raised when the API rejects the stored session token with 401 —
 * missing, malformed or expired. Deliberately distinct from
 * {@link InvalidCredentialsError}: the user did not mistype anything, the
 * session itself no longer validates, so the UI must say "expired" and
 * clear client state instead of "wrong password".
 */
export class SessionExpiredError extends Error {
  constructor(message = 'Your session has expired.') {
    super(message)
    this.name = 'SessionExpiredError'
  }
}
