# F045 — Build contract-shaped storefront discovery mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F045` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S33`; depends on `F044`.
- Planned backend contract: `B037`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user can review a Darno-style all-products catalog with search, category, sale, sorting and pagination while all data remains local.

## Files expected to change

`contracts/discoveryContract.ts`, `clients/ShopDiscoveryClient.ts`, `mockShopDiscoveryClient.ts`, provider extension, `StorefrontCatalogPage.tsx`, filters and product-card components, App routes.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B037` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const discoverySortSchema = z.enum(['newest','price-asc','price-desc','name'])
export type DiscoveryQuery = { q: string; categorySlug: string | null; sort: z.infer<typeof discoverySortSchema>; saleOnly: boolean; pageNumber: number; pageSize: number }
export const storefrontProductSummarySchema = z.object({ id: z.string(), name: z.string(), slug: z.string(), effectivePrice: z.number(), compareAtPrice: z.number().nullable(), isOnSale: z.boolean(), isSoldOut: z.boolean(), thumbnailUrl: z.string().nullable() })
export const storefrontProductListSchema = z.object({ products: z.array(storefrontProductSummarySchema), pagination: paginationSchema })
export interface ShopDiscoveryClient { listProducts(tenantId: string, query: DiscoveryQuery, signal?: AbortSignal): Promise<z.infer<typeof storefrontProductListSchema>> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

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
