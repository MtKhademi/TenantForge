# F048 — Build contract-shaped cart expiry mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F048` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S36`; depends on `F047`.
- Planned backend contract: `B040`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user sees reservation countdown and expired-cart recovery in cart, checkout and review without waiting for backend expiry support.

## Files expected to change

`contracts/cartLeaseContract.ts`, mock cart client extension, CartPage, CheckoutPage, OrderReviewPage and cart storage.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B040` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const cartResponseSchema = z.object({ cartId: z.string(), items: z.array(cartItemSchema), subTotal: z.number(), expiresAtUtc: z.string() })
export const cartExpiredProblemSchema = shopProblemSchema.extend({ status: z.literal(410), type: z.literal('shop_cart_expired') })
export type CartResponse = z.infer<typeof cartResponseSchema>
export interface ShopCartLeaseClient { getCart(tenantId: string, cartId: string, signal?: AbortSignal): Promise<CartResponse>; /* existing add/update/remove signatures keep their request shapes and now return CartResponse */ }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend provider with the cart lease client while preserving current cart interactions. Mock a short lease, extension after mutation and 410 on cart/checkout/review. Countdown is display-only and never polls or auto-extends. Expiry clears tenant cart ID and order draft, keeps other tenants intact, and offers return to catalog; never silently rebuild the cart.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Active countdown, extension, expiry on all three pages, local storage isolation, timer cleanup and mobile alert.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock cartLease client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B040` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
