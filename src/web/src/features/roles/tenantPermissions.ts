import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { httpRoleAdapter } from './roleAdapter'
import {
  AUDIT_VIEW_KEY,
  INVITATIONS_CREATE_KEY,
  INVITATIONS_VIEW_KEY,
  ROLES_MANAGE_KEY,
  type PermissionCatalogResponse,
  type PermissionKey,
} from './roleTypes'

/**
 * S12 (F019): the only place the UI learns the server permission catalog and
 * the caller's tenant permissions.
 *
 * Two server answers drive the shell:
 *
 * - `GET /api/permissions/catalog` (auth-only) — the permission matrix source
 *   of truth: keys, Persian labels, descriptions and grouping. Fetched once
 *   per app load (module cache) and consumed only by the role editor; a
 *   failure never falls back to a local copy, so a broken catalog renders a
 *   retryable error, never a fabricated matrix.
 * - `GET /api/tenants/{tenantId}/me/permissions` — the server-resolved
 *   permission set for the current tenant (Owner → all four keys; members →
 *   the union of their assigned active roles). Cached per (account, tenant)
 *   so the navigation guards and the tenant pages share one request, and
 *   re-fetched whenever the route tenant changes, so tenant switching,
 *   reload and Back/Forward always re-derive capabilities.
 *
 * Missing, unresolved or failed permissions resolve to **no capability** —
 * hiding a control is presentation only, B013 denies the underlying request
 * with 403. A `401` anywhere hands back to the auth lifecycle (sign out).
 */

type CatalogState =
  | { kind: 'loading' }
  | { kind: 'loaded'; catalog: PermissionCatalogResponse }
  | { kind: 'error' }

// The catalog is static server data for the process; one fetch per app load
// serves every consumer. A failed fetch clears the cache so `retryCatalog`
// actually retries.
let cachedCatalog: PermissionCatalogResponse | null = null
let inFlightCatalog: Promise<PermissionCatalogResponse> | null = null

type ResolvedCacheEntry = {
  account: string
  tenantId: string
  permissions: Set<PermissionKey>
}

let resolvedCache: ResolvedCacheEntry | null = null
let inFlightResolved: {
  account: string
  tenantId: string
  promise: Promise<Set<PermissionKey>>
} | null = null

function accountKey(accessToken: string | undefined, email: string | undefined): string {
  return `${email ?? ''}::${accessToken ?? ''}`
}

export type PermissionCatalogState = {
  catalog: CatalogState
  retryCatalog(): void
}

/**
 * The server permission catalog. Only the role editor consumes this; the
 * navigation guards must not trigger a catalog request merely by rendering.
 */
export function usePermissionCatalog(): PermissionCatalogState {
  const { session, signOut } = useAuth()
  const [state, setState] = useState<CatalogState>(
    cachedCatalog ? { kind: 'loaded', catalog: cachedCatalog } : { kind: 'loading' },
  )
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const load = useCallback(() => {
    if (cachedCatalog) {
      setState({ kind: 'loaded', catalog: cachedCatalog })
      return
    }
    inFlightCatalog ??= httpRoleAdapter
      .fetchPermissionCatalog(sessionRef.current?.accessToken ?? '')
      .then((catalog) => {
        cachedCatalog = catalog
        inFlightCatalog = null
        return catalog
      })
      .catch((error) => {
        inFlightCatalog = null
        throw error
      })
    inFlightCatalog
      .then((catalog) => setState({ kind: 'loaded', catalog }))
      .catch((error) => {
        if (error instanceof SessionExpiredError) {
          void signOutRef.current()
          return
        }
        setState({ kind: 'error' })
      })
  }, [])

  const retryCatalog = useCallback(() => {
    if (cachedCatalog) return
    setState({ kind: 'loading' })
    load()
  }, [load])

  useEffect(() => {
    load()
  }, [load])

  return { catalog: state, retryCatalog }
}

