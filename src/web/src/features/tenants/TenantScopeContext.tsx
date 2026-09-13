import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/features/auth/AuthContext'
import { SessionExpiredError } from '@/features/auth/authTypes'
import { DEFAULT_PAGE_SIZE, type PaginationMeta } from '@/features/pagination/paginationTypes'
import { httpTenantAdapter, TenantForbiddenError } from './tenantAdapter'
import { httpTenantDiscoveryAdapter } from './tenantDiscoveryAdapter'
import type { MembershipRole } from './tenantTypes'

/**
 * S07 tenant scope — stable client context (agreed in F010, kept by F011/B007),
 * made scope-aware in S11 (F018/B012).
 *
 * This context owns the **scopes the signed-in user may enter**, so the
 * switcher (in the shell header), the in-shell membership chooser and the
 * tenants page all read the same in-flight/loaded data:
 *
 * - a **platform administrator** reads the platform tenant discovery pages
 *   (`GET /api/platform/tenants`) and normalizes them into switcher `scopes`;
 * - an **ordinary account** reads only its own active memberships
 *   (`GET /api/auth/me/tenants`) and **never** calls the platform list, so an
 *   ordinary login/reload makes no platform request and no 403.
 *
 * It represents **selection**, not authorization: `selectTenant`/`selectHome`
 * only navigate. The **URL (`/t/:tenantId`) is the single source of truth** for
 * which tenant is active — consumers derive it with `useParams`, so there is no
 * second "active tenant" state to drift. Whether the signed-in user may
 * actually work inside a tenant is always decided server-side (S08); nothing
 * here grants or hides privileged behavior.
 *
 * The route carries the tenant **id** (not the slug) because S08's
 * `GET /api/tenants/{tenantId}/members` is keyed by id, and non-admin members
 * cannot call the platform tenant list to resolve a slug. The switcher and the
 * tenants page navigate by id; lookups against the loaded list are by id.
 */
/** A switcher/chooser entry: the fields every scope needs, and nothing more. */
export type ScopeEntry = {
  id: string
  name: string
  slug: string
  /**
   * The caller's own membership kind. Present for ordinary accounts (from the
   * discovery response) so the membership chooser can localize it (مالک / عضو).
   * Absent for a platform administrator's platform-list scopes, which carry no
   * membership-kind field.
   */
  membershipRole?: MembershipRole
}

export type TenantScopeState = {
  isPlatformAdmin: boolean
  /**
   * The scopes the signed-in user may enter. Admin: the platform tenant list.
   * Ordinary account: the caller's own memberships. `null` until the first
   * fetch settles.
   */
  scopes: ScopeEntry[] | null
  /**
   * Platform tenant summaries (`memberCount`, `createdAtUtc`, ...). Populated
   * only for a platform administrator; `null` for ordinary accounts, who never
   * call the platform list.
   */
  /** Metadata for the loaded scope page(s), null until the first fetch settles. */
  scopePagination: PaginationMeta | null
  /** True while any scope fetch is in flight (initial, refresh or load-more). */
  isBusy: boolean
  /** Set only when a fetch failed and no previous data is on screen. */
  failure: 'unavailable' | 'forbidden' | null
  /** Re-fetch page 1 (e.g. after creating a tenant). Superseding is safe. */
  refresh(): void
  /** Load the next bounded page of scope options when available. */
  loadMoreScopes(): void
  /** Look up a scope by id; `null` when unknown or not loaded yet. */
  getScopeById(id: string): ScopeEntry | null
  /** Navigate into a tenant's scoped shell (selection only). */
  selectTenant(id: string): void
  /** Navigate back to the account's home (`/`): admin → dashboard, member → chooser. */
  selectHome(): void
}

const TenantScopeContext = createContext<TenantScopeState | null>(null)

function toScopeEntry(tenant: { id: string; name: string; slug: string }): ScopeEntry {
  return { id: tenant.id, name: tenant.name, slug: tenant.slug }
}

