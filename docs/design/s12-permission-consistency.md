# S12 permission consistency contract

B013 freezes the backend contract that F019 must consume without shape changes.
It supersedes the tenant-permission portions of S09 and ends the S10 deferral
for invitation/audit keys.

## Contract decision

Tenant permissions now describe only tenant-scoped operations that exist in the
current product. Platform/dashboard/user-directory/tenant-management keys are no
longer tenant permissions. Platform access remains controlled by platform-admin
authorization, not by tenant roles.

The tenant permission catalog contains exactly four keys:

| Group | Key | Kind | Purpose |
|---|---|---|---|
| Roles | `IAM.Roles.Manage` | `write` | Create/update roles and assign/unassign roles to tenant members. |
| Invitations | `IAM.Invitations.View` | `read` | List pending tenant invitations. |
| Invitations | `IAM.Invitations.Create` | `write` | Create a pending tenant invitation. |
| Audit | `IAM.Audit.View` | `read` | Read the tenant audit log. |

The response shape of `GET /api/permissions/catalog` stays unchanged:

```json
{
  "groups": [
    {
      "id": "roles",
      "label": "نقش‌ها",
      "description": "مدیریت نقش‌ها و مجوزهای مستأجر.",
      "permissions": [
        {
          "key": "IAM.Roles.Manage",
          "label": "مدیریت نقش‌ها",
          "description": "اجازه ایجاد، ویرایش و تخصیص نقش‌های مستأجر.",
          "kind": "write"
        }
      ]
    }
  ]
}
```

## Persisted role migration

Existing rows in `iam_tenant_roles.permission_keys` are migrated in place:

- `IAM.Tenants.Create` becomes `IAM.Roles.Manage`.
- `IAM.Dashboard.View`, `IAM.Users.View`, `IAM.Users.Create` and
  `IAM.Tenants.View` are removed.
- Existing `IAM.Invitations.View`, `IAM.Invitations.Create`, `IAM.Audit.View`
  and `IAM.Roles.Manage` values are preserved.
- Duplicates are removed and the final array is sorted/stable.
- Role ids, role names, timestamps and member assignments are preserved.
- Invitation/audit permissions are **not** manufactured for ordinary custom
  roles; only grants that already existed remain.

New role create/update requests containing obsolete or unknown keys return `400`
with a field-specific `permissionKeys` validation problem.

## Authorization rules

All tenant-scoped role, permission, invitation and audit operations use the same
base decision:

1. Parse `{tenantId}` and authenticated `sub` as non-empty GUIDs.
2. Verify the account row exists and is `Active`.
3. Verify the tenant row exists and is `Active`.
4. Verify an active membership row exists for `(tenantId, accountId)`.
5. Resolve permissions from membership role plus assigned tenant roles.

If any of the first four checks fails, the operation returns `403` and leaks no
tenant data. Platform administration is not a bypass for tenant APIs.

### Role endpoints

- `GET /api/tenants/{tenantId}/roles`: any active member may read roles. This
  supports invitation role selection.
- `POST /api/tenants/{tenantId}/roles`: requires membership `Owner` or
  `IAM.Roles.Manage`.
- `PUT /api/tenants/{tenantId}/roles/{roleId}`: requires membership `Owner` or
  `IAM.Roles.Manage`; role must belong to the tenant; built-in role changes
  remain `409`.
- `PUT /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`: requires
  membership `Owner` or `IAM.Roles.Manage`; missing member/role within an
  authorized tenant remains `404`.
- `DELETE /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`: same as
  assign; missing assignment remains an idempotent `200` response with the role
  list when the member/role exists.

Successful role create/update/assign/unassign operations keep existing audit
record behavior.

### Resolved permissions

`GET /api/tenants/{tenantId}/me/permissions` returns exactly the server-resolved
permission set used by protected endpoints:

- membership `Owner` resolves all four catalog keys;
- ordinary members resolve the union of their assigned active tenant roles;
- obsolete keys are not returned.

### Invitations and audit

- `GET /api/tenants/{tenantId}/invitations`: requires
  `IAM.Invitations.View` (or Owner membership).
- `POST /api/tenants/{tenantId}/invitations`: requires
  `IAM.Invitations.Create` (or Owner membership).
- `GET /api/tenants/{tenantId}/audit`: requires `IAM.Audit.View` (or Owner
  membership).

A member with `IAM.Roles.Manage` gains no invitation or audit permissions unless
those keys are also explicitly assigned. A member with invitation view may list
invitations but cannot create them or read audit.

## Last-role-administrator protection

An effective role administrator is a **distinct active account** in an active
tenant with either:

- membership role `Owner`, or
- at least one assigned role containing `IAM.Roles.Manage`.

The server evaluates the proposed final state before role update or unassignment
commits. The operation returns `409` when the proposed mutation would leave zero
effective role administrators.

Important counting rules:

- two assignments to the same account count once;
- a role assigned to several members contributes several distinct accounts;
- a separate membership Owner or separate `IAM.Roles.Manage` assignment keeps
  the tenant administrable;
- disabled accounts, suspended tenants and removed memberships do not count.

Concurrent role-management mutations are protected by a tenant-scoped database
transaction/lock around final-state evaluation and mutation so two successful
requests cannot collectively remove the final administrator.

## What remains out of scope

- No new endpoint.
- No frontend implementation; F019 consumes this contract.
- No platform custom roles, direct per-user grants, deny rules, caching or
  generalized authorization framework.
- No invitation acceptance or email delivery.
- No pagination changes.
