# F055 — Bind discovery client to B037 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F055` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F045**.
- Slice: `S33`; depends on `F054, B037`.
- Backend source of truth: `B037` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before starting, follow the boilerplate in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `S33` slice file, read the paired backend task `B037` and its delivered contract, and follow the existing branch-naming/ledger-update rules. Do not skip those — this checklist only covers the coding steps.

1. Open `docs/design/shop/http-contracts.md` and find the B037 section. Compare every route, field name, and status code listed there against the delivered C# request/response records and B037's integration tests. If anything differs from what this Spec below describes, stop, write down the exact mismatch, and ask for a contract correction decision before writing any code.
2. Open (or create) `clients/httpShopDiscoveryClient.ts`. This is the only client file you should need to touch for this task.
3. In the function that fetches the storefront product list, build a `URLSearchParams` object named `params` with exactly these entries: `pageNumber` set to `String(query.pageNumber)`, `pageSize` set to `String(query.pageSize)`, `sort` set to `query.sort`, and `saleOnly` set to `String(query.saleOnly)`.
4. If `query.q` is set (truthy), add it to `params` under the key `q`.
5. If `query.categorySlug` is set (truthy), add it to `params` under the key `categorySlug`.
6. Call `shopFetch` (the repo's existing authenticated-fetch helper) against `` `/api/shop/${encodeURIComponent(tenantId)}/products?${params}` `` passing `{ signal }`, then parse the JSON result with `storefrontProductListSchema.parse(...)` and return it.
7. Reference implementation (copy/adapt this exactly, it is the complete required code):

```ts
const params = new URLSearchParams({
  pageNumber: String(query.pageNumber),
  pageSize: String(query.pageSize),
  sort: query.sort,
  saleOnly: String(query.saleOnly),
})
if (query.q) params.set('q', query.q)
if (query.categorySlug) params.set('categorySlug', query.categorySlug)
return storefrontProductListSchema.parse(
  await shopFetch(`/api/shop/${encodeURIComponent(tenantId)}/products?${params}`, { signal }),
)
```

8. This route is a public storefront route: use plain anonymous requests through `shopFetch` (no auth token needed), not the authenticated admin path.
9. Never implement client-side filtering, sorting, or pagination. Every one of `q`, `categorySlug`, `sort`, `pageNumber`, `pageSize`, and `saleOnly` must be sent to the server as a query parameter, and the response list must be rendered exactly as returned — do not re-filter, re-sort, or re-slice it in the client.
10. Leave `AbortController` creation and ownership on the page/component that already owns it. Only accept the `signal` parameter in the client method and pass it straight into `shopFetch`; do not create a new `AbortController` inside the client.
11. Make sure the tenant ID path segment is passed through `encodeURIComponent`.
12. Parse the successful JSON response through `storefrontProductListSchema`, the Zod schema (a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/`) already created in task `F045`. Do not add new fields or rename any existing schema field.
13. Normalize any failure response into the existing `ShopClientError` type (the repo's shared error type for normalized HTTP failures) using the same normalization helper used elsewhere in the Shop clients.
14. Open the Shop client composition file (where `createShopClients()` is defined). Find the `discovery` slot. Change it from the mock discovery client to the new HTTP discovery client. Do not touch any other slot.
15. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
16. Confirm the expected diff is limited to `clients/httpShopDiscoveryClient.ts` and the Shop client composition file. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
17. Run the regression and browser proof steps in the "Regression and browser proof" section below.
18. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`clients/httpShopDiscoveryClient.ts` and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Build query with URLSearchParams; parse exact B037 list response; AbortController stays page-owned. Never client-filter/sort/paginate. Switch only `discovery`.

```ts
const params = new URLSearchParams({ pageNumber: String(query.pageNumber), pageSize: String(query.pageSize), sort: query.sort, saleOnly: String(query.saleOnly) }); if (query.q) params.set('q', query.q); if (query.categorySlug) params.set('categorySlug', query.categorySlug); return storefrontProductListSchema.parse(await shopFetch(`/api/shop/${encodeURIComponent(tenantId)}/products?${params}`, { signal }));
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal (the object used to cancel an in-flight `fetch`; the repo's `shopFetch` already accepts it as `{ signal }`). Parse every successful JSON response through the schema created in `F045`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `discovery` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F045` against the real backend. Concretely: for each state F045 demonstrated (default list, search query, category filter, sale-only filter, sort order, pagination, empty results, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: apply a filter/search/sort, reload the page, and confirm the URL/state reproduces the same result; then trigger one relevant failure case and confirm it surfaces as a normal error via `ShopClientError`, not a crash or silent fallback.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `storefrontProductListSchema` parses both the old mock data and the new real backend data without modification.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `discovery` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
</content>
</invoke>
