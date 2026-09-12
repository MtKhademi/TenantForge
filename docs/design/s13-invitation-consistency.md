# S13 invitation consistency contract

B014 freezes the backend behavior that F020 must consume without changing the
HTTP shapes already introduced in S10.

## Preserved endpoint contract

### `GET /api/tenants/{tenantId}/invitations`

- Requires `IAM.Invitations.View` or Owner membership.
- Returns the existing shape:

```json
{
  "invitations": [
    {
      "id": "guid",
      "email": "teammate@company.com",
      "role": "Viewer",
      "status": "Pending",
      "expiresAtUtc": "utc-iso",
      "createdAtUtc": "utc-iso"
    }
  ]
}
```

- Only active invitations are returned: `Pending` and `expiresAtUtc` greater
  than the current UTC instant.
- The response never exposes a raw token or hash.

### `POST /api/tenants/{tenantId}/invitations`

Request shape remains:

```json
{ "email": "teammate@company.com", "role": "Viewer" }
```

- Requires `IAM.Invitations.Create` or Owner membership.
- `email` is trimmed and lower-cased before validation, duplicate detection,
  persistence and audit details.
- `role` is trimmed before persistence.
- The response remains `201 Created` with the created invitation in the
  existing `InvitationResponse` shape.

## Role validation

`role` may be one of:

- the built-in invitation roles `Owner` or `Viewer`;
- the exact name of a custom role that belongs to the target tenant.

Unknown role names and custom roles from another tenant return `400` with a
`role` validation error. No invitation row or audit event is created for that
request.

The tenant role read endpoint already returns the names that F020 can offer as
custom choices; B014 does not add a role-selection endpoint.

## Duplicate-active semantics

An invitation is active when:

- `tenant_id` matches the target tenant;
- `normalized_email` matches the incoming normalized email;
- `status` is `Pending`;
- `expires_at_utc` is greater than the current UTC instant.

For a given tenant and normalized email:

- one active invitation may exist;
- a second create while an active invitation exists returns `409`;
- an expired invitation does not block a new invitation;
- an invitation for the same email in another tenant is independent.

## Concurrency rule

Two simultaneous create requests for the same tenant and normalized email must
produce:

- exactly one `201`;
- exactly one `409`;
- exactly one active invitation row;
- exactly one `Invitation.Created` audit event.

The implementation uses a PostgreSQL transaction-scoped advisory lock keyed by
the tenant and normalized email. This is a cross-process database lock, not an
in-process lock. The duplicate check and insert happen inside the same
transaction, and the invitation row and audit event commit together.

No time-dependent partial-index predicate is used because `now()` in an index
predicate cannot define the product rule "expired now is inactive" in a stable,
portable schema.

## Security boundary

- Platform administration does not bypass tenant membership.
- Permission denial remains `403`.
- A tenant's invitation data is never visible to another tenant.
- Tokens are generated server-side, stored only as a SHA-256 hash, and never
  included in API responses, audit details, logs or browser-visible payloads.

## Out of scope

- No invitation acceptance.
- No account registration from an invitation.
- No email delivery.
- No resend or revoke operations.
- No audit export.
- No new endpoint.
- No frontend changes; F020 owns the UI consumer.
