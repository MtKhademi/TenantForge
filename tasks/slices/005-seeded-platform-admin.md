# S05 — Seeded platform administrator

## Executable tasks

- Backend: `B005`

## Owner

`backend-mentor`

## Depends on

- S04.

## Visible outcome

The same login screen authenticates a platform administrator stored in PostgreSQL. Recreating the database and starting the API creates that administrator once; repeated startups never duplicate it.

## Demo

1. Remove the local database volume and start from a clean database.
2. Start the API and observe successful migration/seed completion without logging secrets.
3. Sign in through the unchanged login page.
4. Restart the API several times and sign in again.
5. Verify exactly one platform administrator exists.
6. Prove the old hardcoded credential checker is no longer used.

## UI states

No new visual design. Existing real login states remain unchanged.

## API contract

The S01 login and S02 current-account contracts remain unchanged.

## Backend learning goal

Understand idempotent startup seeding, password hashing and verification, database-backed credential lookup, normalized identity and replacing an implementation behind a stable API contract.

## Scope

- Configure seed identity through safe local configuration or secrets.
- Hash the configured initial password using an established password hasher.
- Create the administrator only when the normalized identity is absent.
- Detect and report unsafe or incomplete production seed configuration without exposing secrets.
- Replace the development credential source with the IAM account store.
- Issue authentication from database identity and status.
- Update dashboard administrator count from persisted accounts.
- Add integration tests for first seed, repeated seed, valid login, wrong password and disabled account.
- Write `docs/learning/s05-seeded-platform-admin.md`.

## Out of scope

- Admin creation UI.
- Multiple platform roles.
- User registration and password recovery.
- Refresh-token rotation.
- Tenants and tenant roles.

## Acceptance criteria

- [ ] Clean database startup creates exactly one configured platform administrator.
- [ ] Repeated and concurrent-safe startup behavior does not duplicate the account.
- [ ] Login reads the persisted account and verifies a hash.
- [ ] Wrong password and disabled account return generic `401` without leaking account state.
- [ ] Secrets and password hashes do not appear in logs or API responses.
- [ ] S01 and S02 response contracts remain compatible.
- [ ] Dashboard count comes from persisted data.
- [ ] Integration and browser tests pass.
- [ ] Learning note explains idempotency, normalization and password hashing.
