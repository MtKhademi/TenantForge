# F061 — Bind order operation client to B043 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F061` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F051.**
- Slice: `S39`; depends on `F060, B043`.
- Backend source of truth: `B043` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before anything else: read `AGENTS.md` (branch naming, ledger update rules and the ownership boundaries are defined there — this checklist does not repeat them), read the `S39` slice file, read the linked `B043` backend task and its delivered C# records/tests, and read the matching `docs/design/shop/http-contracts.md` section. Only then start on the steps below.

1. Create `src/web/src/features/shop/clients/httpShopOrderOperationsClient.ts` implementing the `ShopOrderOperationsClient` interface from `F051`. That file plus `src/web/src/features/shop/clients/ShopClientsProvider.tsx` are the only things you change in this task. Do not touch `httpShopOrdersClient.ts` from `F060` — reads and mutations are separate slots.
2. Confirm the request/response TypeScript schema still matches the actual delivered `B043` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator, created by the paired mock task under `src/web/src/features/shop/contracts/`) to make a mismatch silently pass.
3. Implement `changeStatus` exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path/body names already defined in this file):
   ```ts
   return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}/status`, { method: 'PATCH', headers: { 'Idempotency-Key': idempotencyKey }, json: body, signal }));
   ```
   `shopFetch` is the authenticated fetch helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` — reuse it, do not write a new fetch wrapper. (Its anonymous twin for public storefront routes is `shopFetchPublic`.) `PATCH` sends a partial update (here: the action and version fields) rather than replacing the whole order.
4. The request body (`body`) must contain exactly the action and the current `version` field — nothing more, nothing renamed. The backend uses `version` to detect conflicting concurrent edits.
5. Send the `Idempotency-Key` header exactly as shown above. (Idempotency-Key = a header value that lets the server recognize a retried request as "the same request" so repeating it has no extra effect — this is what "idempotent" means. It prevents the same status change from being applied twice if a request is retried after a network blip.)
6. Reuse the **same** idempotency key value only when retrying the **exact same, unchanged** request (same order, same action, same version). If the user changes what they're requesting (different action, or the version has moved on), generate a **new** idempotency key. Never reuse a key across two logically different requests.
7. Parse the response through the admin order detail schema (as shown in the snippet above) — this is the same schema type used elsewhere for order details.
8. Handle an HTTP `409` (Conflict — the order's version on the server no longer matches what the client sent, meaning someone else changed it first) specially: on `409`, trigger the existing "reviewed refresh" path, i.e. re-fetch the current order detail and show it to the user for review before they retry the action, instead of silently retrying or silently discarding their intended change.
9. For this operation:
   - URL-encode every path segment (e.g. `encodeURIComponent(orderId)`) and every query parameter.
   - Pass the `AbortSignal` (`signal`) through to `shopFetch`. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - Parse every successful JSON response through the Zod schema already created in `F051`.
   - Normalize any failure into `ShopClientError` exactly once — do not invent a second error shape.
   - This mutation is an admin route (`/api/tenants/{tenantId}/shop/...`), so use `shopFetch`, which attaches the bearer token. `shopFetchPublic` (no token) is for public storefront routes only and is not used here.
10. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` wires each capability slot to either its mock or its real HTTP implementation. Change **only** the `orderOperations` slot from `mockShopOrderOperationsClient` to `httpShopOrderOperationsClient`. Leave every other slot as it is; `payments` and `problems` get switched in `F062` and `F063`.
11. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`.
12. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopOrderOperationsClient.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

PATCH exact action/version body and Idempotency-Key. Reuse key only for unchanged retry. Parse returned admin detail; 409 triggers reviewed refresh path. Switch mutation method from mock to HTTP.

```ts
return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}/status`, { method: 'PATCH', headers: { 'Idempotency-Key': idempotencyKey }, json: body, signal }));
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `orderOperations` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F051` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F051`, now against the real backend, and confirm each one still matches.
- [ ] Change an order's status, then reload the page and confirm the new status persisted on the server.
- [ ] Trigger a real `409` conflict (e.g. by changing the version) and confirm the reviewed-refresh path is shown, not a silent retry or a silently dropped change.
- [ ] Fetch/mutate the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
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
- [ ] `orderOperations` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
