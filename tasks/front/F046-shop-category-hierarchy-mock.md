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
2. Create `src/web/src/features/shop/contracts/categoryHierarchyContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`.)
3. Define `adminCategorySchema` exactly as shown below, with fields `id`, `tenantId`, `name`, `slug`, `displayOrder`, `isActive`, `parentCategoryId` (nullable).
4. Define the recursive `PublicCategory` type and its matching `publicCategorySchema` exactly as shown below. Note the schema uses `z.lazy(...)` because the type refers to itself (a category's `children` array holds more `PublicCategory` values) — copy the `z.lazy` pattern exactly, do not flatten it.
5. Define `SaveCategoryRequest` exactly as shown below, with fields `name`, `slug`, `displayOrder`, `isActive`, `parentCategoryId`.
6. Create `src/web/src/features/shop/clients/ShopCategoryClient.ts`. Define the `ShopCategoryClient` TypeScript interface exactly as shown below, with four methods: `listAdmin`, `create`, `update`, `listPublic`. Every method's last parameter is `signal?: AbortSignal` (abort-aware: cancel in-flight work when the caller cancels). `create`'s body type is `Omit<SaveCategoryRequest,'isActive'>` — meaning `create` never accepts `isActive` as input; a newly created category's active state is decided elsewhere (default it to `true` in the mock unless the backend Spec `B038` says otherwise).
7. Create `src/web/src/features/shop/clients/mockShopCategoryClient.ts` implementing `ShopCategoryClient` fully — no method left unimplemented, no `any`, no unchecked casts. Give it deterministic, seeded fixture data with at least: some root categories, some categories with children, one inactive root, and one category already at the maximum allowed nesting depth of two levels (root + one child level only).
8. Simulate network latency in the mock client by awaiting the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`. Use that one helper; never call `setTimeout` directly in a mock client.
9. Add named, in-memory scenarios in the mock client for: attempting to create a third nesting level (invalid — a category may only be a root or a direct child of a root, never a grandchild), attempting to reparent a category under an inactive parent (invalid), and attempting to reparent a category that itself has children (conflict). Every one of these three mock error responses must use the same field name and error-code/status shape that `B038` defines — do not invent your own error shape.
10. Wrap any scenario-selection UI in a check on `import.meta.env.DEV`, so it is stripped from the production build. Verify a production build (`cd src/web && npm run build`) contains no such toolbar and no task ID shown anywhere in production-rendered text.
11. Add a `categories` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to your mock category client. Do not touch any other slot.
12. In `src/web/src/pages/shop/admin/CategoriesPage.tsx` build the hierarchy editor: render direct children visually indented under their parent root. In the parent-selector control (used when creating/editing a category), list only active root categories as selectable parents — never list a child category or an inactive category as a selectable parent.
13. Do not build or import any general-purpose/arbitrary tree UI component for this. Build a simple two-level list (roots, each with an indented list of its direct children) — the data is never more than two levels deep.
14. In `src/web/src/pages/shop/storefront/StorefrontLayout.tsx` (the storefront navigation), group each root category's child links directly below that root.
15. In the storefront breadcrumb (rendered from `src/web/src/pages/shop/storefront/CategoryPage.tsx`), render at most two levels (root, then child) — never render a third level, since the data model itself never has one.
16. Consume the `categories` client only through `ShopClientsProvider` in every component you touch. Never import mock fixtures directly into a component, and never call `fetch` anywhere in this task.
17. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Never show a success message the mock did not actually send.
18. Run `cd src/web && npm run build`, then `cd src/web && npm run lint`; fix every error before moving on.
19. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence, including mobile grouped navigation and RTL (right-to-left) breadcrumb rendering.
20. Report the exact line: `Data source: mock categories client; HTTP integration deferred to the matching F054–F063 task.`
21. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/categoryHierarchyContract.ts`
- `src/web/src/features/shop/clients/ShopCategoryClient.ts`
- `src/web/src/features/shop/clients/mockShopCategoryClient.ts`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `categories` slot)
- `src/web/src/pages/shop/admin/CategoriesPage.tsx`
- `src/web/src/pages/shop/storefront/StorefrontLayout.tsx`
- `src/web/src/pages/shop/storefront/CategoryPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

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

Build every one of these. "State" means something the user can actually see on
screen, not a code path.

- **idle / initial** — before anything is requested.
- **loading** — visible progress, with no destructive layout shift (the page
  must not jump or reflow when loading finishes).
- **success** — the normal populated result.
- **empty** — a successful response that contains no items. This is not an
  error; it must not look like one.
- **each error this capability actually defines.** For this task those are:
  `400` (parent validation failed: a third nesting level, an inactive parent, a foreign-tenant parent, or self-parent — all four use the same `parentCategoryId` field error), `403` (the user lacks `Shop.Catalog.Manage`), `404` (category not found) and `409` (reparenting a category that already has children). Each needs its own message — do not collapse them into one generic
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

Create/edit root and child; invalid depth/conflict; root deactivation visual; mobile grouped navigation and RTL breadcrumb.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock categories client; HTTP integration deferred to the matching F054–F063 task.`

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
