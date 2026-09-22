import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiUnavailableError } from '@/features/auth/authTypes'
import { clearCartId, getOrCreateCartId } from './cartStorage'
import { clearOrderDraft } from './orderDraftState'
import type { ShopCartLeaseClient } from './clients/ShopCartLeaseClient'
import type { CartResponse } from './contracts/cartLeaseContract'
import { CartLeaseExpired } from './contracts/cartLeaseContract'
import { ShopClientError } from './contracts/shopContract'

/**
 * S36 cart reservation lease — the one shared hook behind the cart, checkout
 * and order-review pages (F048). It owns the cart read lifecycle that all
 * three surfaces share, so they agree on exactly one recovery behavior:
 *
 * - loads the tenant's stored cart through the `cartLease` client, aborting
 *   any superseded request (an aborted read never surfaces as an error and
 *   never overwrites a newer one);
 * - on the 410 `shop_cart_expired` problem (thrown as `CartLeaseExpired`) it
 *   clears ONLY this tenant's cart id and order draft — other tenants' stored
 *   carts are untouched — and exposes the `expired` state;
 * - a plain `404` (unknown cart/item) is the `notFound` state and a network
 *   failure is `unavailable`; neither is an expiry; a 200 with zero items is
 *   the `empty` state (a successful response, not an error);
 * - `applyMutation` folds a mutation response back into the read state (the
 *   response carries the fresh `expiresAtUtc` — B040: only a real add/update/
 *   remove ever extends the lease, so the countdown resets from it);
 * - `markLocalExpiry` is wired to the display-only countdown's `onExpiry` so
 *   the recovery state appears promptly when the local clock passes the lease
 *   end even with no request in flight. It is a no-op while a mutation is in
 *   flight or if the lease is actually still live. The countdown itself never
 *   polls and never auto-extends.
 *
 * Mock-phase note: with no stored cart id the hook mints one via
 * `getOrCreateCartId` so the deterministic mock has a cart to key off and the
 * countdown is demonstrable. The real B028 flow creates the cart on
 * add-to-cart; F058 restores that exact behavior when it binds HTTP.
 */
export type CartLeaseState =
  | { kind: 'loading' }
  | { kind: 'ok'; cart: CartResponse }
  | { kind: 'empty' }
  | { kind: 'expired' }
  | { kind: 'notFound'; message: string }
  | { kind: 'unavailable' }

function toState(cart: CartResponse): CartLeaseState {
  return cart.items.length === 0 ? { kind: 'empty' } : { kind: 'ok', cart }
}

export function useCartLease(tenantId: string, cartLease: ShopCartLeaseClient) {
  const [state, setState] = useState<CartLeaseState>({ kind: 'loading' })
  const controllerRef = useRef<AbortController | null>(null)
  const busyRef = useRef(false)

  const clearTenantCart = useCallback(() => {
    clearCartId(tenantId)
    clearOrderDraft(tenantId)
  }, [tenantId])

  const classifyAndSet = useCallback(
    (error: unknown) => {
      if (error instanceof CartLeaseExpired) {
        clearTenantCart()
        setState({ kind: 'expired' })
        return
      }
      if (error instanceof ShopClientError && error.problem.status === 404) {
        setState({
          kind: 'notFound',
          message:
            error.problem.detail ?? error.problem.title ?? 'سبد خرید یا آیتم یافت نشد.',
        })
        return
      }
      if (error instanceof ApiUnavailableError) {
        setState({ kind: 'unavailable' })
        return
      }
      // Any other unrecognized failure degrades to the unavailable state so
      // the page offers a retry rather than blocking; it is never an expiry.
      setState({ kind: 'unavailable' })
    },
    [clearTenantCart],
  )

  const load = useCallback(() => {
    controllerRef.current?.abort()
    const controller = new AbortController()
    controllerRef.current = controller
    const cartId = getOrCreateCartId(tenantId)
    setState({ kind: 'loading' })
    cartLease
      .getCart(tenantId, cartId, controller.signal)
      .then((cart) => {
        if (controller.signal.aborted) return
        setState(toState(cart))
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        classifyAndSet(error)
      })
  }, [tenantId, cartLease, classifyAndSet])

  useEffect(() => {
    load()
    return () => {
      // Aborting on unmount: the in-flight read is discarded and its result
      // must never surface as an error or overwrite a newer page's state.
      controllerRef.current?.abort()
    }
  }, [load])

  /**
   * Folds a successful mutation response back into the read state. The
   * response is authoritative: it carries the items, subtotal and the fresh
   * `expiresAtUtc` the mutation just extended the lease to.
   */
  const applyMutation = useCallback((cart: CartResponse) => {
    setState(toState(cart))
  }, [])

  // Called by the display-only countdown when the local clock reaches the
  // lease end. Flips to recovery only while a live cart is on screen and no
  // mutation is in flight; re-checks the latest lease end to avoid a stale
  // flip (a just-landed mutation would have pushed it into the future).
  const markLocalExpiry = useCallback(() => {
    setState((current) => {
      if (current.kind !== 'ok') return current
      if (busyRef.current) return current
      if (Date.parse(current.cart.expiresAtUtc) > Date.now()) return current
      clearCartId(tenantId)
      clearOrderDraft(tenantId)
      return { kind: 'expired' }
    })
  }, [tenantId])

  /**
   * Unconditional recovery flip for when the SERVER authoritatively answers a
   * 410 `shop_cart_expired` on a mutation (e.g. order creation): clear this
   * tenant's cart id and order draft and show the recovery state. No local
   * re-check — the server has already said the lease is gone.
   */
  const forceExpired = useCallback(() => {
    clearCartId(tenantId)
    clearOrderDraft(tenantId)
    setState({ kind: 'expired' })
  }, [tenantId])

  const setBusy = useCallback((busy: boolean) => {
    busyRef.current = busy
  }, [])

  return {
    state,
    reload: load,
    applyMutation,
    clearTenantCart,
    markLocalExpiry,
    forceExpired,
    setBusy,
    expiresAtUtc: state.kind === 'ok' ? state.cart.expiresAtUtc : null,
  }
}
