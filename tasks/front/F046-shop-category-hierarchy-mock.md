# F046 — Build contract-shaped nested category mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F046` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S34`; depends on `F045`.
- Planned backend contract: `B038`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews root/child category administration and grouped storefront navigation before the hierarchy migration exists.

## Files expected to change

`contracts/categoryHierarchyContract.ts`, `clients/ShopCategoryClient.ts`, mock client, provider extension, CategoriesPage hierarchy editor, storefront navigation and breadcrumb.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B038` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const adminCategorySchema = z.object({ id: z.string(), tenantId: z.string(), name: z.string(), slug: z.string(), displayOrder: z.number().int(), isActive: z.boolean(), parentCategoryId: z.string().nullable() })
export type PublicCategory = { id: string; name: string; slug: string; displayOrder: number; children: PublicCategory[] }
export const publicCategorySchema: z.ZodType<PublicCategory> = z.lazy(() => z.object({ id: z.string(), name: z.string(), slug: z.string(), displayOrder: z.number().int(), children: z.array(publicCategorySchema) }))
export type SaveCategoryRequest = { name: string; slug: string; displayOrder: number; isActive: boolean; parentCategoryId: string | null }
export interface ShopCategoryClient { listAdmin(tenantId: string, signal?: AbortSignal): Promise<AdminCategory[]>; create(tenantId: string, body: Omit<SaveCategoryRequest,'isActive'>, signal?: AbortSignal): Promise<AdminCategory>; update(tenantId: string, categoryId: string, body: SaveCategoryRequest, signal?: AbortSignal): Promise<AdminCategory>; listPublic(tenantId: string, signal?: AbortSignal): Promise<PublicCategory[]> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend provider with `categories`. Admin rows indent direct children and parent selector lists active roots only. Mock invalid third level, inactive parent and parent-with-children reparent conflict using the same field/error shape as B038. Storefront groups child links below roots and renders a maximum two-level breadcrumb. No arbitrary tree component.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Create/edit root and child; invalid depth/conflict; root deactivation visual; mobile grouped navigation and RTL breadcrumb.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock categories client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B038` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
