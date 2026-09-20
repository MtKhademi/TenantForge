# F050 — Build contract-shaped admin order list and detail mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F050` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S38`; depends on `F049`.
- Planned backend contract: `B042`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews permission-aware order filters, list, detail, snapshots and payment history before admin order APIs exist.

## Do this in order

Before anything else, follow the fixed boilerplate: read `AGENTS.md`, read the `S38` slice file, read the paired backend Spec `B042`, and follow the branch-naming and ledger-update rules already described in the "Ownership, phase and dependencies" section above. Do not skip those.

1. Read the full backend Spec `B042` and the matching section of `docs/design/shop/http-contracts.md`. Write down every field name, type and nullability for the order summary, order detail, filters and payment attempt shapes.
2. Create (or open) `features/shop/contracts/adminOrdersContract.ts`. In it, define `export const adminOrderStatusSchema = z.enum(['PendingPayment','Paid','Cancelled','Fulfilled'])` exactly as written.
3. Define `adminOrderSummarySchema` as a Zod object (a Zod schema is a runtime validator + TypeScript type generator already used elsewhere in `features/shop/contracts/`) with fields: `id` (string), `orderNumber` (string), `customerName` (string), `customerPhone` (string), `status` (`adminOrderStatusSchema`), `grandTotal` (number), `createdAtUtc` (string).
4. Define `adminOrderDetailSchema` as a Zod object with fields: `id` (string), `orderNumber` (string), `trackingCode` (string), `status` (`adminOrderStatusSchema`), `customer` (`adminOrderCustomerSchema` — define this nested schema yourself using every customer field `B042` names), `totals` (`adminOrderTotalsSchema` — define this nested schema yourself using every totals field `B042` names), `items` (array of `adminOrderItemSchema` — define this nested schema yourself using every item field `B042` names, remembering these are point-in-time snapshots, not live product data), `paymentAttempts` (array of `adminPaymentAttemptSchema` — define this nested schema yourself using every payment-attempt field `B042` names), `version` (integer), `createdAtUtc` (string).
5. Export the inferred TypeScript types for every schema above (e.g. `export type AdminOrderSummary = z.infer<typeof adminOrderSummarySchema>`, and likewise for detail, list-response and filters types named by `B042`).
6. In the same file, define `export interface ShopOrdersClient` with these exact methods:
   - `list(tenantId: string, filters: AdminOrderFilters, signal?: AbortSignal): Promise<AdminOrderListResponse>`
   - `get(tenantId: string, orderId: string, signal?: AbortSignal): Promise<AdminOrderDetail>`
   (`AbortSignal` makes each call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request.) Define `AdminOrderFilters` and `AdminOrderListResponse` yourself with every field `B042` names for filtering (status, date range, search, pagination, etc.) and for the paginated list response.
7. Go through the full backend Spec `B042` again and add every nested member it names to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
8. Create the file `clients/ShopOrdersClient.ts` implementing the mock version of `ShopOrdersClient`. Make it deterministic (same input always produces the same output) and route all simulated delays through an existing abort-aware latency helper already used by other mock clients in this codebase.
9. Give the mock client named scenarios (e.g. "list with filters", "detail with payment history", "foreign-tenant 404") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
10. Register the new orders client on `ShopClientsProvider` (the existing shared provider component).
11. Add two new permission constants: one named for "View" access to orders and one named for "Manage" access to orders (follow this codebase's existing naming convention for permission constants). These gate what a user can see; enforcing them for real is a backend concern, but the mock UI must still branch on them.
12. In OrdersPage, implement filtering and pagination that behaves like a real server: reflect the current filters in the URL (so back/forward and reload keep the same filtered view), and read initial filters from the URL on load.
13. On desktop, render the order list as a table. On mobile, render it as cards instead. Use the same underlying data for both.
14. In OrderDetailPage, show the order using its stored snapshot fields (not live/current product or customer data), plus the customer info, address, totals, and a bounded (capped, not infinitely long) payment attempt history list.
15. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/400/403/404/410/429 states this capability names (this task specifically needs 400, 403, 404 and unavailable, per "Required UI implementation" below), an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
16. Add no action controls (fulfil/cancel/etc.) to OrderDetailPage in this task — that is out of scope here and belongs to `F051`.
17. Wire new routes for OrdersPage and OrderDetailPage, and add the corresponding nav entry, gated by the "View" permission constant from step 11: when the current user lacks "View", the mock must show a permission-denied state instead of the page content.
18. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

`contracts/adminOrdersContract.ts`, `clients/ShopOrdersClient.ts`, mock client, provider extension, OrdersPage, OrderDetailPage, routes and nav.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B042` exactly. Mock IDs are canonical 13-character TSID strings (TSID: a sortable numeric string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const adminOrderStatusSchema = z.enum(['PendingPayment','Paid','Cancelled','Fulfilled'])
export const adminOrderSummarySchema = z.object({ id: z.string(), orderNumber: z.string(), customerName: z.string(), customerPhone: z.string(), status: adminOrderStatusSchema, grandTotal: z.number(), createdAtUtc: z.string() })
export const adminOrderDetailSchema = z.object({ id: z.string(), orderNumber: z.string(), trackingCode: z.string(), status: adminOrderStatusSchema, customer: adminOrderCustomerSchema, totals: adminOrderTotalsSchema, items: z.array(adminOrderItemSchema), paymentAttempts: z.array(adminPaymentAttemptSchema), version: z.number().int(), createdAtUtc: z.string() })
export interface ShopOrdersClient { list(tenantId: string, filters: AdminOrderFilters, signal?: AbortSignal): Promise<AdminOrderListResponse>; get(tenantId: string, orderId: string, signal?: AbortSignal): Promise<AdminOrderDetail> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

Concretely: this means (see steps 3–7 above) fully defining `adminOrderCustomerSchema`, `adminOrderTotalsSchema`, `adminOrderItemSchema`, `adminPaymentAttemptSchema`, `AdminOrderFilters` and `AdminOrderListResponse` yourself, field by field, from `B042`, until nothing from that backend Spec is missing from `adminOrdersContract.ts`.

## Required UI implementation

Extend provider with orders. Add View/Manage permission constants. Mock server-like URL filters and pagination. Desktop table/mobile cards; detail uses snapshots, customer/address/totals and bounded payment history. Include loading, empty, 400, 403, 404 and unavailable. No action controls yet.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 8–17 for the mechanical breakdown of this section.)

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