export function TenantScopeProvider({ children }: { children: ReactNode }) {
  const { session, signOut } = useAuth()
  const navigate = useNavigate()
  const isPlatformAdmin = session?.user.isPlatformAdmin ?? false

  const [scopes, setScopes] = useState<ScopeEntry[] | null>(null)
  const [scopePagination, setScopePagination] = useState<PaginationMeta | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [failure, setFailure] = useState<'unavailable' | 'forbidden' | null>(null)

  const listRequestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)
  const isPlatformAdminRef = useRef(isPlatformAdmin)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])
  useEffect(() => {
    isPlatformAdminRef.current = isPlatformAdmin
  }, [isPlatformAdmin])

  const handleFailure = useCallback((error: unknown) => {
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    setFailure(error instanceof TenantForbiddenError ? 'forbidden' : 'unavailable')
  }, [])

  /**
   * Branch the data source on the caller's scope: platform admin → the platform
   * tenant list; ordinary account → `GET /api/auth/me/tenants`. Both resolve to
   * the same normalized shape so callers never need to know which one ran.
   */
  const fetchCurrent = useCallback(async (pageNumber: number) => {
    const token = sessionRef.current?.accessToken ?? ''
    const page = { pageNumber, pageSize: DEFAULT_PAGE_SIZE }
    if (isPlatformAdminRef.current) {
      const response = await httpTenantAdapter.listTenants(token, page)
      return { scopes: response.tenants.map(toScopeEntry), pagination: response.pagination }
    }
    const response = await httpTenantDiscoveryAdapter.listMyTenants(token, page)
    const scopes = response.tenants.map((tenant) => ({
      id: tenant.id,
      name: tenant.name,
      slug: tenant.slug,
      membershipRole: tenant.membershipRole,
    }))
    return { scopes, pagination: response.pagination }
  }, [])

  /** Initial fetch: `isBusy` is already true at mount, so no sync setState. */
  const startInitialFetch = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    return fetchCurrent(1)
      .then((result) => {
        if (requestId !== listRequestIdRef.current) return
        setScopes(result.scopes)
        setScopePagination(result.pagination)
        setFailure(null)
      })
      .catch((error) => {
        if (requestId !== listRequestIdRef.current) return
        handleFailure(error)
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [fetchCurrent, handleFailure])

  /** Refresh (event handler): mark busy synchronously, then fetch. */
  const refresh = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    setIsBusy(true)
    setFailure(null)
    return fetchCurrent(1)
      .then((result) => {
        if (requestId !== listRequestIdRef.current) return
        setScopes(result.scopes)
        setScopePagination(result.pagination)
      })
      .catch((error) => {
        if (requestId !== listRequestIdRef.current) return
        handleFailure(error)
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [fetchCurrent, handleFailure])

  const loadMoreScopes = useCallback(() => {
    const nextPage = scopePagination?.hasNextPage ? scopePagination.pageNumber + 1 : null
    if (nextPage === null || isBusy) return
    const requestId = ++listRequestIdRef.current
    setIsBusy(true)
    setFailure(null)
    return fetchCurrent(nextPage)
      .then((result) => {
        if (requestId !== listRequestIdRef.current) return
        setScopes((current) => {
          const byId = new Map((current ?? []).map((scope) => [scope.id, scope]))
          for (const scope of result.scopes) byId.set(scope.id, scope)
          return [...byId.values()]
        })
        setScopePagination(result.pagination)
      })
      .catch((error) => {
        if (requestId !== listRequestIdRef.current) return
        handleFailure(error)
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [fetchCurrent, handleFailure, isBusy, scopePagination])

  useEffect(() => {
    void startInitialFetch()
  }, [startInitialFetch])

  const getScopeById = useCallback(
    (id: string) => scopes?.find((tenant) => tenant.id === id) ?? null,
    [scopes],
  )


  const selectTenant = useCallback(
    (id: string) => {
      navigate(`/t/${encodeURIComponent(id)}`)
    },
    [navigate],
  )

  const selectHome = useCallback(() => {
    navigate('/')
  }, [navigate])

  const value = useMemo<TenantScopeState>(
    () => ({
      isPlatformAdmin,
      scopes,
      scopePagination,
      isBusy,
      failure,
      refresh,
      loadMoreScopes,
      getScopeById,
      selectTenant,
      selectHome,
    }),
    [
      isPlatformAdmin,
      scopes,
      scopePagination,
      isBusy,
      failure,
      refresh,
      loadMoreScopes,
      getScopeById,
      selectTenant,
      selectHome,
    ],
  )

  return <TenantScopeContext.Provider value={value}>{children}</TenantScopeContext.Provider>
}

export function useTenantScope(): TenantScopeState {
  const context = useContext(TenantScopeContext)
  if (context === null) {
    throw new Error('useTenantScope must be used inside <TenantScopeProvider>')
  }
  return context
}
