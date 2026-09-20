# F044 — Build contract-shaped product gallery and storefront image mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F044` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S32`; depends on `F043`.
- Planned backend contract: `B036`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user can review product gallery administration, product-card thumbnails and product-detail galleries entirely in the browser before media APIs exist.

## Do this in order

Before step 1, follow the standing rules already described in the "Ownership,
phase and dependencies" section above: read `AGENTS.md`, read the linked
slice file (`S32`), read the paired backend contract Spec `B036`, and follow
the branch-naming and ledger-update rules from `AGENTS.md`'s Ownership
section. Do not skip those just because they are not repeated below.

1. Open `docs/design/shop/http-contracts.md` and find the section for `B036`. Note every field name, type and nullability it defines — you will copy these exactly, not rename or reshape them.
2. In `features/shop/contracts/mediaContract.ts`, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, already used elsewhere in `features/shop/contracts/` — you define a `z.object({...})` and derive the TypeScript type from it with `z.infer<typeof ...>`, you do not write a separate hand-written interface that could drift from it.)
3. In the same file, find the existing `ShopProduct` type in `shopCatalogTypes.ts` (delivered by prior task `B026`). Move its field-for-field definition behind one new `shopProductSchema` Zod schema, and make the existing `ShopProduct` type equal `z.infer<typeof shopProductSchema>`. Do not create a second, competing product type.
4. Still in `mediaContract.ts`, define `shopProductWithGallerySchema` as `shopProductSchema.extend({ images: z.array(productImageSchema), galleryVersion: z.number().int() })`, exactly as shown below. Export `ShopProductWithGallery` as its inferred type.
5. Create `features/shop/clients/ShopMediaClient.ts`. In it, define the `ShopMediaClient` TypeScript interface exactly as shown in "Required contract/code shape" below, with all five methods: `getProduct`, `getProtectedContent`, `upload`, `reorder`, `remove`. Every method's last parameter is `signal?: AbortSignal` (this makes the method "abort-aware": if the caller cancels, in-flight work should stop instead of updating stale UI).
6. Create `features/shop/clients/mockShopMediaClient.ts`. It must implement `ShopMediaClient` fully (no method left unimplemented, no `any`, no unchecked `as` casts). Give it deterministic, seeded fixture data — the same scenario always returns the same result — and simulate network latency using the shared abort-aware delay helper already used by other mock clients in `features/shop/clients/`. Reuse that existing helper; do not write a new one.
7. In the mock client, add named, in-memory scenarios (plain exported constants or a switch on a scenario key) that can each be selected during development. These scenario names must never appear in any production-rendered text or DOM node.
8. Wrap any scenario-selection UI (a dev-only toolbar or dropdown) in a check on `import.meta.env.DEV`, so it renders in local development only and is stripped from the production build. Verify a production build (`npm run build`) contains no such toolbar.
9. Update `ShopClientsProvider.tsx` to add a new `media` slot on the shared clients object, wired to `mockShopMediaClient` for this task (a later task swaps in the real HTTP client behind the same `ShopMediaClient` interface — do not build that HTTP client now).
10. In `ProductGalleryEditor.tsx` (create it if it does not exist yet) and the current product admin/storefront pages, consume the media client only through `ShopClientsProvider`. Never import the mock fixtures directly into a component, and never call `fetch` anywhere in this task.
11. Implement the eight-image cap: once a product has 8 images, disable/hide the upload control and show a message that the limit is reached. Do not allow a ninth image in any mock scenario.
12. Make the first image in display order the primary image everywhere it is shown (card thumbnail and gallery cover).
13. Add an accessible alt-text field per image, plus accessible (keyboard-operable, labeled) "move earlier", "move later" and "remove" controls for each image.
14. Whenever you create an object URL (`URL.createObjectURL`) for a local file preview, revoke it with `URL.revokeObjectURL` when the component unmounts or the preview is replaced, so you do not leak memory.
15. Build thumbnail selection (clicking/activating a thumbnail changes the shown detail image) and make the gallery layout responsive at the three required viewports (see "Browser evidence and validation").
16. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Do not show a success message unless the mock genuinely returned success — never invent server confirmation the mock did not send.
17. Run `npm run build` and `npm run lint`; fix all errors before moving on.
18. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence.
19. Report the exact line: `Data source: mock media client; HTTP integration deferred to the matching F054–F063 task.`
20. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