export type TenantPermissionsState = {
  /** While the current tenant's resolved permissions are in flight. */
  isResolving: boolean
  /**
   * The server-resolved permission keys for the current tenant; `null` while
   * unresolved (or on failure) — capabilities then resolve to false.
   */
  permissions: Set<PermissionKey> | null
  /** True once the current tenant's resolved permission set has settled. */
  isResolved: boolean
  /** `IAM.Roles.Manage` — role create/update/assign/unassign. */
  canManageRoles: boolean
  /** `IAM.Invitations.View` — invitation list navigation and read. */
  canViewInvitations: boolean
  /** `IAM.Invitations.Create` — the invitation create form and action. */
  canCreateInvitations: boolean
  /** `IAM.Audit.View` — audit log navigation and read. */
  canViewAudit: boolean
  /** Re-fetch the current tenant's resolved permissions (e.g. after a role change by someone else). */
  refresh(): void
}

function fetchResolvedPermissions(
  account: string,
  accessToken: string,
  tenantId: string,
  force: boolean,
): Promise<Set<PermissionKey>> {
  if (!force && resolvedCache && resolvedCache.account === account && resolvedCache.tenantId === tenantId) {
    return Promise.resolve(resolvedCache.permissions)
  }
  if (inFlightResolved && inFlightResolved.account === account && inFlightResolved.tenantId === tenantId) {
    return inFlightResolved.promise
  }
  const promise = httpRoleAdapter
    .getCurrentTenantPermissions(accessToken, tenantId)
    .then((response) => {
      const permissions = new Set(response.permissions)
      resolvedCache = { account, tenantId, permissions }
      if (inFlightResolved?.promise === promise) inFlightResolved = null
      return permissions
    })
    .catch((error) => {
      if (inFlightResolved?.promise === promise) inFlightResolved = null
      // Keep errors here for callers that need to react (401 → sign out);
      // the hook treats any other failure as "no capability" (fail closed).
      throw error
    })
  inFlightResolved = { account, tenantId, promise }
  return promise
}

/**
 * Resolved current-tenant permissions for the route tenant. With no tenant
 * (platform scope) nothing resolves and no request is made. A `401` signs
 * the user out; any other failure (403 not-a-member, 404, transport) leaves
 * capabilities unresolved — the UI hides gated controls and the server
 * remains the authority.
 */
export function useTenantPermissions(tenantId: string | undefined): TenantPermissionsState {
  const { session, signOut } = useAuth()
  const account = accountKey(session?.accessToken, session?.user.email)

  const [permissions, setPermissions] = useState<Set<PermissionKey> | null>(() => {
    if (!tenantId) return null
    return resolvedCache && resolvedCache.account === account && resolvedCache.tenantId === tenantId
      ? resolvedCache.permissions
      : null
  })
  const [isResolving, setIsResolving] = useState(Boolean(tenantId))

  const requestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const load = useCallback(
    (force: boolean) => {
      if (!tenantId) return
      const requestId = ++requestIdRef.current
      setIsResolving(true)
      fetchResolvedPermissions(account, sessionRef.current?.accessToken ?? '', tenantId, force)
        .then((resolved) => {
          if (requestId !== requestIdRef.current) return
          setPermissions(resolved)
        })
        .catch((error) => {
          if (requestId !== requestIdRef.current) return
          if (error instanceof SessionExpiredError) {
            void signOutRef.current()
            return
          }
          setPermissions(null)
        })
        .finally(() => {
          if (requestId === requestIdRef.current) setIsResolving(false)
        })
    },
    [account, tenantId],
  )

  useEffect(() => {
    // A tenant change (or reload on a tenant route) discards the previous
    // tenant's resolved set before its own request settles.
    setPermissions(
      tenantId && resolvedCache && resolvedCache.account === account && resolvedCache.tenantId === tenantId
        ? resolvedCache.permissions
        : null,
    )
    load(false)
  }, [account, load, tenantId])

  const refresh = useCallback(() => {
    load(true)
  }, [load])

  const has = (key: PermissionKey) => permissions?.has(key) ?? false

  return {
    isResolving,
    permissions,
    isResolved: !isResolving && permissions !== null,
    canManageRoles: has(ROLES_MANAGE_KEY),
    canViewInvitations: has(INVITATIONS_VIEW_KEY),
    canCreateInvitations: has(INVITATIONS_CREATE_KEY),
    canViewAudit: has(AUDIT_VIEW_KEY),
    refresh,
  }
}
