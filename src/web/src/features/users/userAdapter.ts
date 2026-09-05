import { ApiUnavailableError } from '@/features/auth/authTypes'
import {
  UserConflictError,
  type CreateUserRequest,
  type PlatformUser,
  type UserListResponse,
} from './userTypes'

/**
 * S06 user management — data source seam (F008: contract + mock).
 *
 * F009 replaces `mockUserAdapter` with a `httpUserAdapter` that calls
 * `GET/POST /api/platform/users` with the session token; the page never
 * needs to change.
 *
 * The mock honors the exact contract (field names, types, UTC timestamps,
 * normalized email, no secret material) and keeps its collection in-tab so
 * a created user survives a list re-fetch — mirroring persisted behavior
 * without a backend.
 */
export type UserAdapter = {
  listUsers(): Promise<UserListResponse>
  createUser(request: CreateUserRequest): Promise<PlatformUser>
}

/** Simulated network latency so the loading skeleton is actually visible. */
const MOCK_LATENCY_MS = 700

/**
 * Dev-only demo hooks (F008 only, removed in F009) — make the non-happy
 * list states demonstrable in the browser without a backend:
 *
 * - `?users=empty`   serves an empty collection (empty state);
 * - `?usersError`    fails exactly one list fetch per page load (retryable
 *   error state). It is scoped to the list call only and to the fetch that
 *   the user actually sees: under React StrictMode the mount effect runs
 *   twice in development, so the visible initial list fetch is the second
 *   list call. The hook never touches `createUser`.
 *
 * Nothing in the visible UI advertises these; they only affect the mock.
 */
const searchParams =
  typeof window !== 'undefined'
    ? new URLSearchParams(window.location.search)
    : new URLSearchParams()

const demoMode = searchParams.get('users') // 'empty' | null
const listErrorRequested = searchParams.has('usersError')

let listCallNumber = 0
let listErrorArmed = listErrorRequested

/**
 * The in-tab collection, seeded with the documented development
 * administrator (B001/B005). This is the task-approved mock data — it
 * matches what the real API will return for the platform at this stage.
 */
const seededUsers: PlatformUser[] = [
  {
    id: 'development-admin',
    email: 'admin@tenantforge.local',
    displayName: 'Platform Administrator',
    status: 'Active',
    isPlatformAdmin: true,
    createdAtUtc: '2030-01-01T00:00:00Z',
  },
]

const collection: PlatformUser[] = [...seededUsers]

let createIdCounter = 0

function delay() {
  return new Promise((resolve) => setTimeout(resolve, MOCK_LATENCY_MS))
}

function normalizeEmail(email: string) {
  return email.trim().toLowerCase()
}

/** Deterministic id for mock-created users (no secret material). */
function nextId() {
  createIdCounter += 1
  return `user-${createIdCounter}`
}

export const mockUserAdapter: UserAdapter = {
  async listUsers() {
    await delay()
    listCallNumber += 1
    // Fault only the visible initial fetch (second call under StrictMode).
    if (listErrorArmed && listCallNumber === 2) {
      listErrorArmed = false
      throw new ApiUnavailableError()
    }
    const items =
      demoMode === 'empty' ? [] : [...collection].map((user) => ({ ...user }))
    return { items }
  },

  async createUser(request) {
    await delay()
    const email = normalizeEmail(request.email)
    // Uniqueness: a stable conflict, identical to the real 409 behavior.
    if (collection.some((user) => user.email === email)) {
      throw new UserConflictError()
    }
    const created: PlatformUser = {
      id: nextId(),
      email,
      displayName: request.displayName.trim(),
      // The mock creates an active user, exactly like the contract.
      status: 'Active',
      isPlatformAdmin: false,
      createdAtUtc: new Date().toISOString(),
    }
    collection.push(created)
    return { ...created }
  },
}
