import { createContext, useContext, useEffect, useMemo, useState, type ComponentType, type ReactNode } from 'react'
import { mockShopCartLeaseClient } from './mockShopCartLeaseClient'
import { mockShopCategoryClient } from './mockShopCategoryClient'
import { mockShopCouponClient } from './mockShopCouponClient'
import { mockShopDiscoveryClient } from './mockShopDiscoveryClient'
import { mockShopOrdersClient } from './mockShopOrdersClient'
import { mockShopMediaClient } from './mockShopMediaClient'
import { mockShopProfileClient } from './mockShopProfileClient'
import type { ShopCartLeaseClient } from './ShopCartLeaseClient'
import type { ShopCategoryClient } from './ShopCategoryClient'
import type { ShopCouponClient } from './ShopCouponClient'
import type { ShopDiscoveryClient } from './ShopDiscoveryClient'
import type { ShopOrdersClient } from './ShopOrdersClient'
import type { ShopMediaClient } from './ShopMediaClient'
import type { ShopProfileClient } from './ShopProfileClient'

/**
 * One slot per Shop capability. The slot names below are FIXED — F045..F063
 * refer to them by these exact keys. Each task adds its own slot and leaves
 * every other slot alone.
 */
export type ShopClients = {
  media: ShopMediaClient // F044 mock  -> F054 HTTP
  discovery: ShopDiscoveryClient // F045 mock -> F055 HTTP
  categories: ShopCategoryClient // F046 mock -> F056 HTTP
  profile: ShopProfileClient // F047 mock -> F057 HTTP
  cartLease: ShopCartLeaseClient // F048 mock -> F058 HTTP
  coupons: ShopCouponClient // F049 mock -> F059 HTTP
  orders: ShopOrdersClient // F050 mock -> F060 HTTP
  // F051 adds:  orderOperations: ShopOrderOperationsClient -> F061 HTTP
  // F052 adds:  payments: ShopPaymentsClient          -> F062 HTTP
}

export function createShopClients(): ShopClients {
  return {
    media: mockShopMediaClient,
    discovery: mockShopDiscoveryClient,
    categories: mockShopCategoryClient,
    profile: mockShopProfileClient,
    cartLease: mockShopCartLeaseClient,
    coupons: mockShopCouponClient,
    orders: mockShopOrdersClient,
  }
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

/**
 * Renders the dev-only mock scenario switchers once their chunks have loaded
 * (dev builds only). Each capability's switcher is loaded independently so a
 * single capability's chunk never blocks the others.
 */
function DevScenarioToolbar() {
  const [MediaSwitcher, setMediaSwitcher] = useState<ComponentType | null>(null)
  const [DiscoverySwitcher, setDiscoverySwitcher] = useState<ComponentType | null>(null)
  const [CategorySwitcher, setCategorySwitcher] = useState<ComponentType | null>(null)
  const [ProfileSwitcher, setProfileSwitcher] = useState<ComponentType | null>(null)
  const [CartLeaseSwitcher, setCartLeaseSwitcher] = useState<ComponentType | null>(null)
  const [CouponSwitcher, setCouponSwitcher] = useState<ComponentType | null>(null)
  const [OrdersSwitcher, setOrdersSwitcher] = useState<ComponentType | null>(null)

  useEffect(() => {
    let cancelled = false
    void import('../DevMediaScenarioSwitcher').then((module) => {
      if (!cancelled) setMediaSwitcher(() => module.DevMediaScenarioSwitcher)
    })
    void import('../DevDiscoveryScenarioSwitcher').then((module) => {
      if (!cancelled) setDiscoverySwitcher(() => module.DevDiscoveryScenarioSwitcher)
    })
    void import('../DevCategoryScenarioSwitcher').then((module) => {
      if (!cancelled) setCategorySwitcher(() => module.DevCategoryScenarioSwitcher)
    })
    void import('../DevProfileScenarioSwitcher').then((module) => {
      if (!cancelled) setProfileSwitcher(() => module.DevProfileScenarioSwitcher)
    })
    void import('../DevCartLeaseScenarioSwitcher').then((module) => {
      if (!cancelled) setCartLeaseSwitcher(() => module.DevCartLeaseScenarioSwitcher)
    })
    void import('../DevCouponScenarioSwitcher').then((module) => {
      if (!cancelled) setCouponSwitcher(() => module.DevCouponScenarioSwitcher)
    })
    void import('../DevOrdersScenarioSwitcher').then((module) => {
      if (!cancelled) setOrdersSwitcher(() => module.DevOrdersScenarioSwitcher)
    })
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <>
      {MediaSwitcher ? <MediaSwitcher /> : null}
      {DiscoverySwitcher ? <DiscoverySwitcher /> : null}
      {CategorySwitcher ? <CategorySwitcher /> : null}
      {ProfileSwitcher ? <ProfileSwitcher /> : null}
      {CartLeaseSwitcher ? <CartLeaseSwitcher /> : null}
      {CouponSwitcher ? <CouponSwitcher /> : null}
      {OrdersSwitcher ? <OrdersSwitcher /> : null}
    </>
  )
}
