import type { ReactNode } from 'react'
import { createContext, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { AuthSession, AuthUser, LoginRequest } from './authTypes'
import {
  ApiUnavailableError,
  SessionExpiredError,
} from './authTypes'
import { httpAuthAdapter } from './httpAuthAdapter'

/**
 * S02 session lifecycle:
 *
 * - `bootstrapping`  – a session is stored in this tab; the API is verifying
 *   the token. Protected routes show a loading screen (no content flash).
 * - `authenticated`  – the API confirmed the token (`GET /api/auth/me` 200);
 *   the displayed identity is the server's answer, not the stored copy.
 * - `unauthenticated` – nothing stored, or the stored session was cleared.
 */
type AuthStatus = 'bootstrapping' | 'authenticated' | 'unauthenticated'

/** Visible explanation on the login page for why the user was sent here. */
type SessionNotice = 'expired' | 'unverified' | null

type AuthContextValue = {
  status: AuthStatus
  session: AuthSession | null
  sessionNotice: SessionNotice
  isSigningOut: boolean
  login(request: LoginRequest): Promise<void>
  signOut(): Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

/**
 * Logout has no server-side session to revoke (S01 limitation), so "signing
 * out" is a client operation. This short beat keeps the state visible —
 * button busy, spinner, accessible name change — instead of an instant,
 * untestable jump to the login page.
 */
const SIGN_OUT_NOTICE_MS = 250

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(() =>
    httpAuthAdapter.getSession(),
  )
  const [status, setStatus] = useState<AuthStatus>(() =>
    session ? 'bootstrapping' : 'unauthenticated',
  )
  const [sessionNotice, setSessionNotice] = useState<SessionNotice>(null)
  const [isSigningOut, setIsSigningOut] = useState(false)
  // Monotonic token: superseded bootstrap results (StrictMode double-effect,
  // a fresh login, or a sign-out mid-flight) must never write state.
  const bootstrapTokenRef = useRef(0)

  useEffect(() => {
    const stored = httpAuthAdapter.getSession()
    if (!stored) return

    const runId = ++bootstrapTokenRef.current
    void (async () => {
      try {
        // The server is the source of truth: adopt its identity (or none).
        const user: AuthUser = await httpAuthAdapter.fetchCurrentAccount(
          stored.accessToken,
        )
        if (runId !== bootstrapTokenRef.current) return
        const verifiedSession = { ...stored, user }
        // Persist the verified identity so the next refresh does not carry
        // a stale stored copy forward.
        httpAuthAdapter.saveSession(verifiedSession)
        setSession(verifiedSession)
        setStatus('authenticated')
      } catch (error) {
        if (runId !== bootstrapTokenRef.current) return
        if (error instanceof SessionExpiredError) {
          // 401: the token is missing, invalid or expired. Clear client
          // state so the next refresh does not retry a dead token.
          httpAuthAdapter.signOut()
          setSession(null)
          setSessionNotice('expired')
        } else if (error instanceof ApiUnavailableError) {
          // Network/server failure: fail closed (no protected content). The
          // token stays in sessionStorage so a flaky connection does not
          // destroy a valid session — the next load re-verifies it. The
          // login page explains why verification failed.
          setSession(null)
          setSessionNotice('unverified')
        }
        setStatus('unauthenticated')
      }
    })()
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      session,
      sessionNotice,
      isSigningOut,
      async login(request) {
        bootstrapTokenRef.current += 1 // supersede any in-flight bootstrap
        const nextSession = await httpAuthAdapter.login(request)
        setSession(nextSession)
        setSessionNotice(null)
        setStatus('authenticated')
      },
      async signOut() {
        if (isSigningOut) return
        setIsSigningOut(true)
        bootstrapTokenRef.current += 1
        await new Promise((resolve) => window.setTimeout(resolve, SIGN_OUT_NOTICE_MS))
        httpAuthAdapter.signOut()
        setSession(null)
        setSessionNotice(null)
        setStatus('unauthenticated')
        setIsSigningOut(false)
      },
    }),
    [status, session, sessionNotice, isSigningOut],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider.')
  }
  return context
}
