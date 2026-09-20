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
2. Create (or open) `features/shop/contracts/orderOperationsContract.ts`. In it, define `export const orderStatusActionSchema = z.enum(['Fulfill','Cancel'])` exactly as written.
3. Define `export type ChangeOrderStatusRequest = { action: z.infer<typeof orderStatusActionSchema>; expectedVersion: number }`. `expectedVersion` is used for optimistic concurrency (the server rejects the change if the order's current version does not match, to avoid two conflicting status changes silently overwriting each other — this shows up later as the "stale version" state).
4. In the same file, define `export interface ShopOrderOperationsClient` with exactly this method: `changeStatus(tenantId: string, orderId: string, body: ChangeOrderStatusRequest, idempotencyKey: string, signal?: AbortSignal): Promise<AdminOrderDetail>`. `idempotencyKey` (a unique string tag on the request) lets a retried request be recognized as "the same attempt" rather than a second mutation — this is what makes a retried click safe (idempotent means retrying doesn't cause a duplicate effect). `AbortSignal` makes the call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request. Reuse the existing `AdminOrderDetail` type from `F050`'s `adminOrdersContract.ts` as the return type; do not redefine it here.
5. Define `export type PendingOrderAction = { idempotencyKey: string; orderId: string; body: ChangeOrderStatusRequest }`. This type represents an action the UI has fired but not yet confirmed as succeeded (used for the retry/duplicate-click handling in step 10).
6. Go through the full backend Spec `B043` again and add every nested member it names (including every conflict/invalid-transition error code) to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
7. Extend the existing order client that `F050` created (`clients/ShopOrdersClient.ts` and its mock) to also implement `ShopOrderOperationsClient`. Do not create a second, separate data source for orders — OrderDetailPage must keep reading order data from the same client slot as before.
8. In the mock implementation, make these exact transitions succeed deterministically: `Paid` -> `Fulfilled` (the "Fulfill" action), and `PendingPayment` -> `Cancelled` (the "Cancel" action).
9. In the mock implementation, make these fail deterministically under the right named scenario: a stale-version conflict (409, when `expectedVersion` doesn't match), and an invalid transition (e.g. trying to fulfil an already-cancelled order).
10. Implement retry handling: if the same `idempotencyKey` is reused for a request whose `action` is unchanged, treat it as a retry of the same attempt (safe to repeat) rather than as a brand-new mutation. Do not reuse an idempotency key across two different actions.
11. In OrderDetailPage, add a confirmation step before firing "Fulfill" or "Cancel" that explicitly explains the action is irreversible.
12. When the current user only has "View" permission (added in `F050`) and not "Manage", hide the fulfil/cancel controls entirely — do not just disable them.
13. Never optimistic-update the order's status in the UI: only show the new status after the mock client's response actually comes back, never immediately on click.
14. Handle a duplicate click (the user clicking "Fulfill" or "Cancel" twice quickly) so it does not fire two separate mutations — this is what the idempotency key from steps 4/10 is for.
15. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
16. Gate mock scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI. Route all simulated delays through the existing abort-aware latency helper already used by other mock clients in this codebase, and keep the mock deterministic (same input always produces the same output).
17. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

`contracts/orderOperationsContract.ts`, order client extension/mock implementation, OrderDetailPage and confirmation UI.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

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

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Fulfil/cancel success, View-only hidden controls, duplicate click, simulated network retry and 409 refresh.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock orderOperations client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B043` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
