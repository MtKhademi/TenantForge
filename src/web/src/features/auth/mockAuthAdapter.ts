import type { AuthAdapter, AuthSession, LoginRequest } from './authTypes'
import { InvalidCredentialsError } from './authTypes'

const MOCK_EMAIL = 'admin@tenantforge.local'
const MOCK_PASSWORD = 'local-development-password'
const SESSION_KEY = 'tenantforge:s00:mock-session'

const mockUser = {
  id: 'development-admin',
  email: MOCK_EMAIL,
  displayName: 'Platform Administrator',
  isPlatformAdmin: true,
}

const wait = (milliseconds: number) =>
  new Promise((resolve) => window.setTimeout(resolve, milliseconds))

// S00-only development adapter. S01 replaces this isolated mock with an HTTP auth adapter.
export const mockAuthAdapter: AuthAdapter = {
  getSession() {
    const storedSession = window.sessionStorage.getItem(SESSION_KEY)
    if (!storedSession) return null

    try {
      return JSON.parse(storedSession) as AuthSession
    } catch {
      window.sessionStorage.removeItem(SESSION_KEY)
      return null
    }
  },

  async login(request: LoginRequest) {
    await wait(350)

    if (request.email !== MOCK_EMAIL || request.password !== MOCK_PASSWORD) {
      throw new InvalidCredentialsError()
    }

    const session: AuthSession = {
      user: mockUser,
      signedInAtUtc: new Date().toISOString(),
    }

    window.sessionStorage.setItem(SESSION_KEY, JSON.stringify(session))
    return session
  },

  signOut() {
    window.sessionStorage.removeItem(SESSION_KEY)
  },
}

export const mockAdministratorCredentials = {
  email: MOCK_EMAIL,
  password: MOCK_PASSWORD,
}
