import type { AuthAdapter, AuthSession, LoginRequest } from './authTypes'
import { ApiUnavailableError, InvalidCredentialsError } from './authTypes'

/**
 * S01: the login UI talks to the real development API through this adapter.
 * The path is relative so the Vite dev/preview proxy (and, later, any same
 * origin deployment) forwards `/api/*` to the .NET API.
 */
const LOGIN_PATH = '/api/auth/login'
const SESSION_KEY = 'tenantforge:auth:session'
const LOGIN_TIMEOUT_MS = 8_000

function createLoginAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), LOGIN_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

/** Documented development administrator (see appsettings.Development.json). */
export const developmentAdministratorCredentials = {
  email: 'admin@tenantforge.local',
  password: 'local-development-password',
}

type LoginResponse = {
  accessToken: string
  expiresAtUtc: string
  user: {
    id: string
    email: string
    displayName: string
    isPlatformAdmin: boolean
  }
}

function parseSession(raw: string | null): AuthSession | null {
  if (!raw) return null

  try {
    const parsed = JSON.parse(raw) as Partial<AuthSession>
    const user = parsed.user
    if (
      typeof parsed.accessToken !== 'string' ||
      parsed.accessToken.length === 0 ||
      typeof parsed.expiresAtUtc !== 'string' ||
      !user ||
      typeof user.id !== 'string' ||
      typeof user.email !== 'string' ||
      typeof user.displayName !== 'string' ||
      typeof user.isPlatformAdmin !== 'boolean'
    ) {
      return null
    }
    return { user, accessToken: parsed.accessToken, expiresAtUtc: parsed.expiresAtUtc }
  } catch {
    return null
  }
}

function parseLoginResponse(payload: unknown): LoginResponse {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const response = payload as Record<string, unknown>
  const user = response.user as Record<string, unknown> | undefined
  if (
    typeof response.accessToken !== 'string' ||
    response.accessToken.length === 0 ||
    typeof response.expiresAtUtc !== 'string' ||
    !user ||
    typeof user.id !== 'string' ||
    typeof user.email !== 'string' ||
    typeof user.displayName !== 'string' ||
    typeof user.isPlatformAdmin !== 'boolean'
  ) {
    throw new ApiUnavailableError()
  }
  return response as unknown as LoginResponse
}

export const httpAuthAdapter: AuthAdapter = {
  getSession() {
    const session = parseSession(window.sessionStorage.getItem(SESSION_KEY))
    if (!session) return null

    // Drop an expired session instead of carrying a token past its expiry.
    const expiresAt = Date.parse(session.expiresAtUtc)
    if (Number.isFinite(expiresAt) && expiresAt <= Date.now()) {
      window.sessionStorage.removeItem(SESSION_KEY)
      return null
    }

    return session
  },

  async login(request: LoginRequest) {
    let response: Response
    const abort = createLoginAbortSignal()
    try {
      response = await fetch(LOGIN_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
        signal: abort.signal,
      })
    } catch {
      throw new ApiUnavailableError()
    } finally {
      abort.clear()
    }

    if (response.status === 401) {
      throw new InvalidCredentialsError()
    }
    if (!response.ok) {
      throw new ApiUnavailableError()
    }

    let payload: unknown
    try {
      payload = await response.json()
    } catch {
      throw new ApiUnavailableError()
    }

    const body = parseLoginResponse(payload)
    const session: AuthSession = {
      user: body.user,
      accessToken: body.accessToken,
      expiresAtUtc: body.expiresAtUtc,
    }

    window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
    return session
  },

  signOut() {
    window.sessionStorage.removeItem(SESSION_KEY)
  },
}
