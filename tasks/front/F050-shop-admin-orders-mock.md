# F050 — Build contract-shaped admin order list and detail mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F050` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S38`; depends on `F049`.
- Planned backend contract: `B042`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews permission-aware order filters, list, detail, snapshots and payment history before admin order APIs exist.

## Files expected to change

`contracts/adminOrdersContract.ts`, `clients/ShopOrdersClient.ts`, mock client, provider extension, OrdersPage, OrderDetailPage, routes and nav.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B042` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const adminOrderStatusSchema = z.enum(['PendingPayment','Paid','Cancelled','Fulfilled'])
export const adminOrderSummarySchema = z.object({ id: z.string(), orderNumber: z.string(), customerName: z.string(), customerPhone: z.string(), status: adminOrderStatusSchema, grandTotal: z.number(), createdAtUtc: z.string() })
export const adminOrderDetailSchema = z.object({ id: z.string(), orderNumber: z.string(), trackingCode: z.string(), status: adminOrderStatusSchema, customer: adminOrderCustomerSchema, totals: adminOrderTotalsSchema, items: z.array(adminOrderItemSchema), paymentAttempts: z.array(adminPaymentAttemptSchema), version: z.number().int(), createdAtUtc: z.string() })
export interface ShopOrdersClient { list(tenantId: string, filters: AdminOrderFilters, signal?: AbortSignal): Promise<AdminOrderListResponse>; get(tenantId: string, orderId: string, signal?: AbortSignal): Promise<AdminOrderDetail> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend provider with orders. Add View/Manage permission constants. Mock server-like URL filters and pagination. Desktop table/mobile cards; detail uses snapshots, customer/address/totals and bounded payment history. Include loading, empty, 400, 403, 404 and unavailable. No action controls yet.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Every filter and URL history; list/detail; View denied; foreign-like 404; snapshot content; responsive evidence.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock orders client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B042` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
