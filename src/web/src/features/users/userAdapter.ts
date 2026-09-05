import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import {
  UserConflictError,
  type CreateUserRequest,
  type PlatformUser,
  type UserListResponse,
  type UserStatus,
} from './userTypes'

/**
 * S06 user management — real API data source (F009).
 *
 * The F008 mock is gone. This adapter calls the B006 endpoints with the
 * current session bearer token and validates response bodies strictly so the
 * page never renders half-parsed data or secret material.
 */
export type UserAdapter = {
  listUsers(accessToken: string): Promise<UserListResponse>
  createUser(accessToken: string, request: CreateUserRequest): Promise<PlatformUser>
}

const USERS_PATH = '/api/platform/users'
const REQUEST_TIMEOUT_MS = 8_000

export class UserValidationError extends Error {
  fieldErrors: Partial<Record<keyof CreateUserRequest, string>>

  constructor(fieldErrors: Partial<Record<keyof CreateUserRequest, string>>) {
    super('درخواست ایجاد کاربر معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'UserValidationError'
  }
}

export class UserForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت کاربران پلتفرم را ندارید.') {
    super(message)
    this.name = 'UserForbiddenError'
  }
}

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

function isUserStatus(value: unknown): value is UserStatus {
  return value === 'Active' || value === 'Disabled'
}

/**
 * B006 emits `createdAtUtc` as a .NET "O" UTC value — `yyyy-MM-ddTHH:mm:ss.fffffff`
 * with **no** `Z` suffix. A `Z`-less ISO string is read by `Date.parse` as *local*
 * time, which would shift the created-at column on any non-UTC machine. The value
 * is UTC by contract, so mark it explicitly before it reaches the table.
 */
function normalizeUtcTimestamp(value: string): string {
  // Already carries a zone (Z or a ±HH:MM offset) — leave it untouched.
  if (/(Z|[+-]\d{2}:?\d{2})$/i.test(value)) return value
  // A bare date-time with no zone: this is the .NET UTC "O" case → append Z.
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/.test(value)) {
    return `${value}Z`
  }
  return value
}

function parseUser(payload: unknown): PlatformUser {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const user = payload as Record<string, unknown>
  if (
    typeof user.id !== 'string' ||
    user.id.length === 0 ||
    typeof user.email !== 'string' ||
    user.email.length === 0 ||
    typeof user.displayName !== 'string' ||
    user.displayName.length === 0 ||
    !isUserStatus(user.status) ||
    typeof user.isPlatformAdmin !== 'boolean' ||
    typeof user.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(user.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: user.id,
    email: user.email,
    displayName: user.displayName,
    status: user.status,
    isPlatformAdmin: user.isPlatformAdmin,
    createdAtUtc: normalizeUtcTimestamp(user.createdAtUtc),
  }
}

function parseListResponse(payload: unknown): UserListResponse {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.users)) {
    throw new ApiUnavailableError()
  }
  return { users: body.users.map(parseUser) }
}

function mapServerValidation(payload: unknown): Partial<Record<keyof CreateUserRequest, string>> {
  const fallback = 'مقدار واردشده معتبر نیست.'
  if (typeof payload !== 'object' || payload === null) return { email: fallback }
  const body = payload as Record<string, unknown>
  const errors = body.errors
  if (typeof errors !== 'object' || errors === null) return { email: fallback }
  const mapped: Partial<Record<keyof CreateUserRequest, string>> = {}
  for (const field of ['email', 'displayName', 'password'] as const) {
    const value = (errors as Record<string, unknown>)[field]
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
    else if (typeof value === 'string') mapped[field] = value
  }
  return Object.keys(mapped).length > 0 ? mapped : { email: fallback }
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

async function request(path: string, init: RequestInit): Promise<Response> {
  const abort = createRequestAbortSignal()
  try {
    return await fetch(path, { ...init, signal: abort.signal })
  } catch {
    throw new ApiUnavailableError()
  } finally {
    abort.clear()
  }
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
}

export const httpUserAdapter: UserAdapter = {
  async listUsers(accessToken) {
    const response = await request(USERS_PATH, {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new UserForbiddenError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseListResponse(await readJson(response))
  },

  async createUser(accessToken, body) {
    const response = await request(USERS_PATH, {
      method: 'POST',
      headers: {
        ...authHeaders(accessToken),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new UserForbiddenError()
    if (response.status === 409) throw new UserConflictError()
    if (response.status === 400) throw new UserValidationError(mapServerValidation(await readJson(response)))
    if (!response.ok) throw new ApiUnavailableError()
    return parseUser(await readJson(response))
  },
}
