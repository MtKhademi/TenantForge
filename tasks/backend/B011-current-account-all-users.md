---
id: B011
slice: S02
title: Return the current account for every authenticated user
agent: backend-mentor
source: tasks/slices/002-authenticated-shell.md
---

# Objective

Make `GET /api/auth/me` succeed for any authenticated user, not only platform
administrators. The endpoint is the "who am I" call the frontend session
bootstrap performs after login and on every refresh; rejecting non-admins with
`401` makes the UI sign them out, so no non-admin tenant member can hold a
browser session (discovered while demonstrating F012).

## Scope

- Remove the `isPlatformAdmin` requirement in `CurrentAccountFeature`:
  authenticated callers with valid `sub`, `email` and `name` claims receive
  `200` with their identity; `isPlatformAdmin` stays in the response body so
  the UI can keep gating admin-only views.
- Keep `.RequireAuthorization()` and the missing-claim fail-closed checks.
- Add integration tests: non-admin token → `200` with `isPlatformAdmin: false`;
  platform admin token → `200` with `isPlatformAdmin: true`; missing/invalid
  token → `401`.
- Write `docs/learning/<task-id>-<slug>.md` explaining why "who am I" must not
  be admin-gated and where authorization actually happens.

## Acceptance

- A non-admin account with a valid token receives `200` from `/api/auth/me`
  with correct identity and `isPlatformAdmin: false`.
- Admin behavior and unauthenticated `401` behavior are unchanged.
- Backend build and integration tests pass; learning note is written.
