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
2. Create `contracts/discoveryContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, already used elsewhere in `features/shop/contracts/`.)
3. In the same file, define `discoverySortSchema` as the exact enum `z.enum(['newest','price-asc','price-desc','name'])` — these four values only, spelled exactly this way.
4. Define the `DiscoveryQuery` type with fields `q`, `categorySlug`, `sort`, `saleOnly`, `pageNumber`, `pageSize` exactly as shown below.
5. Define `storefrontProductSummarySchema` and `storefrontProductListSchema` exactly as shown below. Also write out, in full, every nested type/schema the backend Spec `B037` names that is referenced here (for example the `pagination` schema) — do not leave any of them as a placeholder, comment, `any`, or unchecked cast.
6. Create `features/shop/clients/ShopDiscoveryClient.ts`. In it, define the `ShopDiscoveryClient` TypeScript interface exactly as shown below, with its one method `listProducts(tenantId, query, signal?)`. The `signal?: AbortSignal` parameter makes the call "abort-aware": if the caller cancels, in-flight work should stop instead of updating stale UI.
7. Create `mockShopDiscoveryClient.ts` implementing `ShopDiscoveryClient` fully. Give it deterministic, seeded fixture data — same input always yields the same result — and simulate network latency using the shared abort-aware delay helper already used by other mock clients in `features/shop/clients/`. Reuse that helper; do not write a new one.
8. In the mock client, implement server-like filtering, sorting and paging: apply `q`, `categorySlug`, `sort` and `saleOnly` to the full fixture list, then slice the result by `pageNumber`/`pageSize`, and return that already-sorted, already-paged slice. Never let a component re-sort or re-filter a page that the mock client already returned.
9. Add named, in-memory scenarios (empty results, one page, many pages, invalid filters, unavailable) that can each be selected during development, without ever showing a scenario name or task ID in production-rendered text.
10. Wrap any scenario-selection UI in a check on `import.meta.env.DEV`, so it is stripped from the production build. Verify a production build (`npm run build`) contains no such toolbar.
11. Extend `ShopClientsProvider`'s exported `ShopClients` object with a new `discovery` slot wired to `mockShopDiscoveryClient` for this task.
12. Build/update `StorefrontCatalogPage.tsx` as the page rendered at route `/shop/:tenantId`. It is the all-products mock page.
13. Make the page URL the single source of truth for filter state: read and write the query params `q`, `category`, `sort`, `sale`, `page`. Changing a filter must update the URL; loading the URL with params must reproduce that filter state.
14. Debounce the search input by exactly 300 ms before triggering a new query.
15. Whenever any filter (`q`, `category`, `sort`, `sale`) changes, reset `page` back to its first value.
16. Build the filter and product-card components to consume `discovery` only through `ShopClientsProvider`. Never import mock fixtures directly into a component, and never call `fetch` anywhere in this task.
17. Add/update the App route(s) needed to reach `StorefrontCatalogPage` at `/shop/:tenantId`.
18. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Never show a success message the mock did not actually send.
19. Run `npm run build` and `npm run lint`; fix all errors before moving on.
20. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence.
21. Report the exact line: `Data source: mock discovery client; HTTP integration deferred to the matching F054–F063 task.`
22. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

`contracts/discoveryContract.ts`, `clients/ShopDiscoveryClient.ts`, `mockShopDiscoveryClient.ts`, provider extension, `StorefrontCatalogPage.tsx`, filters and product-card components, App routes.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

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

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Search and every sort; sale and category filters; URL refresh/back-forward; empty/error; responsive cards and pagination.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock discovery client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

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
