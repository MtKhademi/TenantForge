# F059 — Bind coupon rules client to B041 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F059` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F049.**
- Slice: `S37`; depends on `F058, B041`.
- Backend source of truth: `B041` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before anything else: read `AGENTS.md` (branch naming, ledger update rules and the ownership boundaries are defined there — this checklist does not repeat them), read the `S37` slice file, read the linked `B041` backend task and its delivered C# records/tests, and read the matching `docs/design/shop/http-contracts.md` section. Only then start on the steps below.

1. Open `clients/httpShopCouponClient.ts`. This is the only client file you touch in this task.
2. Confirm the request/response TypeScript schema in this file still matches the actual delivered `B041` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch (field name, type, or shape difference), and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator already used elsewhere in this codebase; "weakening" it means making a field optional or untyped just to make errors go away — never do that) to make a mismatch silently pass.
3. Implement the **list** operation: call the coupons endpoint for the tenant, and parse the successful JSON response through the existing coupon-list schema created in `F049`.
4. Implement the **create** operation: POST a new coupon, and parse the response through the existing coupon schema from `F049`.
5. Implement the **update** operation exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path names already defined in this file):
   ```ts
   return couponSchema.parse(await shopFetch(`${couponPath(tenantId)}/${encodeURIComponent(couponId)}`, { method: 'PUT', json: body, signal }));
   ```
   `shopFetch` is the existing authenticated fetch helper already used by other real clients in this codebase — reuse it, do not write a new fetch wrapper.
6. Implement the **deactivate** operation the same way as update: same `shopFetch` pattern, PUT/PATCH to the deactivate route already defined for this client, response parsed through the existing schema.
7. For every one of the four operations above (list, create, update, deactivate):
   - URL-encode every path segment (e.g. `encodeURIComponent(couponId)`) and every query parameter. Never interpolate a raw, unencoded value into a URL.
   - Pass the `AbortSignal` (`signal`) through to `shopFetch` on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - Parse every successful JSON response through the Zod schema already created in `F049`. Never skip parsing and never hand-cast the response.
   - On any failure, normalize it into `ShopClientError` exactly once (the existing error type used by every other real client) — do not invent a second error shape.
   - Admin routes use the existing authenticated `shopFetch`/token behavior. Storefront (customer-facing, non-admin) routes are plain anonymous requests — do not attach an auth token to them.
8. Preserve the exact decimal money convention this client already uses for currency amounts. Do not change how money values are represented (e.g. do not switch between cents-as-integer and decimal-as-string, or round differently) anywhere in this file.
9. Make sure every parsed coupon response correctly surfaces its `version` field and any count fields exactly as the backend returns them — do not rename, drop, or recompute these values on the client.
10. In the checkout flow's error-code mapping, make sure the reason shown to the user for a rejected coupon comes from the **server's** reason code in the response body, not from a client-guessed or hardcoded string.
11. Open the file that defines `createShopClients()` (the composition function that wires each capability — coupons, orders, payments, etc. — to either its mock implementation or its real HTTP implementation). Change **only** the `coupons` slot so it points at your new `httpShopCouponClient.ts` implementation instead of the mock. Leave every other slot (orders, payments, etc.) pointing at its mock — those get switched in their own later tasks, not this one.
12. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`, never fall back to mock data.
13. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`clients/httpShopCouponClient.ts`, checkout error-code mapping and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind list/create/update/deactivate; preserve decimal current money convention exactly. Parse response versions/counts. Checkout uses server reason code. Switch only `coupons`.

```ts
return couponSchema.parse(await shopFetch(`${couponPath(tenantId)}/${encodeURIComponent(couponId)}`, { method: 'PUT', json: body, signal }));
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F049`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `coupons` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F049` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F049`, now against the real backend, and confirm each one still matches.
- [ ] Reload the page after a successful create/update/deactivate and confirm the change persisted on the server (not just in local state).
- [ ] Trigger one real backend failure, permission-denied, or conflict path and confirm the UI shows it correctly.
- [ ] Fetch the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `npm run build` and confirm it succeeds.
- [ ] Run `npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `coupons` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
