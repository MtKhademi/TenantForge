# S13 — Reliable existing invitations

## Executable tasks and owner
- B014: backend-mentor; atomic duplicate protection and role validation.
- F020: ui-engineer; custom role selection and robust invitation rendering.

## Context and contract
The current backend accepts custom tenant role names, but the frontend treats
role as Owner | Viewer. A valid custom-role response can therefore break the
entire list. The duplicate-active check is not atomic under concurrent requests.
Retain the S10 request/response shapes and permission rules revised in S12.
Owner and Viewer remain supported built-in choices. Custom roles must belong
to the selected tenant. Use the existing tenant roles read to offer custom
choices; no new endpoint is required.

An active invitation means Pending with expiresAtUtc greater than the current
UTC instant. Two simultaneous creates for the same normalized email and tenant
produce exactly one 201, one 409, one active row and one Invitation.Created
audit event. An expired invitation must not prevent a new one. The same email
in another tenant remains independent.

## Visible outcome and demo
Owner creates a custom role, invites an email with it, reloads the page and
sees that role. Repeating the invite shows a Persian conflict message.
An Invitations.View-only member sees the list without a create action.
A foreign-tenant role is rejected with 400 and creates no invitation or event.

## Acceptance and boundaries
Use PostgreSQL transaction/locking or a valid schema-backed solution; a
process-local lock or a precheck alone is insufficient. Do not use a
time-dependent partial-index predicate. Translate only the expected duplicate
condition to 409; unexpected persistence failures must remain observable.
Preserve the invitation and audit transaction as one atomic operation.
Tokens/hashes never appear in responses, logs, browser evidence or UI.
Acceptance and email delivery are separate future features; production must
not imply that an email was sent or offer a working acceptance link.
B014 owns docs/design/s13-invitation-consistency.md and its learning note.
