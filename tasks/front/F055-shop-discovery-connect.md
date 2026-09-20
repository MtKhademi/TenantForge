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
2. Create `src/web/src/features/shop/clients/httpShopDiscoveryClient.ts`. This is the only client file you should need to touch for this task.
3. In the function that fetches the storefront product list, build a `URLSearchParams` object named `params` with exactly these entries: `pageNumber` set to `String(query.pageNumber)`, `pageSize` set to `String(query.pageSize)`, `sort` set to `query.sort`, and `saleOnly` set to `String(query.saleOnly)`.
4. If `query.q` is set (truthy), add it to `params` under the key `q`.
5. If `query.categorySlug` is set (truthy), add it to `params` under the key `categorySlug`.
6. Call `shopFetchPublic` against `` `/api/shop/${encodeURIComponent(tenantId)}/products?${params}` `` passing `{ signal }`, then parse the JSON result with `storefrontProductListSchema.parse(...)` and return it. This is a public storefront route, so use `shopFetchPublic` (sends no token), **not** `shopFetch` (sends the bearer token).
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
  await shopFetchPublic(`/api/shop/${encodeURIComponent(tenantId)}/products?${params}`, { signal }),
)
```

8. Confirm you used `shopFetchPublic` and not `shopFetch`: this storefront route is anonymous and must not carry an `Authorization` header.
9. Never implement client-side filtering, sorting, or pagination. Every one of `q`, `categorySlug`, `sort`, `pageNumber`, `pageSize`, and `saleOnly` must be sent to the server as a query parameter, and the response list must be rendered exactly as returned — do not re-filter, re-sort, or re-slice it in the client.
10. Leave `AbortController` creation and ownership on the page/component that already owns it. Only accept the `signal` parameter in the client method and pass it straight into `shopFetch`; do not create a new `AbortController` inside the client.
11. Make sure the tenant ID path segment is passed through `encodeURIComponent`.
12. Parse the successful JSON response through `storefrontProductListSchema`, the Zod schema (a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) already created in task `F045`. Do not add new fields or rename any existing schema field.
13. Normalize any failure response into the shared `ShopClientError` type from `src/web/src/features/shop/contracts/shopContract.ts` (created by `F044`) using the same normalization helper used elsewhere in the Shop clients.
14. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` is defined. Find the `discovery` slot. Change it from the mock discovery client to the new HTTP discovery client. Do not touch any other slot.
15. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
16. Confirm the expected diff is limited to `clients/httpShopDiscoveryClient.ts` and the Shop client composition file. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
17. Run the regression and browser proof steps in the "Regression and browser proof" section below.
18. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopDiscoveryClient.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Build query with URLSearchParams; parse exact B037 list response; AbortController stays page-owned. Never client-filter/sort/paginate. Switch only `discovery`.

```ts
const params = new URLSearchParams({ pageNumber: String(query.pageNumber), pageSize: String(query.pageSize), sort: query.sort, saleOnly: String(query.saleOnly) }); if (query.q) params.set('q', query.q); if (query.categorySlug) params.set('categorySlug', query.categorySlug); return storefrontProductListSchema.parse(await shopFetchPublic(`/api/shop/${encodeURIComponent(tenantId)}/products?${params}`, { signal }));
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `discovery` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F045` against the real backend. Concretely: for each state F045 demonstrated (default list, search query, category filter, sale-only filter, sort order, pagination, empty results, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: apply a filter/search/sort, reload the page, and confirm the URL/state reproduces the same result; then trigger one relevant failure case and confirm it surfaces as a normal error via `ShopClientError`, not a crash or silent fallback.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `storefrontProductListSchema` parses both the old mock data and the new real backend data without modification.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

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
- [ ] `discovery` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
