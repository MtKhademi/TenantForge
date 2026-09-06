import { SessionExpiredError } from '@/features/auth/authTypes'
import { httpAuditAdapter } from '@/features/audit/auditAdapter'
import {
  InvitationConflictError,
  InvitationForbiddenError,
  InvitationValidationError,
  type CreateInvitationRequest,
  type Invitation,
  type InvitationListResponse,
} from './invitationsTypes'

/**
 * S10 tenant invitations — mock data source (F015).
 *
 * F015 freezes the B010 contract while using an in-tab sessionStorage-backed
 * mock. The adapter keeps active invitations per tenant, simulates latency so
 * loading and saving states are visible, and models server-side behavior:
 * - a real bearer token is required;
 * - `?invitationsViewer=member` receives a non-leaking 403;
 * - invalid email or missing role yields per-field validation errors;
 * - a duplicate active email in the same tenant yields a 409 conflict;
 * - creating an invitation also records an immutable `Invitation.Created`
 *   audit event, exactly as B010 will capture it server-side around the
 *   command.
 *
 * No acceptance token is ever generated, stored or returned: the one-time
 * token is a B010 server-side responsibility. The mock returns only the safe
 * {@link Invitation} fields. F016 replaces this adapter with HTTP calls without
 * changing page code or request/response shapes.
 */

/**
 * The signed-in principal who performs the create. In F016 the server derives
 * this from the bearer token; the mock receives it explicitly from the page,
 * which is the equivalent trusted source. It is used only to fill the audit
 * event's actor fields — it never influences authorization here beyond the
 * token/denial check.
 */
export type InvitationActor = {
  displayName: string
  email: string
}

export type InvitationAdapter = {
  listInvitations(accessToken: string, tenantId: string): Promise<InvitationListResponse>
  createInvitation(
    accessToken: string,
    tenantId: string,
    request: CreateInvitationRequest,
    actor: InvitationActor,
  ): Promise<Invitation>
}

const STORAGE_KEY = 'tenantforge.invitations.v1'
const MOCK_LATENCY_MS = 450
/** Fixed 7-day validity window (UTC), independent of the local clock. */
const EXPIRY_DAYS = 7
const EMAIL_MAX = 254

type InvitationStore = Record<string, Invitation[]>

const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function delay() {
  return new Promise<void>((resolve) => window.setTimeout(resolve, MOCK_LATENCY_MS))
}

function assertAuthorized(accessToken: string) {
  if (accessToken.trim().length === 0) throw new SessionExpiredError()
  const mode = new URLSearchParams(window.location.search).get('invitationsViewer')
  if (mode === 'member' || mode === 'denied') throw new InvitationForbiddenError()
}

function readStore(): InvitationStore {
  let raw: string | null
  try {
    raw = window.sessionStorage.getItem(STORAGE_KEY)
  } catch {
    return {}
  }
  if (raw === null) return {}
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return {}
  }
  return typeof parsed === 'object' && parsed !== null ? (parsed as InvitationStore) : {}
}

function writeStore(store: InvitationStore) {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(store))
  } catch {
    // Storage can be full or blocked; the mock degrades to in-memory-only.
  }
}

function normalizeEmail(email: string) {
  return email.trim().toLowerCase()
}

function isInvitationRole(value: unknown): value is Invitation['role'] {
  return value === 'Owner' || value === 'Viewer'
}

/** Persian labels for the audit trail's free-text detail (UI-facing, not the wire `role`). */
const ROLE_LABELS: Record<Invitation['role'], string> = {
  Owner: 'مالک',
  Viewer: 'مشاهده‌گر',
}

function mockId(prefix: string, ...parts: string[]) {
  const seed = parts.join('|')
  let hash = 0
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0
  }
  return `${prefix}-${hash.toString(16)}`
}

function cloneInvitation(invitation: Invitation): Invitation {
  return { ...invitation }
}

export const httpInvitationAdapter: InvitationAdapter = {
  async listInvitations(accessToken, tenantId) {
    assertAuthorized(accessToken)
    await delay()
    const store = readStore()
    const list = Array.isArray(store[tenantId]) ? store[tenantId] : []
    // Newest first.
    const sorted = [...list].sort(
      (a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc),
    )
    return { invitations: sorted.map(cloneInvitation) }
  },

  async createInvitation(accessToken, tenantId, request, actor) {
    assertAuthorized(accessToken)
    await delay()

    const email = normalizeEmail(request.email)
    const fieldErrors: Partial<Record<keyof CreateInvitationRequest, string>> = {}
    if (email.length === 0 || !EMAIL_PATTERN.test(email)) {
      fieldErrors.email = 'ایمیل معتبر وارد کنید.'
    } else if (email.length > EMAIL_MAX) {
      fieldErrors.email = 'ایمیل نباید بیشتر از ۲۵۴ نویسه باشد.'
    }
    if (!isInvitationRole(request.role)) {
      fieldErrors.role = 'نقش را انتخاب کنید.'
    }
    if (Object.keys(fieldErrors).length > 0) {
      throw new InvitationValidationError(fieldErrors)
    }

    const store = readStore()
    const list = Array.isArray(store[tenantId]) ? store[tenantId] : []
    const duplicate = list.some(
      (invitation) => invitation.status === 'Pending' && invitation.email === email,
    )
    if (duplicate) {
      throw new InvitationConflictError()
    }

    const createdAtUtc = new Date().toISOString()
    const expiresAtUtc = new Date(Date.parse(createdAtUtc) + EXPIRY_DAYS * 86_400_000).toISOString()
    const invitation: Invitation = {
      id: mockId('inv', tenantId, email, createdAtUtc),
      email,
      role: request.role,
      status: 'Pending',
      expiresAtUtc,
      createdAtUtc,
    }
    list.unshift(invitation)
    store[tenantId] = list
    writeStore(store)

    // Mirror the real backend: record an immutable audit event for the
    // invitation creation, scoped to this tenant, attributed to the caller.
    await httpAuditAdapter.recordEvent(accessToken, tenantId, {
      actor: actor.displayName,
      actorEmail: actor.email,
      action: 'Invitation.Created',
      target: email,
      details: `${email} با نقش ${ROLE_LABELS[request.role]} دعوت شد.`,
    })

    return cloneInvitation(invitation)
  },
}
