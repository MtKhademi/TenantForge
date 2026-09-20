# F060 — Bind admin orders client to B042 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F060` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F050**.
- Slice: `S38`; depends on `F059, B042`.
- Backend source of truth: `B042` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

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

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `orders` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
