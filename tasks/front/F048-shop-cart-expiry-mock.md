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
2. Create `src/web/src/features/shop/contracts/cartLeaseContract.ts`. In it, define a Zod schema (a Zod schema is a runtime validator + TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) named `cartResponseSchema` with fields `cartId` (string), `items` (array of `cartItemSchema`), `subTotal` (number), `expiresAtUtc` (string, ISO UTC timestamp). There is no `cartItemSchema` yet — define it in this same file by copying the existing `CartItem` type from `src/web/src/features/shop/cartTypes.ts` field for field into a `z.object({...})`, then re-export `CartItem` as `z.infer<typeof cartItemSchema>` so the app still has exactly one cart-item type.
3. In the same file, define `cartExpiredProblemSchema` by extending `shopProblemSchema` from `src/web/src/features/shop/contracts/shopContract.ts` (the shared RFC7807 shape `F044` created — RFC7807 is the standard JSON error body shape ASP.NET Core returns; reuse it, don't invent a new error format) with `status: z.literal(410)` and `type: z.literal('shop_cart_expired')`.
4. Export `export type CartResponse = z.infer<typeof cartResponseSchema>`.
5. Create `src/web/src/features/shop/clients/ShopCartLeaseClient.ts`. In it define `export interface ShopCartLeaseClient` with a method `getCart(tenantId: string, cartId: string, signal?: AbortSignal): Promise<CartResponse>` (`AbortSignal` makes this call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request). Also give it `addItem`, `updateItem` and `removeItem`, copying their parameter lists verbatim from the functions of the same purpose in `src/web/src/features/shop/cartAdapter.ts` and adding `signal?: AbortSignal` as a final optional parameter. All three return `Promise<CartResponse>`.
6. Go through the full backend Spec `B040` again and add every nested member it names to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
7. Create `src/web/src/features/shop/clients/mockShopCartLeaseClient.ts` (the implementation of `ShopCartLeaseClient` that returns canned data instead of calling the network). Make it deterministic (same input always produces the same output) and route all simulated delays through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`.
8. Give the mock client named scenarios (e.g. "short lease", "expired", "extend after mutation") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
9. Add a `cartLease` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to your new mock cart-lease client. Keep every current cart interaction working exactly as before and do not touch any other slot.
10. In the mock behavior, implement: a short lease (a countdown that expires quickly, e.g. within a demo-friendly window), lease extension after any cart mutation, and a 410 (Gone) "expired" response returned deterministically on cart, checkout, and review pages when the scenario says the lease is up.
11. Make the on-screen countdown purely display-only: it never triggers its own polling and never auto-extends the lease by itself. Only a real mutation (add/update/remove) extends the lease.
12. When expiry occurs, clear only the current tenant's cart ID and order draft from storage. Do not touch any other tenant's stored cart. After clearing, show a way to return to the catalog. Never silently rebuild the cart behind the user's back.
13. Implement every state listed under "Required states" below on the cart, checkout and review pages: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something — the mock must not invent server authority.
14. Update `src/web/src/pages/shop/storefront/CartPage.tsx`, `src/web/src/pages/shop/storefront/CheckoutPage.tsx` and `src/web/src/pages/shop/storefront/OrderReviewPage.tsx` to get the client from `useShopClients()` (never by importing fixtures directly and never by calling `fetch` directly).
15. Update `src/web/src/features/shop/cartStorage.ts` so stored cart IDs are keyed per tenant, and `src/web/src/features/shop/orderDraftState.ts` the same way (already required by rule 12). Clearing one tenant's entry must leave every other tenant's entry untouched.
16. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/cartLeaseContract.ts`
- `src/web/src/features/shop/clients/ShopCartLeaseClient.ts`
- `src/web/src/features/shop/clients/mockShopCartLeaseClient.ts`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `cartLease` slot)
- `src/web/src/features/shop/cartTypes.ts` (re-export `CartItem` from the new schema)
- `src/web/src/features/shop/cartStorage.ts`, `src/web/src/features/shop/orderDraftState.ts`
- `src/web/src/pages/shop/storefront/CartPage.tsx`, `CheckoutPage.tsx`, `OrderReviewPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

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

Build every one of these. "State" means something the user can actually see on
screen, not a code path.

- **idle / initial** — before anything is requested.
- **loading** — visible progress, with no destructive layout shift (the page
  must not jump or reflow when loading finishes).
- **success** — the normal populated result.
- **empty** — a successful response that contains no items. This is not an
  error; it must not look like one.
- **each error this capability actually defines.** For this task those are:
  `410` with `type` exactly `shop_cart_expired` — this is the one that matters for this task and it must trigger the expiry recovery flow on all three pages. Also handle `400` (invalid quantity) and `404` (cart or item not found). A plain `404` and a network failure must **not** be treated as expiry. Each needs its own message — do not collapse them into one generic
  "something went wrong". Do **not** invent a state for a status code not listed
  here.
- **unavailable, with retry** — the request could not be made at all (network
  failure). Show a retry control.
- **aborted / superseded** — when a newer request starts, the older one's
  result must never overwrite the newer one's, and an aborted request must not
  surface as an error to the user.
- **honest success feedback** — never show or imply a confirmation the mock did
  not actually return.

## Browser evidence and validation

Active countdown, extension, expiry on all three pages, local storage isolation, timer cleanup and mobile alert.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock cartLease client; HTTP integration deferred to the matching F054–F063 task.`

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   split into "created" and "edited". Compare it against "Files expected to
   change" above and call out every difference, in either direction.
