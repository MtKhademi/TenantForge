import type { ReactNode } from 'react'
import { createContext, useContext, useMemo, useState } from 'react'
import type { AuthSession, LoginRequest } from './authTypes'
import { mockAuthAdapter } from './mockAuthAdapter'

type AuthContextValue = {
  session: AuthSession | null
  login(request: LoginRequest): Promise<void>
  signOut(): void
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<AuthSession | null>(() =>
    mockAuthAdapter.getSession(),
  )

  const value = useMemo<AuthContextValue>(
    () => ({
      session,
      async login(request) {
        const nextSession = await mockAuthAdapter.login(request)
        setSession(nextSession)
      },
      signOut() {
        mockAuthAdapter.signOut()
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
