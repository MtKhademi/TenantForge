import {
  ApiUnavailableError,
  SessionExpiredError,
} from '@/features/auth/authTypes'
import type { DashboardSummary } from './dashboardTypes'

/**
 * S03 dashboard summary — data source seam.
 *
 * F007: `httpDashboardAdapter` calls the real B003 endpoint
 * `GET /api/platform/dashboard-summary` with the session token. The F006 mock
 * is gone; this is the only implementation.
 *
 * Error mapping follows the same rules as the auth adapter:
 * - `401` → `SessionExpiredError` (missing/invalid/expired token — the auth
 *   layer clears the session and redirects to login);
 * - `403` or any other non-2xx / network failure / malformed body →
 *   `ApiUnavailableError` (the page shows the retryable error state).
 */
export type DashboardAdapter = {
  fetchSummary(accessToken: string): Promise<DashboardSummary>
}

const SUMMARY_PATH = '/api/platform/dashboard-summary'
const REQUEST_TIMEOUT_MS = 8_000

function createRequestAbortSignal() {
  const controller = new AbortController()
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS)
  return {
    signal: controller.signal,
    clear: () => window.clearTimeout(timeoutId),
  }
}

/**
 * Strict contract validation: the page must never render half-parsed data.
 * Any deviation from the accepted S03 contract is treated as an unavailable
 * API, not a partially successful one.
 */
function parseSummary(payload: unknown): DashboardSummary {
  if (typeof payload !== 'object' || payload === null) {
    throw new ApiUnavailableError()
  }
  const body = payload as Record<string, unknown>
  if (
    typeof body.environment !== 'string' ||
    body.environment.length === 0 ||
    typeof body.apiStatus !== 'string' ||
    body.apiStatus.length === 0 ||
    typeof body.platformAdminCount !== 'number' ||
    !Number.isInteger(body.platformAdminCount) ||
    typeof body.generatedAtUtc !== 'string' ||
    Number.isNaN(Date.parse(body.generatedAtUtc))
  ) {
    throw new ApiUnavailableError()
  }
  return {
    environment: body.environment,
    apiStatus: body.apiStatus,
    platformAdminCount: body.platformAdminCount,
    generatedAtUtc: body.generatedAtUtc,
  }
}

export const httpDashboardAdapter: DashboardAdapter = {
  async fetchSummary(accessToken) {
    let response: Response
    const abort = createRequestAbortSignal()
    try {
      response = await fetch(SUMMARY_PATH, {
        method: 'GET',
        headers: { Authorization: `Bearer ${accessToken}` },
        signal: abort.signal,
      })
    } catch {
      // Network failure or timeout: the API is unreachable.
      throw new ApiUnavailableError()
    } finally {
      abort.clear()
    }

    if (response.status === 401) {
      throw new SessionExpiredError()
    }
    if (!response.ok) {
      // 403 (not a platform administrator) and any other server failure.
      throw new ApiUnavailableError()
    }

    let payload: unknown
    try {
      payload = await response.json()
    } catch {
      throw new ApiUnavailableError()
    }

    return parseSummary(payload)
  },
}
