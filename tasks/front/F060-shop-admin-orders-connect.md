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

1. Create `src/web/src/features/shop/clients/httpShopOrdersClient.ts`. This file and `ShopClientsProvider.tsx` are the only files you edit in this task.
2. Confirm the request/response TypeScript schema in this file still matches the actual delivered `B042` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator, created by the paired mock task under `src/web/src/features/shop/contracts/`) to make a mismatch silently pass.
3. Implement the **paged list** operation. Encode every filter value as a properly URL-encoded query parameter. Parse the response through the existing paged-list schema from `F050`.
4. Implement the **detail** operation exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path names already defined in this file):
   ```ts
   return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}`, { signal }));
   ```
   `shopFetch` is the authenticated fetch helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` — reuse it, do not write a new fetch wrapper. (Its anonymous twin for public storefront routes is `shopFetchPublic`.)
5. When parsing the detail response, make sure the exact order **snapshot** shape (the point-in-time copy of order data as it existed when the order was placed) and the exact **payment** shape are both parsed through the schema unchanged — do not drop, rename, or flatten any of their fields.
6. Make sure the response's `Version` field is parsed and kept exactly as returned by the backend — do not recompute or drop it. (`Version` is used elsewhere for conflict detection, so it must round-trip exactly.)
7. `F050` already added `SHOP_ORDERS_VIEW_KEY` and `SHOP_ORDERS_MANAGE_KEY` to `src/web/src/features/roles/roleTypes.ts` and `permissionCatalog.ts`. Open both files and confirm they are there and spelled exactly `'Shop.Orders.View'` and `'Shop.Orders.Manage'`. Do **not** add them a second time. If either is missing, that is an `F050` defect — report it and stop rather than adding it here.
8. Add explicit handling so that:
   - an HTTP `400` response (bad request) is normalized into `ShopClientError` and shown as a validation-type error,
   - an HTTP `403` response (forbidden — the caller is authenticated but lacks permission) is normalized into `ShopClientError` and shown as a permission-denied error,
   - an HTTP `404` response (not found) is normalized into `ShopClientError` and shown as a not-found error.
   Use the single existing `ShopClientError` normalization path for all three — do not invent three separate error types.
9. For both operations (list and detail):
   - URL-encode every path segment (e.g. `encodeURIComponent(orderId)`) and every query parameter.
   - Pass the `AbortSignal` (`signal`) through to `shopFetch` on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - Parse every successful JSON response through the Zod schema already created in `F050`.
   - Admin routes (`/api/tenants/{tenantId}/shop/...`) use `shopFetch`, which attaches the bearer token. Public storefront routes (`/api/shop/{tenantId}/...`) use `shopFetchPublic`, which sends no token. Both are in `src/web/src/features/shop/clients/shopFetch.ts`.
10. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` wires each capability slot to either its mock or its real HTTP implementation. Change **only** the `orders` slot so it points at your new `httpShopOrdersClient.ts` implementation. This task connects **order reads only** (list and detail) — leave every other slot, including order mutations, pointing at its mock; those get switched in their own later tasks.
11. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`.
12. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopOrdersClient.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only. The order permission constants already exist from `F050`; this task only verifies them.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind paged list and detail, encode filters, parse exact snapshot/payment shapes and Version. Add `Shop.Orders.View/Manage` to permission guards. Map 400/403/404. Switch only order reads.

```ts
return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}`, { signal }));
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `orders` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F050` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F050`, now against the real backend, and confirm each one still matches.
- [ ] Reload the page after fetching list/detail data and confirm it still shows the same real, persisted data.
- [ ] Trigger one real backend failure, permission-denied (403), or not-found (404) path and confirm the UI shows it correctly.
- [ ] Fetch the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `cd src/web && npm run build` and confirm it succeeds.
- [ ] Run `cd src/web && npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted.
   The component/page diff is expected to be zero — if it is not, say exactly
   which component you had to touch and which contract mismatch forced it.
2. **Implementation decisions.** Every decision this Spec left to you, with the
   option you picked and one sentence of why.
3. **Contract comparison.** The result of step 1's check: whether the delivered
   backend C# records, routes and status codes matched
   `docs/design/shop/http-contracts.md` and this Spec. List every difference
   you found, even ones you decided were harmless. If you found none, say so
   explicitly.
4. **Commands executed.** `cd src/web && npm run build` and
   `cd src/web && npm run lint`, copied verbatim, in the order you ran them.
   State explicitly that you did not run `npm test` or `npm run test:e2e`.
5. **Results of those checks.** For each command: pass or fail, plus the error
   text if it failed. Then: which of the paired mock task's scenarios you
   replayed against the real backend, which slot is now real, whether reload
   and tenant-switch persisted, which failure path you triggered and how it
   surfaced, and whether the browser console stayed clean at all three
   viewports. Never report a check as passing if you did not run it.
6. **Risks, blockers and follow-up.** Anything you could not verify, any
   acceptance item you could not check off and why, and anything the next
   connection task needs to know. Write "None." if there is genuinely nothing.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `orders` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
