# F046 — Build contract-shaped nested category mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F046` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S34`; depends on `F045`.
- Planned backend contract: `B038`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews root/child category administration and grouped storefront navigation before the hierarchy migration exists.

## Do this in order

Before step 1, follow the standing rules already described in the "Ownership,
phase and dependencies" section above: read `AGENTS.md`, read the linked
slice file (`S34`), read the paired backend contract Spec `B038`, and follow
the branch-naming and ledger-update rules from `AGENTS.md`'s Ownership
section. Do not skip those just because they are not repeated below.

1. Open `docs/design/shop/http-contracts.md` and find the section for `B038`. Note every field name, type and nullability it defines — copy these exactly, do not rename or reshape them.
2. Create `contracts/categoryHierarchyContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, already used elsewhere in `features/shop/contracts/`.)
3. Define `adminCategorySchema` exactly as shown below, with fields `id`, `tenantId`, `name`, `slug`, `displayOrder`, `isActive`, `parentCategoryId` (nullable).
4. Define the recursive `PublicCategory` type and its matching `publicCategorySchema` exactly as shown below. Note the schema uses `z.lazy(...)` because the type refers to itself (a category's `children` array holds more `PublicCategory` values) — copy the `z.lazy` pattern exactly, do not flatten it.
5. Define `SaveCategoryRequest` exactly as shown below, with fields `name`, `slug`, `displayOrder`, `isActive`, `parentCategoryId`.
6. Create `features/shop/clients/ShopCategoryClient.ts`. Define the `ShopCategoryClient` TypeScript interface exactly as shown below, with four methods: `listAdmin`, `create`, `update`, `listPublic`. Every method's last parameter is `signal?: AbortSignal` (abort-aware: cancel in-flight work when the caller cancels). `create`'s body type is `Omit<SaveCategoryRequest,'isActive'>` — meaning `create` never accepts `isActive` as input; a newly created category's active state is decided elsewhere (default it to `true` in the mock unless the backend Spec `B038` says otherwise).
7. Create the mock client (place it beside the other mock clients under `features/shop/clients/`, named for this feature, e.g. `mockShopCategoryClient.ts`) implementing `ShopCategoryClient` fully — no method left unimplemented, no `any`, no unchecked casts. Give it deterministic, seeded fixture data with at least: some root categories, some categories with children, one inactive root, and one category already at the maximum allowed nesting depth of two levels (root + one child level only).
8. Simulate network latency in the mock client using the shared abort-aware delay helper already used by other mock clients in `features/shop/clients/`. Reuse that helper; do not write a new one.
9. Add named, in-memory scenarios in the mock client for: attempting to create a third nesting level (invalid — a category may only be a root or a direct child of a root, never a grandchild), attempting to reparent a category under an inactive parent (invalid), and attempting to reparent a category that itself has children (conflict). Every one of these three mock error responses must use the same field name and error-code/status shape that `B038` defines — do not invent your own error shape.
10. Wrap any scenario-selection UI in a check on `import.meta.env.DEV`, so it is stripped from the production build. Verify a production build (`npm run build`) contains no such toolbar and no task ID shown anywhere in production-rendered text.
11. Extend `ShopClientsProvider`'s exported `ShopClients` object with a new `categories` slot wired to the mock category client for this task.
12. In the admin `CategoriesPage` hierarchy editor: render direct children visually indented under their parent root. In the parent-selector control (used when creating/editing a category), list only active root categories as selectable parents — never list a child category or an inactive category as a selectable parent.
13. Do not build or import any general-purpose/arbitrary tree UI component for this. Build a simple two-level list (roots, each with an indented list of its direct children) — the data is never more than two levels deep.
14. In the storefront navigation, group each root category's child links directly below that root.
15. In the storefront breadcrumb, render at most two levels (root, then child) — never render a third level, since the data model itself never has one.
16. Consume the `categories` client only through `ShopClientsProvider` in every component you touch. Never import mock fixtures directly into a component, and never call `fetch` anywhere in this task.
17. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Never show a success message the mock did not actually send.
18. Run `npm run build` and `npm run lint`; fix all errors before moving on.
19. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence, including mobile grouped navigation and RTL (right-to-left) breadcrumb rendering.
20. Report the exact line: `Data source: mock categories client; HTTP integration deferred to the matching F054–F063 task.`
21. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

`contracts/categoryHierarchyContract.ts`, `clients/ShopCategoryClient.ts`, mock client, provider extension, CategoriesPage hierarchy editor, storefront navigation and breadcrumb.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B038` exactly. Mock IDs are canonical 13-character TSID strings (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"; treat it as an opaque 13-character string). Timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const adminCategorySchema = z.object({ id: z.string(), tenantId: z.string(), name: z.string(), slug: z.string(), displayOrder: z.number().int(), isActive: z.boolean(), parentCategoryId: z.string().nullable() })
export type PublicCategory = { id: string; name: string; slug: string; displayOrder: number; children: PublicCategory[] }
export const publicCategorySchema: z.ZodType<PublicCategory> = z.lazy(() => z.object({ id: z.string(), name: z.string(), slug: z.string(), displayOrder: z.number().int(), children: z.array(publicCategorySchema) }))
export type SaveCategoryRequest = { name: string; slug: string; displayOrder: number; isActive: boolean; parentCategoryId: string | null }
export interface ShopCategoryClient { listAdmin(tenantId: string, signal?: AbortSignal): Promise<AdminCategory[]>; create(tenantId: string, body: Omit<SaveCategoryRequest,'isActive'>, signal?: AbortSignal): Promise<AdminCategory>; update(tenantId: string, categoryId: string, body: SaveCategoryRequest, signal?: AbortSignal): Promise<AdminCategory>; listPublic(tenantId: string, signal?: AbortSignal): Promise<PublicCategory[]> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types. (Note: the interface above refers to `AdminCategory` as a type name — export it as `z.infer<typeof adminCategorySchema>` alongside the schema.)

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
- [ ] Write/verify a scenario creating a root category — assert it appears at the top level with no indentation.
- [ ] Write/verify a scenario creating a child category under a root — assert it renders indented under that root.
- [ ] Write/verify a scenario attempting a third nesting level (child-of-a-child) — assert the exact `B038` validation error shape renders and the invalid category is not created.
- [ ] Write/verify a scenario reparenting a category under an inactive parent — assert the exact `B038` error shape renders and the reparent is rejected.
- [ ] Write/verify a scenario reparenting a category that has its own children — assert the exact `B038` conflict shape renders and the reparent is rejected.
- [ ] Write/verify a scenario deactivating a root category — assert its visual state changes (e.g., dimmed/marked inactive) and it no longer appears in the parent selector.
- [ ] Write/verify the parent selector only ever lists active root categories, never a child or inactive category.
- [ ] Write/verify the storefront navigation groups each root's children directly below it.
- [ ] Write/verify the storefront breadcrumb never renders more than two levels.
- [ ] Write/verify mobile grouped navigation and RTL breadcrumb rendering at the required viewports.
