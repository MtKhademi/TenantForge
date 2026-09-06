# S10 audit log and invitations contract

F015 freezes the frontend contract that B010 must implement and that F016 must
consume without shape changes. F015 ships a `sessionStorage`-backed mock that
uses exactly these request/response shapes and error semantics; F016 replaces
the mock with HTTP calls and keeps the pages unchanged.

## Security boundary (read this first)

An invitation grants a person a role in a tenant; the audit log is an immutable,
tenant-scoped record of sensitive actions. Two rules shape this contract:

1. **No secrets in the wire or the UI.** A real invitation carries a one-time
   acceptance token, but that token is generated, stored **hashed**, and
   delivered by email entirely server-side (B010). It is **not** part of any
   request, response or mock value below, and it must never be logged. The UI
   only ever renders the safe fields in `Invitation` / `AuditEvent`.
2. **Authorization is server-owned.** Hiding the Invitations / Audit pages in
   the navigation is presentation only. B010 must return `403` for a caller
   without the relevant permission, and the denied response must leak nothing
   about the tenant or its data.

## New permission keys

B010 introduces three tenant-scoped permissions, enforced on the endpoints
below. They are **not** added to the S09 role-matrix catalog in this slice (that
catalog is frozen for screens that already exist); they are declared here so the
backend knows what to check and so F016 can grant them to the demo Owner.

| Key | Kind | Purpose |
|---|---|---|
| `IAM.Invitations.View` | read | List the tenant's pending invitations. |
| `IAM.Invitations.Create` | write | Create a tenant invitation. |
| `IAM.Audit.View` | read | List the tenant's audit events. |

A tenant Owner holds all three in the demo. A member without them receives
`403` from both pages (F015 models this with the `?invitationsViewer=member` /
`?auditViewer=member` query flags, the same convention F013 used for roles).

## Invitations

### List pending invitations

`GET /api/tenants/{tenantId}/invitations`

`200`:

```json
{
  "invitations": [
    {
      "id": "inv-1f3a",
      "email": "teammate@company.com",
      "role": "Viewer",
      "status": "Pending",
      "expiresAtUtc": "2030-01-08T10:00:00Z",
      "createdAtUtc": "2030-01-01T10:00:00Z"
    }
  ]
}
```

### Create an invitation

`POST /api/tenants/{tenantId}/invitations`

Request:

```json
{ "email": "teammate@company.com", "role": "Viewer" }
```

Response: `201 Created` with the created `Invitation`.

The `role` is a name string (one of the tenant's assignable roles). The server
normalizes `email` (trim, lower-case) and is the source of truth for the
validity window; the response's `expiresAtUtc` is the value the UI displays.

Errors:

- `400` with `{ "errors": { "email": "...", "role": "..." } }` for an invalid or
  missing email or an unknown role.
- `401` for missing/invalid authentication.
- `403` for a caller without `IAM.Invitations.Create`.
- `409` when an **active** invitation for the same normalized email already
  exists in this tenant — the duplicate-active behavior the milestone makes
  explicit.

### Field semantics

| Field | Notes |
|---|---|
| `id` | Stable invitation record id. |
| `email` | Normalized (trimmed, lower-cased) invitee email. |
| `role` | Role name the invitee receives on acceptance. |
| `status` | `Pending` in this slice. |
| `expiresAtUtc` | UTC ISO timestamp when the invitation stops being valid. |
| `createdAtUtc` | UTC ISO timestamp of creation. |

**Deliberately absent:** any acceptance token (raw or hashed), and the invitee's
account id — the person does not need to exist yet.

## Audit log

### List audit events

`GET /api/tenants/{tenantId}/audit?action={action}&fromUtc={isoUtc}`

Both query parameters are optional. `action` narrows to a single action key;
`fromUtc` narrows to events recorded at or after the given UTC ISO timestamp.
The server returns the tenant's events **newest first**, bounded to a fixed page
(the mock uses 50; B010 owns the real page size and cursor).

`200`:

```json
{
  "events": [
    {
      "id": "evt-9c21",
      "actor": "Sara Rahimi",
      "actorEmail": "sara.rahimi@acme.test",
      "action": "Invitation.Created",
      "target": "teammate@company.com",
      "details": "Invited teammate@company.com with the Viewer role.",
      "createdAtUtc": "2030-01-01T10:00:00Z"
    },
    {
      "id": "evt-41ab",
      "actor": "Sara Rahimi",
      "actorEmail": "sara.rahimi@acme.test",
      "action": "Role.Updated",
      "target": "User Manager",
      "details": "Permission IAM.Users.View was added to the custom role User Manager.",
      "createdAtUtc": "2030-01-01T00:00:00Z"
    }
  ]
}
```

Errors:

- `400` for an unknown `action` key or malformed `fromUtc`.
- `401` for missing/invalid authentication.
- `403` for a caller without `IAM.Audit.View`.

### Action keys

| Key | When recorded |
|---|---|
| `Invitation.Created` | A tenant invitation is created. |
| `Role.Created` | A custom role is created. |
| `Role.Updated` | A role's permission set changes. |
| `Role.Assigned` | A role is assigned to a member. |
| `Role.Unassigned` | A role is removed from a member. |

B010 owns the authoritative set and may add keys without a shape change.

### Field semantics

| Field | Notes |
|---|---|
| `id` | Stable event id. |
| `actor` | Display name of the user who performed the action. |
| `actorEmail` | Normalized email of the user who performed the action. |
| `action` | One of the action keys above. |
| `target` | Short human-readable target (an email, a role name, a member). |
| `details` | One-line server-authored prose. Never a token, hash or password. |
| `createdAtUtc` | UTC ISO timestamp of when the event was recorded. |

**Immutability and scoping.** Events are append-only: there is no update or
delete endpoint, and the UI has no path to mutate them. Every query is scoped to
`{tenantId}`, and B010 must enforce that an event from another tenant is never
returned — audit results never cross tenant boundaries.

## Development acceptance link

The first milestone may show a stand-in for the emailed acceptance link instead
of sending real email. F015 renders a clearly labeled, non-HTTP sample
(`acceptance://invite/{id}`) that is **gated on the Vite dev build** and never
appears in a production build. B010 does not need to implement a URL for this;
the label and the gating are the requirements.

## What is out of scope for B010 / this slice

- Production email delivery (provider, SMTP, templates).
- Accepting an invitation for an entirely new account (next visible slice).
- Audit export, retention policies and external SIEM integration.
- An event bus or outbox introduced solely for audit recording.
