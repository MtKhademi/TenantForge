# F048 — Build contract-shaped cart expiry mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F048` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S36`; depends on `F047`.
- Planned backend contract: `B040`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user sees reservation countdown and expired-cart recovery in cart, checkout and review without waiting for backend expiry support.

## Do this in order

Before anything else, follow the fixed boilerplate: read `AGENTS.md`, read the `S36` slice file, read the paired backend Spec `B040`, and follow the branch-naming and ledger-update rules already described in the "Ownership, phase and dependencies" section above. Do not skip those.

1. Read the full backend Spec `B040` and the matching section of `docs/design/shop/http-contracts.md`. Write down every field name, type and nullability for the cart response and the expired-cart error.
2. Create (or open) `features/shop/contracts/cartLeaseContract.ts`. In it, define a Zod schema (a Zod schema is a runtime validator + TypeScript type generator already used elsewhere in `features/shop/contracts/`) named `cartResponseSchema` with fields `cartId` (string), `items` (array of the existing `cartItemSchema`), `subTotal` (number), `expiresAtUtc` (string, ISO UTC timestamp).
3. In the same file, define `cartExpiredProblemSchema` by extending the existing `shopProblemSchema` (the repo's shared RFC7807 shape — RFC7807 is the repo's standard JSON error body shape; reuse the existing helper, don't invent a new error format) with `status: z.literal(410)` and `type: z.literal('shop_cart_expired')`.
4. Export `export type CartResponse = z.infer<typeof cartResponseSchema>`.
5. In the same file, define `export interface ShopCartLeaseClient` with a method `getCart(tenantId: string, cartId: string, signal?: AbortSignal): Promise<CartResponse>` (`AbortSignal` makes this call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request). Keep the existing add/update/remove method signatures on this interface exactly as they already are, but change their return type to `Promise<CartResponse>`.
6. Go through the full backend Spec `B040` again and add every nested member it names to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
7. Create the mock cart client (the implementation of `ShopCartLeaseClient` that returns canned data instead of calling the network). Make it deterministic (same input always produces the same output) and route all simulated delays through an existing abort-aware latency helper already used by other mock clients in this codebase.
8. Give the mock client named scenarios (e.g. "short lease", "expired", "extend after mutation") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
9. Register the new cart-lease client on `ShopClientsProvider` (the existing shared provider component), keeping every current cart interaction working exactly as before.
10. In the mock behavior, implement: a short lease (a countdown that expires quickly, e.g. within a demo-friendly window), lease extension after any cart mutation, and a 410 (Gone) "expired" response returned deterministically on cart, checkout, and review pages when the scenario says the lease is up.
11. Make the on-screen countdown purely display-only: it never triggers its own polling and never auto-extends the lease by itself. Only a real mutation (add/update/remove) extends the lease.
12. When expiry occurs, clear only the current tenant's cart ID and order draft from storage. Do not touch any other tenant's stored cart. After clearing, show a way to return to the catalog. Never silently rebuild the cart behind the user's back.
13. Implement every state listed under "Required states" below on the cart, checkout and review pages: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something — the mock must not invent server authority.
14. Update CartPage, CheckoutPage and OrderReviewPage to consume the new client through the provider (never by importing fixtures directly and never by calling `fetch` directly).
15. Update cart storage code so it isolates per-tenant cart IDs (already required by rule 12).
16. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

`contracts/cartLeaseContract.ts`, mock cart client extension, CartPage, CheckoutPage, OrderReviewPage and cart storage.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B040` exactly. Mock IDs are canonical 13-character TSID strings (TSID: a sortable numeric string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const cartResponseSchema = z.object({ cartId: z.string(), items: z.array(cartItemSchema), subTotal: z.number(), expiresAtUtc: z.string() })
export const cartExpiredProblemSchema = shopProblemSchema.extend({ status: z.literal(410), type: z.literal('shop_cart_expired') })
export type CartResponse = z.infer<typeof cartResponseSchema>
export interface ShopCartLeaseClient { getCart(tenantId: string, cartId: string, signal?: AbortSignal): Promise<CartResponse>; /* existing add/update/remove signatures keep their request shapes and now return CartResponse */ }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

Concretely: this means (see step 6 above) adding every field `B040` defines for the cart, its items, and the expired-cart error, until nothing from that backend Spec is missing from `cartLeaseContract.ts`.

## Required UI implementation

Extend provider with the cart lease client while preserving current cart interactions. Mock a short lease, extension after mutation and 410 on cart/checkout/review. Countdown is display-only and never polls or auto-extends. Expiry clears tenant cart ID and order draft, keeps other tenants intact, and offers return to catalog; never silently rebuild the cart.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 7–15 for the mechanical breakdown of this section.)

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
