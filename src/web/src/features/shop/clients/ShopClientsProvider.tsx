import { createContext, useContext, useEffect, useMemo, useState, type ComponentType, type ReactNode } from 'react'
import { mockShopMediaClient } from './mockShopMediaClient'
import type { ShopMediaClient } from './ShopMediaClient'

/**
 * One slot per Shop capability. The slot names below are FIXED — F045..F063
 * refer to them by these exact keys. Each task adds its own slot and leaves
 * every other slot alone.
 */
export type ShopClients = {
  media: ShopMediaClient // F044 mock  -> F054 HTTP
  // F045 adds:  discovery: ShopDiscoveryClient        -> F055 HTTP
  // F046 adds:  categories: ShopCategoryClient        -> F056 HTTP
  // F047 adds:  profile: ShopProfileClient            -> F057 HTTP
  // F048 adds:  cartLease: ShopCartLeaseClient        -> F058 HTTP
  // F049 adds:  coupons: ShopCouponClient             -> F059 HTTP
  // F050 adds:  orders: ShopOrdersClient              -> F060 HTTP
  // F051 adds:  orderOperations: ShopOrderOperationsClient -> F061 HTTP
  // F052 adds:  payments: ShopPaymentsClient          -> F062 HTTP
}

export function createShopClients(): ShopClients {
  return { media: mockShopMediaClient }
}

const ShopClientsContext = createContext<ShopClients | null>(null)

export function ShopClientsProvider({
  clients,
  children,
}: {
  clients?: ShopClients
  children: ReactNode
}) {
  const value = useMemo(() => clients ?? createShopClients(), [clients])
  return (
    <ShopClientsContext.Provider value={value}>
      {children}
      {import.meta.env.DEV && <DevScenarioToolbar />}
    </ShopClientsContext.Provider>
  )
}

export function useShopClients(): ShopClients {
  const clients = useContext(ShopClientsContext)
  if (!clients) throw new Error('useShopClients must be used inside <ShopClientsProvider>')
  return clients
}

/** Renders the dev-only switcher once its chunk has loaded (dev builds only). */
function DevScenarioToolbar() {
  const [Switcher, setSwitcher] = useState<ComponentType | null>(null)

  useEffect(() => {
    let cancelled = false
    void import('../DevMediaScenarioSwitcher').then((module) => {
      if (!cancelled) setSwitcher(() => module.DevMediaScenarioSwitcher)
    })
    return () => {
      cancelled = true
    }
  }, [])

  return Switcher ? <Switcher /> : null
}
