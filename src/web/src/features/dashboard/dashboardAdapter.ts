import type { DashboardSummary } from './dashboardTypes'

/**
 * S03 dashboard summary — data source seam.
 *
 * F006 ships only the mock. F007 replaces `mockDashboardAdapter` with an
 * implementation that calls `GET /api/platform/dashboard-summary` using the
 * session token; the page never needs to change.
 *
 * The mock honors the exact contract (field names, types, UTC timestamp) so
 * the UI built here is already the UI that will render real data.
 */
export type DashboardAdapter = {
  fetchSummary(): Promise<DashboardSummary>
}

/** Simulated network latency so the loading skeleton is actually visible. */
const MOCK_LATENCY_MS = 900

/**
 * Dev-only demo hook (F006 only, removed in F007) — makes the retryable
 * error states demonstrable in the browser without touching a real API:
 *
 * - `?summaryError=once`    fails the visible initial fetch → the full
 *   error panel with a retry button is shown, and retrying succeeds;
 * - `?summaryError=refetch` lets the initial load succeed, then fails the
 *   first refresh → the compact inline warning is shown while the last
 *   values stay on screen, and retrying succeeds.
 *
 * Fetch numbers assume the development environment (this hook is only used
 * for the F006 browser demo): under React StrictMode the mount effect runs
 * twice, so the visible initial fetch is the second one and the first
 * refresh click is the third. Nothing in the visible UI advertises this; it
 * only affects the mock.
 */
const searchParams =
  typeof window !== 'undefined'
    ? new URLSearchParams(window.location.search)
    : new URLSearchParams()

const faultMode = searchParams.get('summaryError')
const faultFetchNumber =
  faultMode === 'once' ? 2 : faultMode === 'refetch' ? 3 : null

let fetchCount = 0

/**
 * Values are the task-approved mock: the development environment name, a
 * healthy API, and the single seeded development administrator. They mirror
 * what the real B003 endpoint returns at this stage — no invented metrics.
 */
function nextMockSummary(): DashboardSummary {
  return {
    environment: 'Development',
    apiStatus: 'Healthy',
    platformAdminCount: 1,
    generatedAtUtc: new Date().toISOString(),
  }
}

export const mockDashboardAdapter: DashboardAdapter = {
  async fetchSummary() {
    await new Promise((resolve) => setTimeout(resolve, MOCK_LATENCY_MS))
    fetchCount += 1
    if (faultFetchNumber === fetchCount) {
      throw new Error('mock-summary-unavailable')
    }
    return nextMockSummary()
  },
}
