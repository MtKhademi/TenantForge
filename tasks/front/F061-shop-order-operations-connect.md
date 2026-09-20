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

1. Open the HTTP order client file and find its `changeStatus` function. This function's implementation, plus composition, are the only things you change in this task.
2. Confirm the request/response TypeScript schema still matches the actual delivered `B043` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator already used elsewhere in this codebase) to make a mismatch silently pass.
3. Implement `changeStatus` exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path/body names already defined in this file):
   ```ts
   return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}/status`, { method: 'PATCH', headers: { 'Idempotency-Key': idempotencyKey }, json: body, signal }));
   ```
   `shopFetch` is the existing authenticated fetch helper already used by other real clients in this codebase — reuse it, do not write a new fetch wrapper. `PATCH` sends a partial update (here: the action and version fields) rather than replacing the whole order.
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
   - Admin routes use the existing authenticated `shopFetch`/token behavior. Storefront (customer-facing, non-admin) routes are plain anonymous requests — not relevant to this specific mutation, but keep that rule in mind if you touch shared code.
10. Open the file that defines `createShopClients()` (the composition function that wires each capability to either its mock or real HTTP implementation). Change **only** the `orderOperations` slot — specifically, switch the mutation method (`changeStatus`) from its mock implementation to this new HTTP implementation. Leave every other slot pointing at its mock; those get switched in their own later tasks.
11. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`.
12. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

HTTP order client `changeStatus` implementation and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

PATCH exact action/version body and Idempotency-Key. Reuse key only for unchanged retry. Parse returned admin detail; 409 triggers reviewed refresh path. Switch mutation method from mock to HTTP.

```ts
return adminOrderDetailSchema.parse(await shopFetch(`${tenantShopPath(tenantId, 'orders')}/${encodeURIComponent(orderId)}/status`, { method: 'PATCH', headers: { 'Idempotency-Key': idempotencyKey }, json: body, signal }));
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F051`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `orderOperations` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F051` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F051`, now against the real backend, and confirm each one still matches.
- [ ] Change an order's status, then reload the page and confirm the new status persisted on the server.
- [ ] Trigger a real `409` conflict (e.g. by changing the version) and confirm the reviewed-refresh path is shown, not a silent retry or a silently dropped change.
- [ ] Fetch/mutate the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `npm run build` and confirm it succeeds.
- [ ] Run `npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `orderOperations` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
