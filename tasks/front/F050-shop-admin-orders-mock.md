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
2. Create `src/web/src/features/shop/contracts/adminOrdersContract.ts`. In it, define `export const adminOrderStatusSchema = z.enum(['PendingPayment','Paid','Cancelled','Fulfilled'])` exactly as written.
3. Define `adminOrderSummarySchema` as a Zod object (a Zod schema is a runtime validator + TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) with fields: `id` (string), `orderNumber` (string), `customerName` (string), `customerPhone` (string), `status` (`adminOrderStatusSchema`), `grandTotal` (number), `createdAtUtc` (string).
4. Define `adminOrderDetailSchema` as a Zod object with fields: `id` (string), `orderNumber` (string), `trackingCode` (string), `status` (`adminOrderStatusSchema`), `customer` (`adminOrderCustomerSchema` — define this nested schema yourself using every customer field `B042` names), `totals` (`adminOrderTotalsSchema` — define this nested schema yourself using every totals field `B042` names), `items` (array of `adminOrderItemSchema` — define this nested schema yourself using every item field `B042` names, remembering these are point-in-time snapshots, not live product data), `paymentAttempts` (array of `adminPaymentAttemptSchema` — define this nested schema yourself using every payment-attempt field `B042` names), `version` (integer), `createdAtUtc` (string).
5. Export the inferred TypeScript types for every schema above (e.g. `export type AdminOrderSummary = z.infer<typeof adminOrderSummarySchema>`, and likewise for detail, list-response and filters types named by `B042`).
6. Create `src/web/src/features/shop/clients/ShopOrdersClient.ts`. In it define `export interface ShopOrdersClient` with these exact methods:
   - `list(tenantId: string, filters: AdminOrderFilters, signal?: AbortSignal): Promise<AdminOrderListResponse>`
   - `get(tenantId: string, orderId: string, signal?: AbortSignal): Promise<AdminOrderDetail>`
   (`AbortSignal` makes each call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request.) Define `AdminOrderFilters` and `AdminOrderListResponse` yourself with every field `B042` names for filtering (status, date range, search, pagination, etc.) and for the paginated list response.
