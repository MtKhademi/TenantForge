import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import {
  InvitationConflictError,
  InvitationForbiddenError,
  InvitationValidationError,
  type CreateInvitationRequest,
  type Invitation,
  type InvitationListResponse,
  type InvitationRole,
  type InvitationStatus,
} from './invitationsTypes'

/**
 * S10 tenant invitations — real API data source (F016).
 *
 * F015's sessionStorage mock is gone. This adapter calls the B010 tenant
 * invitation endpoints with the current bearer token and validates response
 * bodies strictly so the page never renders half-parsed invitation data. The
 * actor recorded on the resulting audit event is resolved server-side from
 * the bearer token — the mock's explicit-actor parameter is no longer needed.
 * UI visibility is still only presentation: B010 owns authorization and
 * returns 403/409 for denied invitation management and duplicate-active
 * emails.
 */
export type InvitationAdapter = {
  listInvitations(accessToken: string, tenantId: string): Promise<InvitationListResponse>
  createInvitation(accessToken: string, tenantId: string, request: CreateInvitationRequest): Promise<Invitation>
}

const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

function invitationsPath(tenantId: string) {
  return `/api/tenants/${encodeURIComponent(tenantId)}/invitations`
}

function authHeaders(accessToken: string) {
  return { Authorization: `Bearer ${accessToken}` }
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

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    throw new ApiUnavailableError()
  }
}

function isInvitationRole(value: unknown): value is InvitationRole {
  return value === 'Owner' || value === 'Viewer'
}

function isInvitationStatus(value: unknown): value is InvitationStatus {
  return value === 'Pending'
}

/**
 * B010 emits `expiresAtUtc`/`createdAtUtc` as a .NET "O" UTC value
 * (`yyyy-MM-ddTHH:mm:ss.fffffff+00:00`). The value is UTC by contract; if it
 * ever arrives as a bare date-time with no zone, `Date.parse` would read it as
 * *local* time and shift the column, so the zone is marked explicitly in that
 * case, matching the same guard already used by `tenantAdapter.ts`.
 */
function normalizeUtcTimestamp(value: string): string {
  if (/(Z|[+-]\d{2}:?\d{2})$/i.test(value)) return value
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/.test(value)) {
    return `${value}Z`
  }
  return value
}

function parseInvitation(payload: unknown): Invitation {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const invitation = payload as Record<string, unknown>
  if (
    typeof invitation.id !== 'string' ||
    invitation.id.length === 0 ||
    typeof invitation.email !== 'string' ||
    invitation.email.length === 0 ||
    !isInvitationRole(invitation.role) ||
    !isInvitationStatus(invitation.status) ||
    typeof invitation.expiresAtUtc !== 'string' ||
    Number.isNaN(Date.parse(invitation.expiresAtUtc)) ||
    typeof invitation.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(invitation.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: invitation.id,
    email: invitation.email,
    role: invitation.role,
    status: invitation.status,
    expiresAtUtc: normalizeUtcTimestamp(invitation.expiresAtUtc),
    createdAtUtc: normalizeUtcTimestamp(invitation.createdAtUtc),
  }
}

function parseInvitationList(payload: unknown): InvitationListResponse {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.invitations)) throw new ApiUnavailableError()
  return { invitations: body.invitations.map(parseInvitation) }
}

function mapServerValidation(payload: unknown): Partial<Record<keyof CreateInvitationRequest, string>> {
  const fallback = 'مقدار واردشده معتبر نیست.'
  if (typeof payload !== 'object' || payload === null) return { email: fallback }
  const body = payload as Record<string, unknown>
  const errors = body.errors
  if (typeof errors !== 'object' || errors === null) return { email: fallback }
  const mapped: Partial<Record<keyof CreateInvitationRequest, string>> = {}
  for (const field of ['email', 'role'] as const) {
    const value = (errors as Record<string, unknown>)[field]
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
    else if (typeof value === 'string') mapped[field] = value
  }
  return Object.keys(mapped).length > 0 ? mapped : { email: fallback }
}

export const httpInvitationAdapter: InvitationAdapter = {
  async listInvitations(accessToken, tenantId) {
    const response = await request(invitationsPath(tenantId), {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new InvitationForbiddenError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseInvitationList(await readJson(response))
  },

  async createInvitation(accessToken, tenantId, body) {
    const response = await request(invitationsPath(tenantId), {
      method: 'POST',
      headers: { ...authHeaders(accessToken), 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (response.status === 400) throw new InvitationValidationError(mapServerValidation(await readJson(response)))
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new InvitationForbiddenError()
    if (response.status === 409) throw new InvitationConflictError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseInvitation(await readJson(response))
  },
}
