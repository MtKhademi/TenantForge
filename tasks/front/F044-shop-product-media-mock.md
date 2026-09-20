# F044 — Build contract-shaped product gallery and storefront image mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F044` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S32`; depends on `F043`.
- Planned backend contract: `B036`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user can review product gallery administration, product-card thumbnails and product-detail galleries entirely in the browser before media APIs exist.

## Build the shared Shop client seam first (this task creates it)

**Read this before step 1.** `F045`–`F063` all say "reuse the shared helper
from `F044`". Those helpers do **not** exist in the repository yet — this
task creates every one of them. Today `src/web/src/features/shop/` contains
only flat files (`shopCatalogAdapter.ts`, `cartAdapter.ts`, `storefrontTypes.ts`
and friends, all directly under `src/web/src/features/shop/`) that call `fetch` directly; there is no `contracts/` folder, no
`clients/` folder, no `ShopClientsProvider`, no `shopFetch`, no shared delay
helper and no `ShopClientError`. Do not go looking for them and do not
conclude the task is blocked when you cannot find them.

Create exactly these three files, with exactly this content, before you do
anything else. Every path below is written in full from the repository root.

### 1. `src/web/src/features/shop/contracts/shopContract.ts`

```ts
import { z } from 'zod'

/**
 * Mirrors C# `PaginationMetadata` in
 * src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationMetadata.cs
 * field for field. It has SIX fields, not four.
 */
export const paginationSchema = z.object({
  pageNumber: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
  hasPreviousPage: z.boolean(),
  hasNextPage: z.boolean(),
})
export type Pagination = z.infer<typeof paginationSchema>

/** A page request every paginated Shop client method takes. */
export type PageQuery = { pageNumber: number; pageSize: number }

/**
 * RFC7807 = the standard JSON error body ASP.NET Core returns for a failed
 * request (`status`, `type`, `title`, `detail`, optional per-field `errors`).
 * This is the ONLY error body shape any Shop client parses.
 */
export const shopProblemSchema = z.object({
  status: z.number().int(),
  type: z.string().optional(),
  title: z.string().optional(),
  detail: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
  retryAfterSeconds: z.number().int().positive().optional(),
})
export type ShopProblem = z.infer<typeof shopProblemSchema>

/** The ONE error type every Shop client throws. Never add a second one. */
export class ShopClientError extends Error {
  constructor(readonly problem: ShopProblem) {
    super(problem.detail ?? problem.title ?? 'Shop request failed')
    this.name = 'ShopClientError'
  }
}
```

### 2. `src/web/src/features/shop/clients/shopFetch.ts`

```ts
import { ShopClientError, shopProblemSchema } from '../contracts/shopContract'

type ShopFetchInit = RequestInit & { json?: unknown }

/** Throws ShopClientError for any non-2xx response. Shared by every client. */
async function failed(response: Response): Promise<never> {
  const raw: unknown = await response.json().catch(() => ({}))
  const body = typeof raw === 'object' && raw !== null ? raw : {}
  throw new ShopClientError(shopProblemSchema.parse({ ...body, status: response.status }))
}

function build(init: ShopFetchInit): RequestInit {
  const { json, ...rest } = init
  if (json === undefined) return rest
  return {
    ...rest,
    body: JSON.stringify(json),
    headers: { ...(rest.headers ?? {}), 'Content-Type': 'application/json' },
  }
}

/** Authenticated JSON call. Attaches the current bearer token. */
export async function shopFetch(path: string, init: ShopFetchInit = {}): Promise<unknown> {
  const response = await fetch(path, build(init))
  if (!response.ok) return failed(response)
  if (response.status === 204) return undefined
  return response.json()
}

/** Anonymous JSON call for public storefront routes. Sends no token. */
export async function shopFetchPublic(path: string, init: ShopFetchInit = {}): Promise<unknown> {
  const response = await fetch(path, build(init))
  if (!response.ok) return failed(response)
  if (response.status === 204) return undefined
  return response.json()
}

/** Authenticated call that returns raw bytes instead of JSON. */
export async function shopFetchBlob(path: string, init: ShopFetchInit = {}): Promise<Blob> {
  const response = await fetch(path, build(init))
  if (!response.ok) return failed(response)
  return response.blob()
}

/**
 * Abort-aware delay. Every mock client simulates latency through THIS
 * function and never through a bare `setTimeout`, so a cancelled request
 * rejects immediately instead of resolving into stale UI.
 */
export function delay(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const id = window.setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    function onAbort() {
      window.clearTimeout(id)
      reject(new DOMException('Aborted', 'AbortError'))
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}
```

Members you must still complete yourself, each with exactly this name and
behaviour:

- Token attachment inside `shopFetch` and `shopFetchBlob`. Read the current
  access token the same way `src/web/src/features/shop/shopCatalogAdapter.ts` already
  does (it takes an `accessToken` and sends `Authorization: Bearer <token>`)
  and set that header on the request. Do not invent a second token store.
