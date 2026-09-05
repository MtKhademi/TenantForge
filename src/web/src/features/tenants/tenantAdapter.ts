import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import {
  TenantConflictError,
  type CreateTenantRequest,
  type TenantListResponse,
  type TenantStatus,
  type TenantSummary,
} from './tenantTypes'

/**
 * S07 tenant membership — real API data source (F011).
 *
 * The F010 `sessionStorage` mock is gone. This adapter calls the B007
 * endpoints (`GET/POST /api/platform/tenants`) with the current session
 * bearer token and validates response bodies strictly so the page never
 * renders half-parsed data. The `TenantAdapter` interface is unchanged from
 * F010, so the page and the shared tenant-scope context needed no other edits
 * to swap data sources.
 */
export type TenantAdapter = {
  listTenants(accessToken: string): Promise<TenantListResponse>
  createTenant(accessToken: string, request: CreateTenantRequest): Promise<TenantSummary>
}

const TENANTS_PATH = '/api/platform/tenants'
const REQUEST_TIMEOUT_MS = 8_000

/**
 * `POST /api/platform/tenants` client-side validation failure (HTTP 400 in
 * B007). Field keys are the request field names, exactly like B006's user
 * validation, so the page maps server field errors identically in both.
 */
export class TenantValidationError extends Error {
  fieldErrors: Partial<Record<keyof CreateTenantRequest, string>>

  constructor(fieldErrors: Partial<Record<keyof CreateTenantRequest, string>>) {
    super('درخواست ایجاد مستأجر معتبر نیست.')
    this.fieldErrors = fieldErrors
    this.name = 'TenantValidationError'
  }
}

export class TenantForbiddenError extends Error {
  constructor(message = 'شما اجازه مدیریت مستأجران پلتفرم را ندارید.') {
    super(message)
    this.name = 'TenantForbiddenError'
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

function isTenantStatus(value: unknown): value is TenantStatus {
  return value === 'Active' || value === 'Suspended'
}

/**
 * B007 emits `createdAtUtc` as a .NET "O" UTC value (`yyyy-MM-ddTHH:mm:ss.fffffff+00:00`).
 * The value is UTC by contract; if it ever arrives as a bare date-time with no
 * zone, `Date.parse` would read it as *local* time and shift the column, so we
 * mark the zone explicitly in that case. A value that already carries a `Z` or
 * a ±HH:MM offset is left untouched.
 */
function normalizeUtcTimestamp(value: string): string {
  if (/(Z|[+-]\d{2}:?\d{2})$/i.test(value)) return value
  if (/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?$/.test(value)) {
    return `${value}Z`
  }
  return value
}

function parseTenant(payload: unknown): TenantSummary {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const tenant = payload as Record<string, unknown>
  if (
    typeof tenant.id !== 'string' ||
    tenant.id.length === 0 ||
    typeof tenant.name !== 'string' ||
    tenant.name.length === 0 ||
    typeof tenant.slug !== 'string' ||
    tenant.slug.length === 0 ||
    !isTenantStatus(tenant.status) ||
    typeof tenant.memberCount !== 'number' ||
    !Number.isInteger(tenant.memberCount) ||
    tenant.memberCount < 1 ||
    typeof tenant.createdAtUtc !== 'string' ||
    Number.isNaN(Date.parse(tenant.createdAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: tenant.id,
    name: tenant.name,
    slug: tenant.slug,
    status: tenant.status,
    memberCount: tenant.memberCount,
    createdAtUtc: normalizeUtcTimestamp(tenant.createdAtUtc),
  }
}

function parseListResponse(payload: unknown): TenantListResponse {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.tenants)) {
    throw new ApiUnavailableError()
  }
  return { tenants: body.tenants.map(parseTenant) }
}

function mapServerValidation(payload: unknown): Partial<Record<keyof CreateTenantRequest, string>> {
  const fallback = 'مقدار واردشده معتبر نیست.'
  if (typeof payload !== 'object' || payload === null) return { name: fallback }
  const body = payload as Record<string, unknown>
  const errors = body.errors
  if (typeof errors !== 'object' || errors === null) return { name: fallback }
  const mapped: Partial<Record<keyof CreateTenantRequest, string>> = {}
  for (const field of ['name', 'slug', 'ownerUserId'] as const) {
    const value = (errors as Record<string, unknown>)[field]
    if (Array.isArray(value) && typeof value[0] === 'string') mapped[field] = value[0]
    else if (typeof value === 'string') mapped[field] = value
  }
  return Object.keys(mapped).length > 0 ? mapped : { name: fallback }
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

export const httpTenantAdapter: TenantAdapter = {
  async listTenants(accessToken) {
    const response = await request(TENANTS_PATH, {
      method: 'GET',
      headers: authHeaders(accessToken),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new TenantForbiddenError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseListResponse(await readJson(response))
  },

  async createTenant(accessToken, body) {
    const response = await request(TENANTS_PATH, {
      method: 'POST',
      headers: {
        ...authHeaders(accessToken),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(body),
    })
    if (response.status === 401) throw new SessionExpiredError()
    if (response.status === 403) throw new TenantForbiddenError()
    if (response.status === 409) throw new TenantConflictError()
    if (response.status === 400) throw new TenantValidationError(mapServerValidation(await readJson(response)))
    if (!response.ok) throw new ApiUnavailableError()
    return parseTenant(await readJson(response))
  },
}