`features/shop/contracts/mediaContract.ts`, `clients/ShopMediaClient.ts`, `clients/mockShopMediaClient.ts`, `ShopClientsProvider.tsx`, `ProductGalleryEditor.tsx`, current product admin/storefront pages.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B036` exactly. Mock IDs are canonical 13-character TSID strings (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"; treat it as an opaque 13-character string, do not generate it yourself with a different format). Timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const productImageSchema = z.object({ id: z.string(), altText: z.string(), displayOrder: z.number().int(), width: z.number().int(), height: z.number().int(), contentUrl: z.string() })
export const productGallerySchema = z.object({ images: z.array(productImageSchema), galleryVersion: z.number().int() })
export type ProductGallery = z.infer<typeof productGallerySchema>
export const shopProductWithGallerySchema = shopProductSchema.extend({
  images: z.array(productImageSchema),
  galleryVersion: z.number().int(),
})
export type ShopProductWithGallery = z.infer<typeof shopProductWithGallerySchema>
export type ReorderProductImagesRequest = { imageIds: string[]; expectedGalleryVersion: number }
export interface ShopMediaClient { getProduct(tenantId: string, productId: string, signal?: AbortSignal): Promise<ShopProductWithGallery>; getProtectedContent(tenantId: string, productId: string, imageId: string, signal?: AbortSignal): Promise<Blob>; upload(tenantId: string, productId: string, file: File, altText: string, expectedGalleryVersion: number, signal?: AbortSignal): Promise<ProductGallery>; reorder(tenantId: string, productId: string, body: ReorderProductImagesRequest, signal?: AbortSignal): Promise<ProductGallery>; remove(tenantId: string, productId: string, imageId: string, expectedGalleryVersion: number, signal?: AbortSignal): Promise<void> }
```

`ShopProduct` is the already delivered B026 contract from
`shopCatalogTypes.ts`. Move its field-for-field definition behind one
`shopProductSchema` and infer the existing type from that schema, then extend it
with B036's `images` and `galleryVersion`; do not create a competing product
DTO. `getProduct` maps to the existing product-detail route extended by B036, and
`getProtectedContent` maps to B036's authenticated byte route (a route that
returns raw image bytes only to an authenticated, authorized caller, rather
than a public static URL). Complete every nested schema without `any`,
unchecked casts or duplicated competing types — meaning: every field of every
type referenced above must be written out in full in your code, none left as
a placeholder or `TODO`.

## Required UI implementation

Create the incremental `ShopClientsProvider` with a `media` slot backed by `mockShopMediaClient`. Mock empty, populated, eight-image limit, upload progress/failure, stale version and broken-image states. First ordered image is primary. Include alt text, accessible earlier/later/remove controls, object-URL cleanup, thumbnail selection and responsive gallery. No `fetch` anywhere in this task.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Admin empty/populated/error/eight-image states; keyboard reordering; storefront card/detail/broken fallback at 1440×900, 1024×768 and 390×844.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock media client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B036` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify a scenario for the empty gallery: no images uploaded yet — assert the empty state renders, not a broken gallery.
- [ ] Write/verify a scenario for a populated gallery — assert images render in `displayOrder`, the first one marked/styled as primary.
- [ ] Write/verify a scenario at exactly 8 images — assert the upload control is disabled/hidden and no 9th upload is possible.
- [ ] Write/verify a scenario for upload progress — assert a visible in-progress indicator appears during the mocked delay.
- [ ] Write/verify a scenario for upload failure — assert an error state renders and the gallery is not left in a partially-updated state.
- [ ] Write/verify a scenario for a stale `galleryVersion` (409-style conflict) — assert the UI tells the user to refresh rather than silently overwriting.
- [ ] Write/verify a scenario for a broken image URL — assert a fallback placeholder renders instead of a broken `<img>`.
- [ ] Write/verify keyboard-only reordering (earlier/later controls) — assert focus stays sensible and the order updates.
