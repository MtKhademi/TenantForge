import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import type { MyTenant, MyTenantListResponse } from './tenantTypes'

/**
 * S11 tenant discovery — real API data source (F018, connected to B012).
 *
 * `GET /api/auth/me/tenants` returns the caller's own active-tenant
 * memberships for **every** account, including a platform administrator
 * (never a bypass to the full platform list). The response is the smallest
 * projection the switcher and the in-shell membership chooser need: no
 * member counts, no timestamps — so the client cannot render invented data.
 *
 * Error mapping follows the S02 auth rules:
 * - `401` → `SessionExpiredError` (missing/invalid/expired token — the auth
 *   layer clears the session and redirects to login);
 * - `403` (missing or disabled account — the stateless JWT cannot outlive the
 *   row) → `SessionExpiredError` as well, because the session is no longer
 *   usable and the honest response is the S02 "expired" flow;
 * - any other non-2xx / network failure / malformed body → `ApiUnavailableError`
 *   (the page offers a retryable state).
 *
 * The `TenantAdapter` interface in `./tenantAdapter.ts` is left untouched: the
 * platform tenant list and its admin-only create flow still own `TenantSummary`,
 * which is strictly richer than a membership row.
 */
export type TenantDiscoveryAdapter = {
  listMyTenants(accessToken: string): Promise<MyTenantListResponse>
}

const MY_TENANTS_PATH = '/api/auth/me/tenants'
const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

/** Strict contract validation: never render a half-parsed membership row. */
function parseMyTenant(payload: unknown): MyTenant {
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
    tenant.status !== 'Active' ||
    (tenant.membershipRole !== 'Owner' && tenant.membershipRole !== 'Member')
  ) {
    throw new ApiUnavailableError()
  }
  return {
    id: tenant.id,
    name: tenant.name,
    slug: tenant.slug,
    status: 'Active',
    membershipRole: tenant.membershipRole,
  }
}

function parseMyTenantList(payload: unknown): MyTenantListResponse {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const body = payload as Record<string, unknown>
  if (!Array.isArray(body.tenants)) {
    throw new ApiUnavailableError()
  }
  return { tenants: body.tenants.map(parseMyTenant) }
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

export const httpTenantDiscoveryAdapter: TenantDiscoveryAdapter = {
  async listMyTenants(accessToken) {
    const response = await request(MY_TENANTS_PATH, {
      method: 'GET',
      headers: { Authorization: `Bearer ${accessToken}` },
    })
    if (response.status === 401 || response.status === 403) throw new SessionExpiredError()
    if (!response.ok) throw new ApiUnavailableError()
    return parseMyTenantList(await readJson(response))
  },
}
