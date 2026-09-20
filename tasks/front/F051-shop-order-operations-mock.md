# F051 — Build contract-shaped fulfil and cancel mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F051` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S39`; depends on `F050`.
- Planned backend contract: `B043`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews paid-order fulfilment and pending-order cancellation including confirmations and conflict states before mutations exist.

## Files expected to change

`contracts/orderOperationsContract.ts`, order client extension/mock implementation, OrderDetailPage and confirmation UI.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B043` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const orderStatusActionSchema = z.enum(['Fulfill','Cancel'])
export type ChangeOrderStatusRequest = { action: z.infer<typeof orderStatusActionSchema>; expectedVersion: number }
export interface ShopOrderOperationsClient { changeStatus(tenantId: string, orderId: string, body: ChangeOrderStatusRequest, idempotencyKey: string, signal?: AbortSignal): Promise<AdminOrderDetail> }
export type PendingOrderAction = { idempotencyKey: string; orderId: string; body: ChangeOrderStatusRequest }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend the existing order client slot rather than adding a second page data source. Mock Paid -> Fulfilled, PendingPayment -> Cancelled, stale version, invalid transition, retry and View-only. Confirmation explains irreversible effect. Reuse UUID only for retry of the unchanged action. Never optimistic-update status.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

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