- The 8-second request timeout `shopCatalogAdapter.ts` already applies
  (`REQUEST_TIMEOUT_MS`). Combine it with the caller's `signal` so either one
  can abort the request.

### 3. `src/web/src/features/shop/clients/ShopClientsProvider.tsx`

This is the composition seam. It exports the `ShopClients` object, the
`createShopClients()` factory that `F054`–`F063` each edit one slot of, a
React provider, and the `useShopClients()` hook components call.

```tsx
import { createContext, useContext, useMemo, type ReactNode } from 'react'

/**
 * One slot per Shop capability. The slot names below are FIXED — F045..F063
 * refer to them by these exact keys. Each task adds its own slot and leaves
 * every other slot alone.
 */
export type ShopClients = {
  media: ShopMediaClient // F044 mock  -> F054 HTTP
  // F045 adds:  discovery: ShopDiscoveryClient        -> F055 HTTP
  // F046 adds:  categories: ShopCategoryClient        -> F056 HTTP
  // F047 adds:  profile: ShopProfileClient            -> F057 HTTP
  // F048 adds:  cartLease: ShopCartLeaseClient        -> F058 HTTP
  // F049 adds:  coupons: ShopCouponClient             -> F059 HTTP
  // F050 adds:  orders: ShopOrdersClient              -> F060 HTTP
  // F051 adds:  orderOperations: ShopOrderOperationsClient -> F061 HTTP
  // F052 adds:  payments: ShopPaymentsClient          -> F062 HTTP
}

export function createShopClients(): ShopClients {
  return { media: mockShopMediaClient }
}

const ShopClientsContext = createContext<ShopClients | null>(null)

export function ShopClientsProvider({
  clients,
  children,
}: {
  clients?: ShopClients
  children: ReactNode
}) {
  const value = useMemo(() => clients ?? createShopClients(), [clients])
  return <ShopClientsContext.Provider value={value}>{children}</ShopClientsContext.Provider>
}

export function useShopClients(): ShopClients {
  const clients = useContext(ShopClientsContext)
  if (!clients) throw new Error('useShopClients must be used inside <ShopClientsProvider>')
  return clients
}
```

Add the imports for `ShopMediaClient` and `mockShopMediaClient` yourself, from
the two files you create in steps 5 and 6 below.

### 4. Mount the provider

In `src/web/src/App.tsx`, wrap the whole `<Routes>` element in `<ShopClientsProvider>`
so both the storefront routes (`/shop/:tenantId/*`) and the tenant admin routes
(`/t/:tenantId/shop/*`) can call `useShopClients()`. Do not add a second
provider anywhere else.

### 5. Leave the existing adapters alone

`shopCatalogAdapter.ts`, `cartAdapter.ts`, `checkoutAdapter.ts`,
`orderAdapter.ts`, `orderLookupAdapter.ts`, `shippingAndCouponAdapter.ts` and
`storefrontAdapter.ts` keep working exactly as they do today. This task does
not migrate them. Only the new gallery/media behaviour goes through the new
seam.

## Do this in order

Before step 1, follow the standing rules already described in the "Ownership,
phase and dependencies" section above: read `AGENTS.md`, read the linked
slice file (`S32`), read the paired backend contract Spec `B036`, and follow
the branch-naming and ledger-update rules from `AGENTS.md`'s Ownership
section. Do not skip those just because they are not repeated below.

