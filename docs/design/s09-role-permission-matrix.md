# S09 role permission matrix contract

F013 freezes the frontend contract that B009 must implement and that F014 must consume without shape changes.

## Permission catalog

`GET /api/permissions/catalog`

```json
{
  "groups": [
    {
      "id": "dashboard",
      "label": "داشبورد",
      "description": "دسترسی خواندن به نمای خلاصه وضعیت مستأجر.",
      "permissions": [
        {
          "key": "IAM.Dashboard.View",
          "label": "مشاهده داشبورد",
          "description": "اجازه دیدن خلاصه‌ها و شاخص‌های صفحه داشبورد.",
          "kind": "read"
        }
      ]
    }
  ]
}
```

Stable keys introduced in S09 are limited to screens that already exist:

| Group | Key | Kind | Purpose |
|---|---|---|---|
| Dashboard | `IAM.Dashboard.View` | `read` | Open/read the dashboard summary. |
| Users | `IAM.Users.View` | `read` | Read the user-management list. |
| Users | `IAM.Users.Create` | `write` | Create a platform user. |
| Tenants | `IAM.Tenants.View` | `read` | Read tenant-management data. |
| Tenants | `IAM.Tenants.Create` | `write` | Create a tenant and first Owner membership. |

No wildcard keys, platform custom roles or direct per-user grants are part of S09.

## Tenant roles

`GET /api/tenants/{tenantId}/roles`

```json
{
  "roles": [
    {
      "id": "role-id",
      "name": "User Manager",
      "description": "نقش سفارشی مستأجر؛ قابل ویرایش و قابل انتساب به اعضا.",
      "kind": "custom",
      "permissionKeys": ["IAM.Users.View", "IAM.Users.Create"],
      "memberIds": ["tenant-member-id"],
      "createdAtUtc": "2030-01-01T00:00:00Z",
      "updatedAtUtc": "2030-01-01T00:00:00Z"
    }
  ]
}
```

`POST /api/tenants/{tenantId}/roles`

Request:

```json
{
  "name": "User Manager",
  "permissionKeys": ["IAM.Users.View", "IAM.Users.Create"]
}
```

Response: `201 Created` with the created `TenantRole`.

Errors:

- `400` with `{ "errors": { "name": "...", "permissionKeys": "..." } }` for invalid names or unknown permission keys.
- `401` for missing/invalid authentication.
- `403` for non-members or members who are not effective Owners of the tenant.
- `409` when another role in the same tenant has the same normalized name.

`PUT /api/tenants/{tenantId}/roles/{roleId}`

Request:

```json
{
  "permissionKeys": ["IAM.Users.View"]
}
```

Response: `200 OK` with the updated `TenantRole`.

Errors match create. Built-in roles return `409` when a permission change is attempted.

## Role assignment

Assign:

`PUT /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`

Unassign:

`DELETE /api/tenants/{tenantId}/members/{memberId}/roles/{roleId}`

Response: `200 OK` with `{ "roles": TenantRole[] }` so the UI can refresh the whole matrix atomically.

Errors:

- `401` for missing/invalid authentication.
- `403` for non-members or members who are not effective Owners.
- `404` only when the caller is authorized for the tenant but the member or role id is not part of that tenant.
- `409` if the operation would remove or disable the tenant's last effective Owner.

## Resolved permissions for current tenant

`GET /api/tenants/{tenantId}/me/permissions`

```json
{
  "permissions": ["IAM.Dashboard.View", "IAM.Users.View"]
}
```

This endpoint is consumed by F014 to adjust navigation visibility. It is not an authorization boundary; all protected API operations must still enforce the required permission server-side.

## Security expectations

- Tenant role reads and writes are tenant-scoped and require active tenant membership.
- Role management requires an effective Owner in the selected tenant.
- A hidden or manually re-enabled UI control must not bypass API authorization.
- Last-effective-Owner protection is enforced by the server on assignment and unassignment.
- Role names are unique per tenant only; the same role name may exist in another tenant with different permissions.
