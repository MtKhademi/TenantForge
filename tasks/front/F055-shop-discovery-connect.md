# F055 — Bind discovery client to B037 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F055` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F045**.
- Slice: `S33`; depends on `F054, B037`.
- Backend source of truth: `B037` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Files expected to change

`clients/httpShopDiscoveryClient.ts` and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Build query with URLSearchParams; parse exact B037 list response; AbortController stays page-owned. Never client-filter/sort/paginate. Switch only `discovery`.

```ts
const params = new URLSearchParams({ pageNumber: String(query.pageNumber), pageSize: String(query.pageSize), sort: query.sort, saleOnly: String(query.saleOnly) }); if (query.q) params.set('q', query.q); if (query.categorySlug) params.set('categorySlug', query.categorySlug); return storefrontProductListSchema.parse(await shopFetch(`/api/shop/${encodeURIComponent(tenantId)}/products?${params}`, { signal }));
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F045`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `discovery` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F045` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `discovery` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
