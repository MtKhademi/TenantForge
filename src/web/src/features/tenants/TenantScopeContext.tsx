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
import { mockTenantAdapter, TenantForbiddenError } from './tenantAdapter'
import type { TenantSummary } from './tenantTypes'

/**
 * S07 tenant scope — stable client context (agreed in F010, kept by F011/B007).
 *
 * This context owns the **shared tenant list** so the switcher (in the shell
 * header) and the tenants page both read the same in-flight/loaded data.
 *
 * It represents **selection**, not authorization: `selectTenant`/
 * `selectPlatform` only navigate. The **URL (`/t/:slug`) is the single source
 * of truth** for which tenant is active — consumers derive it with
 * `useParams`, so there is no second "active tenant" state to drift. Whether
 * the signed-in user may actually work inside a tenant is always decided
 * server-side (S08); nothing here grants or hides privileged behavior.
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
  /** Look up a tenant by its URL slug; `null` when unknown. */
  getTenantBySlug(slug: string): TenantSummary | null
  /** Navigate into a tenant's scoped shell (selection only). */
  selectTenant(slug: string): void
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
    return mockTenantAdapter
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
    mockTenantAdapter
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

  const getTenantBySlug = useCallback(
    (slug: string) => tenants?.find((tenant) => tenant.slug === slug) ?? null,
    [tenants],
  )

  const selectTenant = useCallback(
    (slug: string) => {
      navigate(`/t/${encodeURIComponent(slug)}`)
    },
    [navigate],
  )

  const selectPlatform = useCallback(() => {
    navigate('/platform/tenants')
  }, [navigate])

  const value = useMemo<TenantScopeState>(
    () => ({ tenants, isBusy, failure, refresh, getTenantBySlug, selectTenant, selectPlatform }),
    [tenants, isBusy, failure, refresh, getTenantBySlug, selectTenant, selectPlatform],
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