7. Go through the full backend Spec `B042` again and add every nested member it names to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
8. Create `src/web/src/features/shop/clients/mockShopOrdersClient.ts` implementing `ShopOrdersClient`. (The interface lives in `ShopOrdersClient.ts` from step 6; the mock is a separate file — do not put both in one file.) Make it deterministic (same input always produces the same output) and route all simulated delays through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`.
9. Give the mock client named scenarios (e.g. "list with filters", "detail with payment history", "foreign-tenant 404") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
10. Add an `orders` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to your new mock orders client. Do not touch any other slot.
11. Add two permission constants in `src/web/src/features/roles/roleTypes.ts`, beside the existing `SHOP_CATALOG_MANAGE_KEY` and `SHOP_SHIPPING_MANAGE_KEY`:

    ```ts
    export const SHOP_ORDERS_VIEW_KEY = 'Shop.Orders.View'
    export const SHOP_ORDERS_MANAGE_KEY = 'Shop.Orders.Manage'
    ```

    Also add `'Shop.Orders.View'` and `'Shop.Orders.Manage'` to the `TenantPermissionKey` union in that same file, and to the key list in `src/web/src/features/roles/permissionCatalog.ts`, matching exactly how `'Shop.Catalog.Manage'` and `'Shop.Shipping.Manage'` already appear in both places. The backend task `B042` adds the matching server-side keys; enforcing them for real is its job, but the mock UI must still branch on them.
12. Create `src/web/src/pages/shop/admin/OrdersPage.tsx` and implement filtering and pagination that behaves like a real server: reflect the current filters in the URL (so back/forward and reload keep the same filtered view), and read initial filters from the URL on load.
13. On desktop, render the order list as a table. On mobile, render it as cards instead. Use the same underlying data for both.
14. Create `src/web/src/pages/shop/admin/OrderDetailPage.tsx` and show the order using its stored snapshot fields (not live/current product or customer data), plus the customer info, address, totals, and a bounded (capped, not infinitely long) payment attempt history list.
15. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/400/403/404/410/429 states this capability names (this task specifically needs 400, 403, 404 and unavailable, per "Required UI implementation" below), an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
16. Add no action controls (fulfil/cancel/etc.) to OrderDetailPage in this task — that is out of scope here and belongs to `F051`.
17. In `src/web/src/App.tsx`, add `/t/:tenantId/shop/orders` -> `OrdersPage` and `/t/:tenantId/shop/orders/:orderId` -> `OrderDetailPage` inside the existing `<ProtectedLayout />` block, beside the other `/t/:tenantId/shop/*` routes. Add the matching nav entry in `src/web/src/components/shell/ShellNav.tsx`, gated on `SHOP_ORDERS_VIEW_KEY` the same way the existing Shop nav items are gated on `SHOP_CATALOG_MANAGE_KEY` (delivered by `F043`). When the current user lacks `Shop.Orders.View`, both pages must render a permission-denied state instead of their content.
18. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/adminOrdersContract.ts`
- `src/web/src/features/shop/clients/ShopOrdersClient.ts` (interface only)
- `src/web/src/features/shop/clients/mockShopOrdersClient.ts`
- `src/web/src/pages/shop/admin/OrdersPage.tsx`
- `src/web/src/pages/shop/admin/OrderDetailPage.tsx`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `orders` slot)
- `src/web/src/features/roles/roleTypes.ts`, `src/web/src/features/roles/permissionCatalog.ts`
- `src/web/src/App.tsx`, `src/web/src/components/shell/ShellNav.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

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

Build every one of these. "State" means something the user can actually see on
screen, not a code path.

- **idle / initial** — before anything is requested.
- **loading** — visible progress, with no destructive layout shift (the page
  must not jump or reflow when loading finishes).
- **success** — the normal populated result.
- **empty** — a successful response that contains no items. This is not an
  error; it must not look like one.
- **each error this capability actually defines.** For this task those are:
  `400` (an invalid filter value — an unknown `status`, a malformed date, or a range over 366 days), `403` (the user lacks `Shop.Orders.View`) and `404` (a malformed, missing or other-tenant order ID — all three look identical). This read-only capability has no 409/410/429 states; do not build them. Each needs its own message — do not collapse them into one generic
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

Every filter and URL history; list/detail; View denied; foreign-like 404; snapshot content; responsive evidence.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock orders client; HTTP integration deferred to the matching F054–F063 task.`

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
- [ ] Contract schemas/types match `B042` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify the order list with no filters — assert rows render with order number, customer, status, total and created date.
- [ ] Write/verify each filter (status, date range, search) — assert the list narrows and the URL query params update.
- [ ] Write/verify reloading a URL that already carries filter params — assert the same filtered view renders; then assert browser back/forward moves between filter states.
- [ ] Write/verify paging forward and back — assert the page param is in the URL and the content changes.
- [ ] Write/verify an empty result set — assert the empty state renders, not a blank table.
- [ ] Write/verify a `400` invalid-filter response — assert the validation error state renders.
- [ ] Write/verify a user without `Shop.Orders.View` — assert the permission-denied state renders instead of page content, and the nav entry is hidden.
- [ ] Write/verify a foreign/unknown order ID — assert the generic not-found state renders (identical for malformed, missing and other-tenant IDs).
- [ ] Write/verify the order detail page — assert item rows show the stored snapshot name/price, plus customer, address and totals.
- [ ] Write/verify an order with more than 20 payment attempts — assert the history list is capped and does not grow without bound.
- [ ] Write/verify that OrderDetailPage renders **no** fulfil/cancel controls in this task — those belong to `F051`.
- [ ] Write/verify the list renders as a table at 1440×900 and as cards at 390×844, from the same data.
