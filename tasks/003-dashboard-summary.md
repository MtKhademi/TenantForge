# S03 — Real dashboard summary

## Status

`Planned`

## Owner

`ui-engineer` defines the small visible contract, `backend-mentor` implements it, then `ui-engineer` integrates it.

## Depends on

- S02.

## Visible outcome

The dashboard replaces placeholder content with a small, honest system summary: current environment, API status and the current platform-administrator count available at this stage.

## Demo

1. Sign in and open the dashboard.
2. See summary values loaded from the API.
3. Refresh the data without losing the whole page layout.
4. Stop the API or force a server error and see a retryable dashboard error state.

## UI states

- Initial skeleton.
- Loaded summary.
- Background refetch.
- Retryable error.

## API contract

### `GET /api/platform/dashboard-summary`

Success `200`:

```json
{
  "environment": "Development",
  "apiStatus": "Healthy",
  "platformAdminCount": 1,
  "generatedAtUtc": "2030-01-01T00:00:00Z"
}
```

The endpoint requires the platform-administrator claim.

## Backend learning goal

Understand a protected query slice, response contracts, authorization at the endpoint boundary and frontend server-state fetching.

## Scope

- Replace dashboard placeholder cards with only the contract values.
- Add a protected query endpoint.
- Compute values honestly from current state; no fake trend percentages or charts.
- Add integration coverage for valid admin and unauthorized access.
- Write `docs/learning/s03-dashboard-summary.md`.

## Out of scope

- Database or stored metrics.
- User, tenant or invitation counts that do not exist yet.
- Charts, activity feeds and audit logs.
- Caching.

## Acceptance criteria

- [ ] Dashboard shows real API data and no invented business metrics.
- [ ] Loading and error states preserve the application shell.
- [ ] Unauthorized access returns `401` or `403` as appropriate.
- [ ] API response uses UTC and a stable contract.
- [ ] Integration, frontend and browser tests pass.
- [ ] Learning note explains the protected query request flow.
