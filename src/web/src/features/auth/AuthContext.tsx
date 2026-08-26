import type { ReactNode } from 'react'
import { createContext, useContext, useMemo, useState } from 'react'
import type { AuthSession, LoginRequest } from './authTypes'
import { httpAuthAdapter } from './httpAuthAdapter'

type AuthContextValue = {
  session: AuthSession | null
  login(request: LoginRequest): Promise<void>
  signOut(): void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(() =>
    httpAuthAdapter.getSession(),
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      async login(request) {
        const nextSession = await httpAuthAdapter.login(request)
        setSession(nextSession)
      },
      signOut() {
        httpAuthAdapter.signOut()
        setSession(null)
      },
    }),
    [session],
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
