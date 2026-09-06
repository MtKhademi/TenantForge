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
import { httpTenantAdapter, TenantForbiddenError } from './tenantAdapter'
import type { TenantSummary } from './tenantTypes'

/**
 * S07 tenant scope — stable client context (agreed in F010, kept by F011/B007).
 *
 * This context owns the **shared tenant list** so the switcher (in the shell
 * header) and the tenants page both read the same in-flight/loaded data.
 *
 * It represents **selection**, not authorization: `selectTenant`/
 * `selectPlatform` only navigate. The **URL (`/t/:tenantId`) is the single
 * source of truth** for which tenant is active — consumers derive it with
 * `useParams`, so there is no second "active tenant" state to drift. Whether
 * the signed-in user may actually work inside a tenant is always decided
 * server-side (S08); nothing here grants or hides privileged behavior.
 *
 * The route carries the tenant **id** (not the slug) because S08's
 * `GET /api/tenants/{tenantId}/members` is keyed by id, and non-admin members
 * cannot call the platform tenant list to resolve a slug. The switcher and
 * the tenants page navigate by id; lookups against the loaded list are by id.
 */
export type TenantScopeState = {
  /** `null` until the first list fetch settles. */
  tenants: TenantSummary[] | null
  /** True while any list fetch is in flight (initial or refresh). */
  isBusy: boolean
  /** Set only when a fetch failed and no previous data is on screen. */
  failure: 'unavailable' | 'forbidden' | null
  /** Re-fetch (e.g. after creating a tenant). Superseding is safe. */
  refresh(): void
  /** Look up a tenant by its id; `null` when unknown. */
  getTenantById(id: string): TenantSummary | null
  /** Navigate into a tenant's scoped shell (selection only). */
  selectTenant(id: string): void
  /** Navigate back to the platform tenants page. */
  selectPlatform(): void
}

const TenantScopeContext = createContext<TenantScopeState | null>(null)

export function TenantScopeProvider({ children }: { children: ReactNode }) {
  const { session, signOut } = useAuth()
  const navigate = useNavigate()
  const [tenants, setTenants] = useState<TenantSummary[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [failure, setFailure] = useState<'unavailable' | 'forbidden' | null>(null)

  const listRequestIdRef = useRef(0)
  const sessionRef = useRef(session)
  const signOutRef = useRef(signOut)

  useEffect(() => {
    sessionRef.current = session
  }, [session])
  useEffect(() => {
    signOutRef.current = signOut
  }, [signOut])

  const handleFailure = useCallback((error: unknown) => {
    if (error instanceof SessionExpiredError) {
      void signOutRef.current()
      return
    }
    setFailure(error instanceof TenantForbiddenError ? 'forbidden' : 'unavailable')
  }, [])

  /** Initial fetch: `isBusy` is already true at mount, so no sync setState. */
  const startInitialFetch = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    return httpTenantAdapter
      .listTenants(sessionRef.current?.accessToken ?? '')
      .then((response) => {
        if (requestId !== listRequestIdRef.current) return
        setTenants(response.tenants)
        setFailure(null)
      })
      .catch((error) => {
        if (requestId !== listRequestIdRef.current) return
        handleFailure(error)
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [handleFailure])

  /** Refresh (event handler): mark busy synchronously, then fetch. */
  const refresh = useCallback(() => {
    const requestId = ++listRequestIdRef.current
    setIsBusy(true)
    setFailure(null)
    httpTenantAdapter
      .listTenants(sessionRef.current?.accessToken ?? '')
      .then((response) => {
        if (requestId !== listRequestIdRef.current) return
        setTenants(response.tenants)
      })
      .catch((error) => {
        if (requestId !== listRequestIdRef.current) return
        handleFailure(error)
      })
      .finally(() => {
        if (requestId === listRequestIdRef.current) setIsBusy(false)
      })
  }, [handleFailure])

  useEffect(() => {
    void startInitialFetch()
  }, [startInitialFetch])

  const getTenantById = useCallback(
    (id: string) => tenants?.find((tenant) => tenant.id === id) ?? null,
    [tenants],
  )

  const selectTenant = useCallback(
    (id: string) => {
      navigate(`/t/${encodeURIComponent(id)}`)
    },
    [navigate],
  )

  const selectPlatform = useCallback(() => {
    navigate('/platform/tenants')
  }, [navigate])

  const value = useMemo<TenantScopeState>(
    () => ({ tenants, isBusy, failure, refresh, getTenantById, selectTenant, selectPlatform }),
    [tenants, isBusy, failure, refresh, getTenantById, selectTenant, selectPlatform],
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
