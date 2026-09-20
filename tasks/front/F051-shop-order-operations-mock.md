# F051 — Build contract-shaped fulfil and cancel mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F051` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S39`; depends on `F050`.
- Planned backend contract: `B043`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews paid-order fulfilment and pending-order cancellation including confirmations and conflict states before mutations exist.

## Do this in order

Before anything else, follow the fixed boilerplate: read `AGENTS.md`, read the `S39` slice file, read the paired backend Spec `B043`, and follow the branch-naming and ledger-update rules already described in the "Ownership, phase and dependencies" section above. Do not skip those.

1. Read the full backend Spec `B043` and the matching section of `docs/design/shop/http-contracts.md`. Write down every field name, type and nullability for the status-change request/response and its error/conflict codes.
2. Create `src/web/src/features/shop/contracts/orderOperationsContract.ts`. In it, define `export const orderStatusActionSchema = z.enum(['Fulfill','Cancel'])` exactly as written.
3. Define `export type ChangeOrderStatusRequest = { action: z.infer<typeof orderStatusActionSchema>; expectedVersion: number }`. `expectedVersion` is used for optimistic concurrency (the server rejects the change if the order's current version does not match, to avoid two conflicting status changes silently overwriting each other — this shows up later as the "stale version" state).
4. Create `src/web/src/features/shop/clients/ShopOrderOperationsClient.ts`. In it define `export interface ShopOrderOperationsClient` with exactly this method: `changeStatus(tenantId: string, orderId: string, body: ChangeOrderStatusRequest, idempotencyKey: string, signal?: AbortSignal): Promise<AdminOrderDetail>`. `idempotencyKey` (a unique string tag on the request) lets a retried request be recognized as "the same attempt" rather than a second mutation — this is what makes a retried click safe (idempotent means retrying doesn't cause a duplicate effect). `AbortSignal` makes the call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request. Reuse the existing `AdminOrderDetail` type from `F050`'s `adminOrdersContract.ts` as the return type; do not redefine it here.
5. Define `export type PendingOrderAction = { idempotencyKey: string; orderId: string; body: ChangeOrderStatusRequest }`. This type represents an action the UI has fired but not yet confirmed as succeeded (used for the retry/duplicate-click handling in step 10).
6. Go through the full backend Spec `B043` again and add every nested member it names (including every conflict/invalid-transition error code) to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
7. Add an `orderOperations` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, and back it with a new `src/web/src/features/shop/clients/mockShopOrderOperationsClient.ts`. Have that mock read and write the **same** in-memory order fixtures `F050`'s `mockShopOrdersClient.ts` uses — export those fixtures from `mockShopOrdersClient.ts` and import them, so a successful status change is visible the next time `orders.get(...)` is called. Do not create a second, separate set of order data, and do not change the `orders` slot.
8. In the mock implementation, make these exact transitions succeed deterministically: `Paid` -> `Fulfilled` (the "Fulfill" action), and `PendingPayment` -> `Cancelled` (the "Cancel" action).
9. In the mock implementation, make these fail deterministically under the right named scenario: a stale-version conflict (409, when `expectedVersion` doesn't match), and an invalid transition (e.g. trying to fulfil an already-cancelled order).
10. Implement retry handling: if the same `idempotencyKey` is reused for a request whose `action` is unchanged, treat it as a retry of the same attempt (safe to repeat) rather than as a brand-new mutation. Do not reuse an idempotency key across two different actions.
11. In `src/web/src/pages/shop/admin/OrderDetailPage.tsx` (created by `F050`), add a confirmation step before firing "Fulfill" or "Cancel" that explicitly explains the action is irreversible.
12. When the current user holds `SHOP_ORDERS_VIEW_KEY` (`'Shop.Orders.View'`, added in `F050`) but not `SHOP_ORDERS_MANAGE_KEY` (`'Shop.Orders.Manage'`), hide the fulfil/cancel controls entirely — do not just disable them.
13. Never optimistic-update the order's status in the UI: only show the new status after the mock client's response actually comes back, never immediately on click.
14. Handle a duplicate click (the user clicking "Fulfill" or "Cancel" twice quickly) so it does not fire two separate mutations — this is what the idempotency key from steps 4/10 is for.
15. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
16. Gate mock scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI. Route all simulated delays through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`, and keep the mock deterministic (same input always produces the same output).
17. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/orderOperationsContract.ts`
- `src/web/src/features/shop/clients/ShopOrderOperationsClient.ts` (interface only)
- `src/web/src/features/shop/clients/mockShopOrderOperationsClient.ts`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `orderOperations` slot)
- `src/web/src/features/shop/clients/mockShopOrdersClient.ts` (export its fixtures so the operations mock shares them)
- `src/web/src/pages/shop/admin/OrderDetailPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

JSON member casing and nullability mirror `B043` exactly. Mock IDs are canonical 13-character TSID strings (TSID: a sortable numeric string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const orderStatusActionSchema = z.enum(['Fulfill','Cancel'])
export type ChangeOrderStatusRequest = { action: z.infer<typeof orderStatusActionSchema>; expectedVersion: number }
export interface ShopOrderOperationsClient { changeStatus(tenantId: string, orderId: string, body: ChangeOrderStatusRequest, idempotencyKey: string, signal?: AbortSignal): Promise<AdminOrderDetail> }
export type PendingOrderAction = { idempotencyKey: string; orderId: string; body: ChangeOrderStatusRequest }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

Concretely: this means (see step 6 above) adding every conflict/invalid-transition error code and every other field `B043` defines, until nothing from that backend Spec is missing from `orderOperationsContract.ts`.

## Required UI implementation

Extend the existing order client slot rather than adding a second page data source. Mock Paid -> Fulfilled, PendingPayment -> Cancelled, stale version, invalid transition, retry and View-only. Confirmation explains irreversible effect. Reuse UUID only for retry of the unchanged action. Never optimistic-update status.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 7–16 for the mechanical breakdown of this section.)

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
  `403` (the user lacks `Shop.Orders.Manage`), `404` (order not found), and `409` for two distinct reasons that need two distinct messages: a stale `expectedVersion`, and `invalid_order_transition` (e.g. fulfilling an already-cancelled order). Each needs its own message — do not collapse them into one generic
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

Fulfil/cancel success, View-only hidden controls, duplicate click, simulated network retry and 409 refresh.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock orderOperations client; HTTP integration deferred to the matching F054–F063 task.`

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
- [ ] Contract schemas/types match `B043` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify fulfilling a `Paid` order — assert the status becomes `Fulfilled` only after the mock responds, never on click.
- [ ] Write/verify cancelling a `PendingPayment` order — assert the status becomes `Cancelled` only after the mock responds.
- [ ] Write/verify the confirmation step for both actions — assert it states the action is irreversible and that cancelling the dialog fires no request.
- [ ] Write/verify an invalid transition (e.g. fulfilling a cancelled order) — assert the `invalid_order_transition` message renders, distinct from the stale-version message.
- [ ] Write/verify a stale `expectedVersion` — assert the conflict message renders and tells the user to refresh.
- [ ] Write/verify a user with `Shop.Orders.View` but not `Shop.Orders.Manage` — assert the fulfil/cancel controls are absent from the DOM, not merely disabled.
- [ ] Write/verify double-clicking fulfil or cancel quickly — assert only one mutation fires.
- [ ] Write/verify retrying a failed request — assert the same idempotency key is reused for the unchanged action, and that a different action gets a new key.
- [ ] Write/verify that after a successful action the order detail reflects the new status when re-read from the `orders` slot.
- [ ] Write/verify the confirmation dialog is usable by keyboard alone and renders correctly at 390×844.
