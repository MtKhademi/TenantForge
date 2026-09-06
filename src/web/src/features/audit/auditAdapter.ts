import { SessionExpiredError } from '@/features/auth/authTypes'
import { AuditForbiddenError } from './auditTypes'
import type { AuditEvent, AuditListResponse, AuditQuery } from './auditTypes'

/**
 * S10 audit log — mock data source (F015).
 *
 * F015 freezes the B010 contract while using an in-tab sessionStorage-backed
 * mock. The adapter keeps immutable events per tenant, seeds a previous
 * role/permission change (so the demo shows more than just the invitation) and
 * simulates server-side behavior: a real token is required, `?auditViewer=member`
 * receives a non-leaking 403, results are bounded newest-first, and the
 * `action` / `fromUtc` filters work exactly as the HTTP query will. F016
 * replaces this adapter with HTTP calls without changing page code or
 * request/response shapes.
 *
 * Immutability is modeled here by design: there is no update or remove path in
 * this store — events are only ever seeded or appended.
 */
export type AuditAdapter = {
  listAuditEvents(accessToken: string, tenantId: string, query?: AuditQuery): Promise<AuditListResponse>
  /**
   * Appends one immutable event to the tenant's store. Exposed so the
   * invitation mock can record `Invitation.Created` the same way the real
   * backend would record it server-side around the create command.
   */
  recordEvent(accessToken: string, tenantId: string, event: Omit<AuditEvent, 'id' | 'createdAtUtc'> & { createdAtUtc?: string }): Promise<AuditEvent>
}

const STORAGE_KEY = 'tenantforge.audit.v1'
const MOCK_LATENCY_MS = 450
/** Bounded result window, mirroring the server-side pagination B010 enforces. */
const MAX_EVENTS = 50
/** Fixed seed timestamp so the demo is deterministic (matches F013's convention). */
const SEED_TIMESTAMP = '2030-01-01T00:00:00Z'

type AuditStore = Record<string, AuditEvent[]>

function delay() {
  return new Promise<void>((resolve) => window.setTimeout(resolve, MOCK_LATENCY_MS))
}

function assertAuthorized(accessToken: string) {
  if (accessToken.trim().length === 0) throw new SessionExpiredError()
  const mode = new URLSearchParams(window.location.search).get('auditViewer')
  if (mode === 'member' || mode === 'denied') throw new AuditForbiddenError()
}

function readStore(): AuditStore {
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
  return typeof parsed === 'object' && parsed !== null ? (parsed as AuditStore) : {}
}

function writeStore(store: AuditStore) {
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(store))
  } catch {
    // Storage can be full or blocked; the mock degrades to in-memory-only.
  }
}

/**
 * Deterministic, collision-resistant-enough id for mock rows. Not a GUID: the
 * mock does not need global uniqueness, only per-tab stability.
 */
function mockId(prefix: string, ...parts: string[]) {
  const seed = parts.join('|')
  let hash = 0
  for (let i = 0; i < seed.length; i += 1) {
    hash = (hash * 31 + seed.charCodeAt(i)) >>> 0
  }
  return `${prefix}-${hash.toString(16)}`
}

/**
 * One previous role/permission change, seeded so the audit page demonstrates
 * "the invitation and a previous role/permission change" (S10 demo step 5).
 */
function seededEvents(): AuditEvent[] {
  return [
    {
      id: 'evt-seed-role-created',
      actor: 'Sara Rahimi',
      actorEmail: 'sara.rahimi@acme.test',
      action: 'Role.Created',
      target: 'Viewer',
      details: 'نقش سیستمی Viewer برای این مستأجر ایجاد شد.',
      createdAtUtc: SEED_TIMESTAMP,
    },
    {
      id: 'evt-seed-role-updated',
      actor: 'Sara Rahimi',
      actorEmail: 'sara.rahimi@acme.test',
      action: 'Role.Updated',
      target: 'User Manager',
      details: 'مجوز IAM.Users.View به نقش سفارشی User Manager افزوده شد.',
      createdAtUtc: SEED_TIMESTAMP,
    },
  ]
}

/** Ensure the tenant's store exists and contains its seed events (idempotent). */
function ensureSeeded(store: AuditStore, tenantId: string): AuditEvent[] {
  if (!Array.isArray(store[tenantId]) || store[tenantId].length === 0) {
    store[tenantId] = seededEvents()
  }
  return store[tenantId]
}

function cloneEvent(event: AuditEvent): AuditEvent {
  return { ...event }
}

export const httpAuditAdapter: AuditAdapter = {
  async listAuditEvents(accessToken, tenantId, query) {
    assertAuthorized(accessToken)
    await delay()
    const store = readStore()
    const events = ensureSeeded(store, tenantId)
    writeStore(store)

    let result = events
    if (query?.action) {
      result = result.filter((event) => event.action === query.action)
    }
    if (query?.fromUtc) {
      const from = Date.parse(query.fromUtc)
      if (!Number.isNaN(from)) {
        result = result.filter((event) => Date.parse(event.createdAtUtc) >= from)
      }
    }

    // Newest first, then truncated to the bounded window.
    const sorted = [...result].sort(
      (a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc),
    )
    return { events: sorted.slice(0, MAX_EVENTS).map(cloneEvent) }
  },

  async recordEvent(accessToken, tenantId, event) {
    assertAuthorized(accessToken)
    await delay()
    const store = readStore()
    const events = ensureSeeded(store, tenantId)
    const createdAtUtc = event.createdAtUtc ?? new Date().toISOString()
    const fullEvent: AuditEvent = {
      id: mockId('evt', tenantId, event.action, event.target, createdAtUtc),
      actor: event.actor,
      actorEmail: event.actorEmail,
      action: event.action,
      target: event.target,
      details: event.details,
      createdAtUtc,
    }
    events.unshift(fullEvent)
    store[tenantId] = events.slice(0, MAX_EVENTS)
    writeStore(store)
    return cloneEvent(fullEvent)
  },
}
