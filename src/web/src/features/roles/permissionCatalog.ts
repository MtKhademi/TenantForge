import type { PermissionKey } from './roleTypes'

/**
 * S12 (F019): the permission catalog is **server-owned**. `GET
 * /api/permissions/catalog` (B013) is the single source of truth for the
 * four tenant permission keys, their Persian labels, descriptions and
 * grouping; the UI renders it as sent and never falls back to a local copy.
 *
 * This module keeps only the compile-time contract: the stable keys and their
 * canonical order (roles → invitations → audit → shop), used to validate the
 * server response and to order the role-editor matrix. Labels never live here.
 * S31 (F043): the Shop keys added by B035 must be listed here too — this is a
 * runtime allowlist, so a key present in the `PermissionKey` type but missing
 * here would make `isPermissionKey` reject a valid server response and the
 * strict adapter fail closed (the role editor and gated nav would break).
 */
const CATALOG_KEY_ORDER: PermissionKey[] = [
  'IAM.Roles.Manage',
  'IAM.Invitations.View',
  'IAM.Invitations.Create',
  'IAM.Audit.View',
  'Shop.Catalog.Manage',
  'Shop.Shipping.Manage',
]

const keySet = new Set<string>(CATALOG_KEY_ORDER)

export function isPermissionKey(value: unknown): value is PermissionKey {
  return typeof value === 'string' && keySet.has(value)
}

/** Canonical key order for the role-editor matrix, independent of group order. */
export const CATALOG_KEYS = CATALOG_KEY_ORDER
