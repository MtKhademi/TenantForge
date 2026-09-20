# F044 — Build contract-shaped product gallery and storefront image mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F044` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S32`; depends on `F043`.
- Planned backend contract: `B036`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user can review product gallery administration, product-card thumbnails and product-detail galleries entirely in the browser before media APIs exist.

## Files expected to change

`features/shop/contracts/mediaContract.ts`, `clients/ShopMediaClient.ts`, `clients/mockShopMediaClient.ts`, `ShopClientsProvider.tsx`, `ProductGalleryEditor.tsx`, current product admin/storefront pages.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B036` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

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
`getProtectedContent` maps to B036's authenticated byte route. Complete every
nested schema without `any`, unchecked casts or duplicated competing types.

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
