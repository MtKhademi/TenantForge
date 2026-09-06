import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import { AuditForbiddenError } from './auditTypes'
import type { AuditAction, AuditEvent, AuditListResponse, AuditQuery } from './auditTypes'

/**
 * S10 audit log — real API data source (F016).
 *
 * F015's sessionStorage mock is gone. This adapter calls the B010 tenant
 * audit endpoint with the current bearer token and validates response bodies
 * strictly so the page never renders half-parsed event data. Every audit
 * event is now recorded server-side around the sensitive commands that
 * created it (invitation creation, role changes) — there is no client-side
 * `recordEvent` path; the mock's seeding and append behavior are gone.
 */
export type AuditAdapter = {
  listAuditEvents(accessToken: string, tenantId: string, query?: AuditQuery): Promise<AuditListResponse>
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

function auditPath(tenantId: string, query?: AuditQuery) {
  const params = new URLSearchParams()
  if (query?.action) params.set('action', query.action)
  if (query?.fromUtc) params.set('fromUtc', query.fromUtc)
  const suffix = params.toString()
  return `/api/tenants/${encodeURIComponent(tenantId)}/audit${suffix ? `?${suffix}` : ''}`
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

const KNOWN_ACTIONS: readonly AuditAction[] = [
  'Invitation.Created',
  'Role.Created',
  'Role.Updated',
  'Role.Assigned',
  'Role.Unassigned',
]

function isAuditAction(value: unknown): value is AuditAction {
  return typeof value === 'string' && (KNOWN_ACTIONS as readonly string[]).includes(value)
}

/**
 * B010 emits `createdAtUtc` as a .NET "O" UTC value. The value is UTC by
 * contract; if it ever arrives as a bare date-time with no zone, mark the
 * zone explicitly so `Date.parse` does not read it as local time (same guard
 * as `tenantAdapter.ts`/`tenantMembersAdapter.ts`).
 */
function normalizeUtcTimestamp(value: string): string {
  if (/(Z|[+-]\d{2}:?\d{2})$/i.test(value)) return value
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/.test(value)) {
    return `${value}Z`
  }
  return value
}

function parseAuditEvent(payload: unknown): AuditEvent {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const event = payload as Record<string, unknown>
  if (
    typeof event.id !== 'string' ||
    event.id.length === 0 ||
    typeof event.actor !== 'string' ||
    event.actor.length === 0 ||
    typeof event.actorEmail !== 'string' ||
    event.actorEmail.length === 0 ||
    !isAuditAction(event.action) ||
    typeof event.target !== 'string' ||
    typeof event.details !== 'string' ||
    typeof event.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(event.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: event.id,
    actor: event.actor,
    actorEmail: event.actorEmail,
    action: event.action,
    target: event.target,
    details: event.details,
    createdAtUtc: normalizeUtcTimestamp(event.createdAtUtc),
  }
}

function parseAuditList(payload: unknown): AuditListResponse {
  if (typeof payload !== 'object' || payload === null) throw new ApiUnavailableError()
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.events)) throw new ApiUnavailableError()
  return { events: body.events.map(parseAuditEvent) }
}

export const httpAuditAdapter: AuditAdapter = {
  async listAuditEvents(accessToken, tenantId, query) {
    const response = await request(auditPath(tenantId, query), {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new AuditForbiddenError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseAuditList(await readJson(response))
  },
}