2. **Implementation decisions.** Every decision this Spec left to you, with the
   option you picked and one sentence of why. Name every place you had to add
   a field, schema or type that the Spec referenced but did not spell out.
3. **Commands executed.** `cd src/web && npm run build` and
   `cd src/web && npm run lint`, copied verbatim, in the order you ran them.
   State explicitly that you did not run `npm test` or `npm run test:e2e`
   (the UI engineer does not touch frontend tests — see `AGENTS.md`).
4. **Results of those checks.** For each command: pass or fail, plus the error
   text if it failed and what you changed to fix it. Then the browser evidence:
   which scenarios you exercised at 1440×900, 1024×768 and 390×844, and whether
   the browser console stayed clean. Never report a check as passing if you did
   not run it.
5. **Contract fidelity.** State that every schema field name, type and
   nullability matches the paired backend Spec, and list any field where you
   were unsure. If the paired Spec and `docs/design/shop/http-contracts.md`
   disagreed, say which one you followed and why.
6. **Risks, blockers and follow-up.** Anything you could not verify, any
   acceptance item you could not check off and why, and anything the paired
   connection task needs to know. Finish with the exact `Data source:` line
   this Spec names. Write "None." for the risk list if there is genuinely
   nothing.

## Definition of done

"Write/verify a scenario" below means: add that scenario to the mock client
and exercise it by hand in a real browser, then record what you saw. It does
**not** mean writing an automated test file — the UI engineer does not create,
edit or run frontend tests (see `AGENTS.md`, "Ownership"). Check a box only
after you have actually seen the described behaviour in the browser.

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B040` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify a scenario with an active cart and a short lease — assert the countdown renders and ticks down.
- [ ] Write/verify that adding, updating and removing a cart item each push `expiresAtUtc` forward — assert the countdown resets after each one.
- [ ] Write/verify that a plain cart GET does **not** move `expiresAtUtc` — assert the countdown keeps running from its old value.
- [ ] Write/verify the 410 `shop_cart_expired` scenario on the cart page — assert the recovery state renders with a way back to the catalog.
- [ ] Write/verify the same 410 scenario on the checkout page — assert the same recovery state renders.
- [ ] Write/verify the same 410 scenario on the order-review page — assert the same recovery state renders.
- [ ] Write/verify that a plain `404` and a simulated network failure do **not** trigger the expiry recovery flow — assert the ordinary error state renders instead.
- [ ] Write/verify that expiry clears only the current tenant's stored cart ID and order draft — store a cart for a second tenant first, then assert that second tenant's entry still exists afterwards.
- [ ] Write/verify that the countdown never polls and never auto-extends the lease on its own — assert no client call fires while it counts down.
- [ ] Write/verify unmounting the page mid-countdown — assert no console error and no leaked timer.
- [ ] Write/verify the expiry alert renders correctly at 390×844.