1. Open `docs/design/shop/http-contracts.md` and find the section for `B036`. Note every field name, type and nullability it defines — you will copy these exactly, not rename or reshape them. One known correction: that file's `PaginationMetadata` block lists only four fields; the real C# record has six (it also has `hasPreviousPage` and `hasNextPage`). Use the six-field version from the seam section above.
2. Do everything in the "Build the shared Shop client seam first" section above: create `src/web/src/features/shop/contracts/shopContract.ts`, `src/web/src/features/shop/clients/shopFetch.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, and mount the provider in `src/web/src/App.tsx`. Nothing later in this list works until these exist.
3. Create `src/web/src/features/shop/contracts/mediaContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator — you define a `z.object({...})` and derive the TypeScript type from it with `z.infer<typeof ...>`, you do not write a separate hand-written interface that could drift from it. Zod is already a dependency; `src/web/src/pages/shop/admin/ProductsPage.tsx` uses it for form validation.)
4. In the same file, copy the existing `ShopProduct` type from `src/web/src/features/shop/shopCatalogTypes.ts` (delivered by prior task `B026`) field for field into one new `shopProductSchema` Zod schema, then re-export `ShopProduct` from `shopCatalogTypes.ts` as `z.infer<typeof shopProductSchema>` so there is still exactly one `ShopProduct` type in the app. Do not create a second, competing product type, and do not change any field name or nullability while moving it.
5. Still in `mediaContract.ts`, define `shopProductWithGallerySchema` as `shopProductSchema.extend({ images: z.array(productImageSchema), galleryVersion: z.number().int() })`, exactly as shown below. Export `ShopProductWithGallery` as its inferred type.
6. Create `src/web/src/features/shop/clients/ShopMediaClient.ts`. In it, define the `ShopMediaClient` TypeScript interface exactly as shown in "Required contract/code shape" below, with all five methods: `getProduct`, `getProtectedContent`, `upload`, `reorder`, `remove`. Every method's last parameter is `signal?: AbortSignal` (this makes the method "abort-aware": if the caller cancels, in-flight work should stop instead of updating stale UI).
7. Create `src/web/src/features/shop/clients/mockShopMediaClient.ts`. It must implement `ShopMediaClient` fully (no method left unimplemented, no `any`, no unchecked `as` casts). Give it deterministic, seeded fixture data — the same scenario always returns the same result — and simulate network latency by awaiting the `delay(ms, signal)` helper you created in `src/web/src/features/shop/clients/shopFetch.ts`. Use that one helper everywhere; never call `setTimeout` directly in a mock client.
8. In the mock client, add named, in-memory scenarios (plain exported constants or a switch on a scenario key) that can each be selected during development. These scenario names must never appear in any production-rendered text or DOM node.
9. Wrap any scenario-selection UI (a dev-only toolbar or dropdown) in a check on `import.meta.env.DEV`, so it renders in local development only and is stripped from the production build. Verify a production build (`cd src/web && npm run build`) contains no such toolbar.
10. Confirm `createShopClients()` in `ShopClientsProvider.tsx` returns `{ media: mockShopMediaClient }`. `F054` later swaps that one value for the real HTTP client behind the same `ShopMediaClient` interface — do not build that HTTP client now.
11. Create `src/web/src/components/shop/ProductGalleryEditor.tsx` (the `components/shop/` folder does not exist yet — create it). Use it from `src/web/src/pages/shop/admin/ProductsPage.tsx`. Update `src/web/src/pages/shop/storefront/ProductDetailPage.tsx` and `src/web/src/pages/shop/storefront/CategoryPage.tsx` for the storefront gallery and card thumbnail. Every one of these gets the media client from `useShopClients()`. Never import the mock fixtures directly into a component, and never call `fetch` from a component in this task.
12. Implement the eight-image cap: once a product has 8 images, disable/hide the upload control and show a message that the limit is reached. Do not allow a ninth image in any mock scenario.
13. Make the first image in display order the primary image everywhere it is shown (card thumbnail and gallery cover).
14. Add an accessible alt-text field per image, plus accessible (keyboard-operable, labeled) "move earlier", "move later" and "remove" controls for each image.
15. Whenever you create an object URL (`URL.createObjectURL`) for a local file preview, revoke it with `URL.revokeObjectURL` when the component unmounts or the preview is replaced, so you do not leak memory.
16. Build thumbnail selection (clicking/activating a thumbnail changes the shown detail image) and make the gallery layout responsive at the three required viewports (see "Browser evidence and validation").
17. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Do not show a success message unless the mock genuinely returned success — never invent server confirmation the mock did not send.
18. Run the exact commands in "Browser evidence and validation" below (`cd src/web && npm run build`, then `cd src/web && npm run lint`); fix every error before moving on.
19. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through) and capture evidence.
20. Report the exact line: `Data source: mock media client; HTTP integration deferred to the matching F054–F063 task.`
21. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/shopContract.ts`
- `src/web/src/features/shop/contracts/mediaContract.ts`
- `src/web/src/features/shop/clients/shopFetch.ts`
- `src/web/src/features/shop/clients/ShopClientsProvider.tsx`
- `src/web/src/features/shop/clients/ShopMediaClient.ts`
- `src/web/src/features/shop/clients/mockShopMediaClient.ts`
- `src/web/src/components/shop/ProductGalleryEditor.tsx`

Edited by this task:

- `src/web/src/App.tsx` (mount `<ShopClientsProvider>`)
- `src/web/src/features/shop/shopCatalogTypes.ts` (re-export `ShopProduct` from the new schema)
- `src/web/src/pages/shop/admin/ProductsPage.tsx`
- `src/web/src/pages/shop/storefront/ProductDetailPage.tsx`
- `src/web/src/pages/shop/storefront/CategoryPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

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

Build every one of these. "State" means something the user can actually see on
screen, not a code path.

- **idle / initial** — before anything is requested.
- **loading** — visible progress, with no destructive layout shift (the page
  must not jump or reflow when loading finishes).
- **success** — the normal populated result.
- **empty** — a successful response that contains no items. This is not an
  error; it must not look like one.
- **each error this capability actually defines.** For this task those are:
  `400` (a field failed validation — e.g. missing alt text), `403` (the signed-in user lacks `Shop.Catalog.Manage`), `404` (product or image not found), `409` (stale `galleryVersion` — tell the user to refresh, never silently overwrite), `413` (the uploaded file is over 5 MiB) and `415` (not a JPEG/PNG/WebP, or a corrupt/animated image). Each needs its own message — do not collapse them into one generic
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

Admin empty/populated/error/eight-image states; keyboard reordering; storefront card/detail/broken fallback at 1440×900, 1024×768 and 390×844.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock media client; HTTP integration deferred to the matching F054–F063 task.`

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
