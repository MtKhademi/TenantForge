/**
 * S03 dashboard summary — the fixed request/response contract.
 *
 * The backend (B003) implements this contract; F006 mocked it and F007
 * connected the real endpoint. The shape here is the source of truth both
 * sides must keep in sync.
 */

/**
 * `GET /api/platform/dashboard-summary`
 *
 * Requires the platform-administrator claim:
 *   - `401` when unauthenticated
 *   - `403` when authenticated but not a platform administrator
 *
 * Success `200` body:
 *
 * ```json
 * {
 *   "environment": "Development",
 *   "apiStatus": "Healthy",
 *   "platformAdminCount": 1,
 *   "generatedAtUtc": "2030-01-01T00:00:00Z"
 * }
 * ```
 *
 * All values are computed honestly from current server state — no trend
 * percentages, no charts, no stored metrics. `generatedAtUtc` is always UTC.
 */
export type DashboardSummary = {
  /** Hosting environment the API is currently running in. */
  environment: string
  /** Live API health as of the response. */
  apiStatus: string
  /** Number of platform administrators available at this stage. */
  platformAdminCount: number
  /**
   * UTC timestamp of when the summary was generated. The API emits .NET "O"
   * form (`...+00:00`); a `Z` suffix is also valid UTC and must be accepted.
   */
  generatedAtUtc: string
}
