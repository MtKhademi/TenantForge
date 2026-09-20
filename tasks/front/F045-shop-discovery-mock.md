# F045 — Build contract-shaped storefront discovery mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F045` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S33`; depends on `F044`.
- Planned backend contract: `B037`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user can review a Darno-style all-products catalog with search, category, sale, sorting and pagination while all data remains local.

## Do this in order

Before step 1, follow the standing rules already described in the "Ownership,
phase and dependencies" section above: read `AGENTS.md`, read the linked
slice file (`S33`), read the paired backend contract Spec `B037`, and follow
the branch-naming and ledger-update rules from `AGENTS.md`'s Ownership
section. Do not skip those just because they are not repeated below.

1. Open `docs/design/shop/http-contracts.md` and find the section for `B037`. Note every field name, type and nullability it defines — copy these exactly, do not rename or reshape them.
2. Create `src/web/src/features/shop/contracts/discoveryContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`.)
3. In the same file, define `discoverySortSchema` as the exact enum `z.enum(['newest','price-asc','price-desc','name'])` — these four values only, spelled exactly this way.
4. Define the `DiscoveryQuery` type with fields `q`, `categorySlug`, `sort`, `saleOnly`, `pageNumber`, `pageSize` exactly as shown below.
5. Define `storefrontProductSummarySchema` and `storefrontProductListSchema` exactly as shown below. Also write out, in full, every nested type/schema the backend Spec `B037` names that is referenced here (for example the `pagination` schema) — do not leave any of them as a placeholder, comment, `any`, or unchecked cast.
6. Create `src/web/src/features/shop/clients/ShopDiscoveryClient.ts`. In it, define the `ShopDiscoveryClient` TypeScript interface exactly as shown below, with its one method `listProducts(tenantId, query, signal?)`. The `signal?: AbortSignal` parameter makes the call "abort-aware": if the caller cancels, in-flight work should stop instead of updating stale UI.
7. Create `src/web/src/features/shop/clients/mockShopDiscoveryClient.ts` implementing `ShopDiscoveryClient` fully. Give it deterministic, seeded fixture data — same input always yields the same result — and simulate network latency by awaiting the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`. Use that one helper; never call `setTimeout` directly in a mock client.
8. In the mock client, implement server-like filtering, sorting and paging: apply `q`, `categorySlug`, `sort` and `saleOnly` to the full fixture list, then slice the result by `pageNumber`/`pageSize`, and return that already-sorted, already-paged slice. Never let a component re-sort or re-filter a page that the mock client already returned.
9. Add named, in-memory scenarios (empty results, one page, many pages, invalid filters, unavailable) that can each be selected during development, without ever showing a scenario name or task ID in production-rendered text.
10. Wrap any scenario-selection UI in a check on `import.meta.env.DEV`, so it is stripped from the production build. Verify a production build (`cd src/web && npm run build`) contains no such toolbar.
11. Add a `discovery` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to `mockShopDiscoveryClient`. Do not touch any other slot.
12. Create `src/web/src/pages/shop/storefront/StorefrontCatalogPage.tsx`. This is the all-products mock page.
13. Make the page URL the single source of truth for filter state: read and write the query params `q`, `category`, `sort`, `sale`, `page`. Changing a filter must update the URL; loading the URL with params must reproduce that filter state.
14. Debounce the search input by exactly 300 ms before triggering a new query.
15. Whenever any filter (`q`, `category`, `sort`, `sale`) changes, reset `page` back to its first value.
16. Build the filter and product-card components to consume `discovery` only through `ShopClientsProvider`. Never import mock fixtures directly into a component, and never call `fetch` anywhere in this task.
17. Wire the route in `src/web/src/App.tsx`. The storefront routes today are:

    ```tsx
    <Route path="/shop/:tenantId" element={<StorefrontLayout />}>
      <Route index element={<CategoryPage />} />
      <Route path="categories/:categorySlug" element={<CategoryPage />} />
      ...
    ```

    Replace the `index` element with `<StorefrontCatalogPage />`, so `/shop/:tenantId` becomes the all-products page. Leave `categories/:categorySlug` pointing at `CategoryPage` — that route still handles a single category and this task does not change it. Do not delete `CategoryPage.tsx`.
18. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Never show a success message the mock did not actually send.
19. Run `cd src/web && npm run build`, then `cd src/web && npm run lint`; fix every error before moving on.
20. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence.
21. Report the exact line: `Data source: mock discovery client; HTTP integration deferred to the matching F054–F063 task.`
22. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/discoveryContract.ts`
- `src/web/src/features/shop/clients/ShopDiscoveryClient.ts`
- `src/web/src/features/shop/clients/mockShopDiscoveryClient.ts`
- `src/web/src/pages/shop/storefront/StorefrontCatalogPage.tsx`
- `src/web/src/components/shop/StorefrontFilters.tsx` and `src/web/src/components/shop/ProductCard.tsx`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `discovery` slot)
- `src/web/src/App.tsx` (the `/shop/:tenantId` index route)

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

