export type AuthUser = {
  id: string
  email: string
  displayName: string
  isPlatformAdmin: boolean
}

export type AuthSession = {
  user: AuthUser
  signedInAtUtc: string
}

export type LoginRequest = {
  email: string
  password: string
}

export interface AuthAdapter {
  getSession(): AuthSession | null
  login(request: LoginRequest): Promise<AuthSession>
  signOut(): void
}

export class InvalidCredentialsError extends Error {
  constructor() {
    super('Invalid mock administrator credentials.')
    this.name = 'InvalidCredentialsError'
  }
}
