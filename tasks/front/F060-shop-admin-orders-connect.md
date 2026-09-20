# F060 — Bind admin orders client to B042 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F060` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F050.**
- Slice: `S38`; depends on `F059, B042`.
- Backend source of truth: `B042` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before anything else: read `AGENTS.md` (branch naming, ledger update rules and the ownership boundaries are defined there — this checklist does not repeat them), read the `S38` slice file, read the linked `B042` backend task and its delivered C# records/tests, and read the matching `docs/design/shop/http-contracts.md` section. Only then start on the steps below.

1. Open `clients/httpShopOrdersClient.ts`. This, plus order permission constants and composition, are the only files you touch in this task.
2. Confirm the request/response TypeScript schema in this file still matches the actual delivered `B042` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator already used elsewhere in this codebase) to make a mismatch silently pass.
3. Implement the **paged list** operation. Encode every filter value as a properly URL-encoded query parameter. Parse the response through the existing paged-list schema from `F050`.
4. Implement the **detail** operation exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path names already defined in this file):
   ```ts
   return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}`, { signal }));
   ```
   `shopFetch` is the existing authenticated fetch helper already used by other real clients in this codebase — reuse it, do not write a new fetch wrapper.
5. When parsing the detail response, make sure the exact order **snapshot** shape (the point-in-time copy of order data as it existed when the order was placed) and the exact **payment** shape are both parsed through the schema unchanged — do not drop, rename, or flatten any of their fields.
6. Make sure the response's `Version` field is parsed and kept exactly as returned by the backend — do not recompute or drop it. (`Version` is used elsewhere for conflict detection, so it must round-trip exactly.)
7. Open the file(s) defining permission guard constants for this feature. Add two permission keys exactly named `Shop.Orders.View` and `Shop.Orders.Manage` to the permission guards that gate the admin orders screens, matching how other permission keys are already wired into guards in this codebase.
8. Add explicit handling so that:
   - an HTTP `400` response (bad request) is normalized into `ShopClientError` and shown as a validation-type error,
   - an HTTP `403` response (forbidden — the caller is authenticated but lacks permission) is normalized into `ShopClientError` and shown as a permission-denied error,
   - an HTTP `404` response (not found) is normalized into `ShopClientError` and shown as a not-found error.
   Use the single existing `ShopClientError` normalization path for all three — do not invent three separate error types.
9. For both operations (list and detail):
   - URL-encode every path segment (e.g. `encodeURIComponent(orderId)`) and every query parameter.
   - Pass the `AbortSignal` (`signal`) through to `shopFetch` on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - Parse every successful JSON response through the Zod schema already created in `F050`.
   - Admin routes use the existing authenticated `shopFetch`/token behavior. Storefront (customer-facing, non-admin) routes are plain anonymous requests — do not attach an auth token to them.
10. Open the file that defines `createShopClients()` (the composition function that wires each capability to either its mock or real HTTP implementation). Change **only** the `orders` slot so it points at your new `httpShopOrdersClient.ts` implementation. This task connects **order reads only** (list and detail) — leave every other slot, including order mutations, pointing at its mock; those get switched in their own later tasks.
11. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`.
12. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`clients/httpShopOrdersClient.ts`, order permission constants and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind paged list and detail, encode filters, parse exact snapshot/payment shapes and Version. Add `Shop.Orders.View/Manage` to permission guards. Map 400/403/404. Switch only order reads.

```ts
return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}`, { signal }));
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F050`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `orders` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F050` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F050`, now against the real backend, and confirm each one still matches.
- [ ] Reload the page after fetching list/detail data and confirm it still shows the same real, persisted data.
- [ ] Trigger one real backend failure, permission-denied (403), or not-found (404) path and confirm the UI shows it correctly.
- [ ] Fetch the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `npm run build` and confirm it succeeds.
- [ ] Run `npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `orders` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
