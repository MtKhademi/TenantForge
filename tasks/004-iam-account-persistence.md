# S04 — IAM account persistence

## Status

`Planned`

## Owner

`backend-mentor`

## Depends on

- S03.

## Visible outcome

The existing login and dashboard continue to work unchanged while the repository gains a real PostgreSQL-backed account model visible through migrations and integration tests. This is an intentionally behavior-preserving backend slice before S05 switches credential sources.

## Demo

1. Start PostgreSQL and the applications from a clean state.
2. Apply the IAM migration.
3. Run the existing S01–S03 browser flow unchanged.
4. Run persistence integration tests against a real PostgreSQL container.

## UI states

No new UI. Existing login and dashboard must not regress.

## API contract

No API contract changes.

## Backend learning goal

Understand EF Core model ownership, DbContext configuration, PostgreSQL migrations, password-hash storage boundaries and integration testing with Testcontainers without prematurely changing authentication behavior.

## Scope

- Add PostgreSQL local configuration and Docker Compose infrastructure.
- Introduce the minimum IAM account entity and EF Core mapping.
- Store normalized email, display name, password hash, platform-admin flag, status and UTC timestamps.
- Add database constraints required by the model.
- Generate the initial IAM migration.
- Add repository or direct data access only where required by S05's known authentication use case.
- Add Testcontainers-based persistence tests.
- Write `docs/learning/s04-iam-account-persistence.md`.

## Out of scope

- Switching login from development stub to database.
- Seeding an administrator.
- Registration, password change or recovery.
- Tenant, membership, roles and permissions.
- Generic repository framework or unit of work.

## Acceptance criteria

- [ ] A clean PostgreSQL database can be created through documented commands.
- [ ] Migration applies successfully and is represented in source control.
- [ ] Email uniqueness and required constraints are enforced by PostgreSQL.
- [ ] Only a password hash can be stored; plaintext has no model field.
- [ ] Existing login/dashboard browser flow remains unchanged.
- [ ] Integration tests execute against real PostgreSQL.
- [ ] Learning note separates domain choices, EF mapping and generated migration code.
