# F054 — Bind product media client to B036 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F054` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F044**.
- Slice: `S32`; depends on `F053, B036`.
- Backend source of truth: `B036` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before starting, follow the boilerplate in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `S32` slice file, read the paired backend task `B036` and its delivered contract, and follow the existing branch-naming/ledger-update rules. Do not skip those — this checklist only covers the coding steps.

1. Open `docs/design/shop/http-contracts.md` and find the B036 section. Compare every route, field name, and status code listed there against the delivered C# request/response records and B036's integration tests. If anything differs from what this Spec below describes, stop, write down the exact mismatch, and ask for a contract correction decision before writing any code.
2. Open (or create) `clients/httpShopMediaClient.ts`. This is the only client file you should need to touch for this task.
3. In that file, define a helper function `productPath(tenantId: string, productId: string)` that returns the string `` `/api/tenants/${encodeURIComponent(tenantId)}/shop/products/${encodeURIComponent(productId)}` ``. Every path segment must be passed through `encodeURIComponent`.
4. Create an object named `httpShopMediaClient` that implements the existing `ShopMediaClient` interface (the same interface the mock implementation already implements). It must have exactly these four methods, with exactly this behavior:
   - `getProduct(tenantId, productId, signal)`: call `shopFetch(productPath(tenantId, productId), { signal })`, then parse the JSON result with `shopProductWithGallerySchema.parse(...)` and return that. (`shopFetch` is the repo's existing helper that performs authenticated `fetch` calls and already exists — do not write your own `fetch` wrapper.)
   - `getProtectedContent(tenantId, productId, imageId, signal)`: call `shopFetchBlob` (the existing helper for fetching binary/blob responses) against `` `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}/content` ``, passing `{ signal }`, and return its result directly.
   - `upload(tenantId, productId, file, altText, expectedGalleryVersion, signal)`: build a `FormData` object (`body`), set `body.set('file', file)`, `body.set('altText', altText)`, and `body.set('expectedGalleryVersion', String(expectedGalleryVersion))`. POST it to `` `${productPath(tenantId, productId)}/images` `` via `shopFetch(url, { method: 'POST', body, signal })`, then parse the result with `productGallerySchema.parse(...)` and return it.
   - `reorder(tenantId, productId, body, signal)`: PUT `body` as JSON to `` `${productPath(tenantId, productId)}/images/order` `` via `shopFetch(url, { method: 'PUT', json: body, signal })`, then parse the result with `productGallerySchema.parse(...)` and return it.
   - `remove(tenantId, productId, imageId, expectedGalleryVersion, signal)`: build a `URLSearchParams` object with `{ expectedGalleryVersion: String(expectedGalleryVersion) }`, then DELETE `` `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}?${query}` `` via `shopFetch(url, { method: 'DELETE', signal })`. Do not parse a body from this call (the backend returns no content on success).
5. Reference implementation (copy/adapt this exactly, it is the complete required code):

```ts
const productPath = (tenantId: string, productId: string) =>
  `/api/tenants/${encodeURIComponent(tenantId)}/shop/products/${encodeURIComponent(productId)}`

export const httpShopMediaClient: ShopMediaClient = {
  async getProduct(tenantId, productId, signal) {
    return shopProductWithGallerySchema.parse(
      await shopFetch(productPath(tenantId, productId), { signal }),
    )
  },
  async getProtectedContent(tenantId, productId, imageId, signal) {
    return shopFetchBlob(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}/content`,
      { signal },
    )
  },
  async upload(tenantId, productId, file, altText, expectedGalleryVersion, signal) {
    const body = new FormData()
    body.set('file', file)
    body.set('altText', altText)
    body.set('expectedGalleryVersion', String(expectedGalleryVersion))
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images`, { method: 'POST', body, signal },
    ))
  },
  async reorder(tenantId, productId, body, signal) {
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images/order`, { method: 'PUT', json: body, signal },
    ))
  },
  async remove(tenantId, productId, imageId, expectedGalleryVersion, signal) {
    const query = new URLSearchParams({ expectedGalleryVersion: String(expectedGalleryVersion) })
    await shopFetch(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}?${query}`,
      { method: 'DELETE', signal },
    )
  },
}
```

6. Use the existing authenticated `shopFetch`/token behavior for every admin route (all routes in this task are admin routes). If a route were a public storefront route, it would instead use plain anonymous requests — this task has none, but keep that rule in mind for later tasks.
7. Make sure every path segment (tenant ID, product ID, image ID) is passed through `encodeURIComponent`, and every query string value is built with `URLSearchParams`.
8. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch`/`shopFetchBlob` call, unchanged, so callers can cancel in-flight requests.
9. Parse every successful JSON response through the Zod schema already created in task `F044` (a Zod schema is a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/` — reuse the existing ones, do not invent new schemas). Do not add new fields or rename any existing schema field.
10. Wrap failures so that HTTP status codes 400, 403, 409, 413, and 415 are all normalized into the existing `ShopClientError` type (the repo's shared error type for normalized HTTP failures) — do one normalization step for all of them, do not write per-status-code branches unless `ShopClientError` construction requires it.
11. In whatever code manages object URLs for image previews (the "protected content" blob returned by `getProtectedContent`), make sure every blob URL created with `URL.createObjectURL` is later revoked with `URL.revokeObjectURL` when it is no longer needed, so memory is not leaked.
12. Open the Shop client composition file (where `createShopClients()` is defined). Find the `media` slot. Change it from the mock media client to `httpShopMediaClient`. Do not touch any other slot.
13. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
14. Confirm the expected diff is limited to `clients/httpShopMediaClient.ts` and the Shop client composition file. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
15. Run the regression and browser proof steps in the "Regression and browser proof" section below.
16. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`clients/httpShopMediaClient.ts` and Shop client composition only; component edits require a documented contract defect.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Implement the existing admin product-detail read extended by B036, protected
content/blob preview, multipart upload, reorder and delete routes exactly as
B036. Parse JSON through F044 schemas; map 400/403/409/413/415 into
`ShopClientError`; revoke blob URLs. Switch only `media` from mock to HTTP.

```ts
const productPath = (tenantId: string, productId: string) =>
  `/api/tenants/${encodeURIComponent(tenantId)}/shop/products/${encodeURIComponent(productId)}`

export const httpShopMediaClient: ShopMediaClient = {
  async getProduct(tenantId, productId, signal) {
    return shopProductWithGallerySchema.parse(
      await shopFetch(productPath(tenantId, productId), { signal }),
    )
  },
  async getProtectedContent(tenantId, productId, imageId, signal) {
    return shopFetchBlob(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}/content`,
      { signal },
    )
  },
  async upload(tenantId, productId, file, altText, expectedGalleryVersion, signal) {
    const body = new FormData()
    body.set('file', file)
    body.set('altText', altText)
    body.set('expectedGalleryVersion', String(expectedGalleryVersion))
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images`, { method: 'POST', body, signal },
    ))
  },
  async reorder(tenantId, productId, body, signal) {
    return productGallerySchema.parse(await shopFetch(
      `${productPath(tenantId, productId)}/images/order`, { method: 'PUT', json: body, signal },
    ))
  },
  async remove(tenantId, productId, imageId, expectedGalleryVersion, signal) {
    const query = new URLSearchParams({ expectedGalleryVersion: String(expectedGalleryVersion) })
    await shopFetch(
      `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}?${query}`,
      { method: 'DELETE', signal },
    )
  },
}
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal (the object used to cancel an in-flight `fetch`; the repo's `shopFetch` already accepts it as `{ signal }`). Parse every successful JSON response through the schema created in `F044`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `media` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F044` against the real backend. Concretely: for each state F044 demonstrated (empty gallery, populated gallery, upload success, upload failure, reorder, delete, protected-image preview, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: upload or reorder an image, reload the page, and confirm the change survived; then trigger one 403/409/413/415 case and confirm it surfaces as a normal error via `ShopClientError`, not a crash or silent fallback.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm the same Zod schema (`shopProductWithGallerySchema`/`productGallerySchema`) parses both the old mock data and the new real backend data without modification.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `media` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
</content>
</invoke>