JSON member casing and nullability mirror `B037` exactly. Mock IDs are canonical 13-character TSID strings (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"; treat it as an opaque 13-character string). Timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const discoverySortSchema = z.enum(['newest','price-asc','price-desc','name'])
export type DiscoveryQuery = { q: string; categorySlug: string | null; sort: z.infer<typeof discoverySortSchema>; saleOnly: boolean; pageNumber: number; pageSize: number }
export const storefrontProductSummarySchema = z.object({ id: z.string(), name: z.string(), slug: z.string(), effectivePrice: z.number(), compareAtPrice: z.number().nullable(), isOnSale: z.boolean(), isSoldOut: z.boolean(), thumbnailUrl: z.string().nullable() })
export const storefrontProductListSchema = z.object({ products: z.array(storefrontProductSummarySchema), pagination: paginationSchema })
export interface ShopDiscoveryClient { listProducts(tenantId: string, query: DiscoveryQuery, signal?: AbortSignal): Promise<z.infer<typeof storefrontProductListSchema>> }
```

Complete the schemas and types for every nested member named by the backend Spec (this includes `paginationSchema`, referenced above but not fully spelled out — write it out in full, field by field, matching `B037`). Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend `ShopClients` with `discovery`. Make `/shop/:tenantId` the all-products mock page. URL owns `q/category/sort/sale/page`; debounce search 300 ms and reset page when filters change. Mock loading, results, sold out, sale, empty, invalid filters and unavailable. Never client-sort a returned page; the mock client performs server-like filtering/paging.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

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
  `400` (a filter value is outside the allowed shape — e.g. a `sort` value that is not one of the four) and `404` (the `categorySlug` does not resolve). There is no 403/409/410 for this read-only public capability — do not build states for codes `B037` does not define. Each needs its own message — do not collapse them into one generic
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

Search and every sort; sale and category filters; URL refresh/back-forward; empty/error; responsive cards and pagination.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock discovery client; HTTP integration deferred to the matching F054–F063 task.`

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
- [ ] Contract schemas/types match `B037` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify a scenario for search text `q` — assert results narrow to matching products and the URL's `q` param updates after the 300 ms debounce.
- [ ] Write/verify a scenario for each `sort` value (`newest`, `price-asc`, `price-desc`, `name`) — assert the returned order matches that sort.
- [ ] Write/verify a scenario with `saleOnly` true — assert only `isOnSale` products appear.
- [ ] Write/verify a scenario with a `categorySlug` filter — assert only matching-category products appear.
- [ ] Write/verify a scenario for an empty result set — assert the empty state renders, not a blank list.
- [ ] Write/verify a scenario for invalid filters (values outside the named enum/shape) — assert the named validation error state renders.
- [ ] Write/verify a scenario for the unavailable state — assert a retry affordance renders.
- [ ] Write/verify that changing any filter resets `pageNumber` back to its first value.
- [ ] Write/verify that reloading a URL with `q`, `category`, `sort`, `sale` and `page` params reproduces the same filtered/sorted/paged view (browser back/forward included).
