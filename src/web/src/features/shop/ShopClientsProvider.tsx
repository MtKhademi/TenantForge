import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { mockShopMediaClient } from './clients/mockShopMediaClient'
import type { ShopMediaClient } from './clients/ShopMediaClient'

/**
 * S32 shop clients — the composition seam (F044 mock phase).
 *
 * Shop capabilities expose a stable client interface under `clients/`; this
 * provider binds one implementation per capability and hands the same
 * interface to every consumer. F044 binds `media` to `mockShopMediaClient`;
 * F054 swaps exactly this slot for `httpShopMediaClient` without touching any
 * component (docs/design/shop/frontend-contract-boundary.md).
 *
 * The dev-only scenario switcher is loaded through a *conditional dynamic
 * import* on `import.meta.env.DEV`, so the production build ships a separate,
 * never-executed chunk for it — no mock scenario label can reach production
 * markup.
 */
export type ShopClients = {
  media: ShopMediaClient
}

const ShopClientsContext = createContext<ShopClients | null>(null)

export function ShopClientsProvider({ children }: { children: ReactNode }) {
  // The mock client is a stable module singleton; the binding never changes
  // within a page lifetime (scenario switches re-seed it, they do not
  // re-bind the interface).
  const clients = useMemo<ShopClients>(
    () => ({
      media: mockShopMediaClient,
    }),
    [],
  )

  return (
    <ShopClientsContext.Provider value={clients}>
      {children}
      {import.meta.env.DEV && <DevScenarioToolbar />}
    </ShopClientsContext.Provider>
  )
}

export function useShopClients(): ShopClients {
  const context = useContext(ShopClientsContext)
  if (context === null) {
    throw new Error('useShopClients must be used inside <ShopClientsProvider>')
  }
  return context
}

/** Renders the dev-only switcher once its chunk has loaded (dev builds only). */
function DevScenarioToolbar() {
  const [Switcher, setSwitcher] = useState<(() => ReactNode) | null>(null)

  useEffect(() => {
    let cancelled = false
    void import('./DevMediaScenarioSwitcher').then((module) => {
      if (!cancelled) setSwitcher(() => <module.DevMediaScenarioSwitcher />)
    })
    return () => {
      cancelled = true
    }
  }, [])

  return Switcher ? <>{Switcher()}</> : null
}
