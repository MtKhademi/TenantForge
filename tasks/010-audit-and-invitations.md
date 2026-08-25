# S10 — Audit log and invitations

## Status

`Planned`

## Owner

`ui-engineer`, then `backend-mentor`, then `ui-engineer`.

## Depends on

- S09.

## Visible outcome

A tenant Owner invites a person by email, sees the pending invitation, and inspects an audit log containing the invitation and recent role/permission changes.

## Demo

1. Enter Acme as Owner and open Invitations.
2. Invite a new email with a selected role.
3. See the pending invitation and its expiry.
4. Open Audit Log.
5. See the invitation and a previous role/permission change with actor, action, target and time.
6. Attempt both pages as a member without permission and see `403`.

## UI states

- Invitation list empty/loaded.
- Invite form validation/submitting/conflict/success.
- Audit list loading/empty/loaded.
- Basic filtering by action or date if it remains within the task contract.
- Forbidden pages.

## API contract

The UI mock freezes contracts for:

- listing tenant invitations;
- creating a tenant invitation;
- listing tenant audit events.

The first milestone may display a development acceptance link instead of sending real email, but it must label that behavior clearly and never expose it in production mode.

## Backend learning goal

Understand expiring invitation tokens, safe token storage, tenant-scoped uniqueness, immutable audit records and capturing actor/target metadata around sensitive commands.

## Scope

- Create expiring single-use invitations with a role selection.
- Store only a hash of any acceptance token.
- Add pending invitation list and create flow.
- Record immutable audit events for invitation creation and role/permission changes.
- Add tenant-scoped audit query with bounded pagination.
- Enforce dedicated invitation and audit permissions.
- Add integration and browser tests.
- Write `docs/learning/s10-audit-and-invitations.md`.

## Out of scope

- Production email provider.
- Invitation acceptance for an entirely new account if it requires a larger registration flow; split it into the next visible slice.
- Audit export, retention policies and external SIEM integration.
- Event bus or outbox introduced only for audit recording.
- Billing and subscriptions.

## Acceptance criteria

- [ ] Owner creates a tenant-scoped pending invitation with expiry and role.
- [ ] Duplicate active invitation behavior is explicit and tested.
- [ ] Raw acceptance tokens are not stored or logged.
- [ ] Invitation and role/permission changes create useful immutable audit events.
- [ ] Audit results never cross tenant boundaries.
- [ ] Missing permissions return `403` from the API.
- [ ] Integration, frontend and browser tests pass.
- [ ] Learning note explains token hashing and audit boundaries.
