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
2. Create `src/web/src/features/shop/clients/httpShopMediaClient.ts`. This is the only client file you should need to touch for this task.
3. In that file, define a helper function `productPath(tenantId: string, productId: string)` that returns the string `` `/api/tenants/${encodeURIComponent(tenantId)}/shop/products/${encodeURIComponent(productId)}` ``. Every path segment must be passed through `encodeURIComponent`.
4. Create an object named `httpShopMediaClient` that implements the existing `ShopMediaClient` interface (the same interface the mock implementation already implements). It must have exactly these four methods, with exactly this behavior:
   - `getProduct(tenantId, productId, signal)`: call `shopFetch(productPath(tenantId, productId), { signal })`, then parse the JSON result with `shopProductWithGallerySchema.parse(...)` and return that. (`shopFetch` is the authenticated fetch helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` — do not write your own `fetch` wrapper.)
   - `getProtectedContent(tenantId, productId, imageId, signal)`: call `shopFetchBlob` (the binary/blob helper `F044` created in the same file) against `` `${productPath(tenantId, productId)}/images/${encodeURIComponent(imageId)}/content` ``, passing `{ signal }`, and return its result directly.
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
9. Parse every successful JSON response through the Zod schema already created in task `F044` (a Zod schema is a runtime validator plus TypeScript type generator; reuse the schemas the paired mock task already created under `src/web/src/features/shop/contracts/`, do not invent new ones). Do not add new fields or rename any existing schema field.
10. Wrap failures so that HTTP status codes 400, 403, 409, 413, and 415 are all normalized into the shared `ShopClientError` type from `src/web/src/features/shop/contracts/shopContract.ts` (created by `F044`) — do one normalization step for all of them, do not write per-status-code branches unless `ShopClientError` construction requires it.
11. In whatever code manages object URLs for image previews (the "protected content" blob returned by `getProtectedContent`), make sure every blob URL created with `URL.createObjectURL` is later revoked with `URL.revokeObjectURL` when it is no longer needed, so memory is not leaked.
12. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` is defined. Find the `media` slot. Change it from the mock media client to `httpShopMediaClient`. Do not touch any other slot.
13. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
14. Confirm the expected diff is limited to `clients/httpShopMediaClient.ts` and the Shop client composition file. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
15. Run the regression and browser proof steps in the "Regression and browser proof" section below.
16. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopMediaClient.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only; component edits require a documented contract defect.

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

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `media` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F044` against the real backend. Concretely: for each state F044 demonstrated (empty gallery, populated gallery, upload success, upload failure, reorder, delete, protected-image preview, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: upload or reorder an image, reload the page, and confirm the change survived; then trigger one 403/409/413/415 case and confirm it surfaces as a normal error via `ShopClientError`, not a crash or silent fallback.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm the same Zod schema (`shopProductWithGallerySchema`/`productGallerySchema`) parses both the old mock data and the new real backend data without modification.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted.
   The component/page diff is expected to be zero — if it is not, say exactly
   which component you had to touch and which contract mismatch forced it.
2. **Implementation decisions.** Every decision this Spec left to you, with the
   option you picked and one sentence of why.
3. **Contract comparison.** The result of step 1's check: whether the delivered
   backend C# records, routes and status codes matched
   `docs/design/shop/http-contracts.md` and this Spec. List every difference
   you found, even ones you decided were harmless. If you found none, say so
   explicitly.
4. **Commands executed.** `cd src/web && npm run build` and
   `cd src/web && npm run lint`, copied verbatim, in the order you ran them.
   State explicitly that you did not run `npm test` or `npm run test:e2e`.
5. **Results of those checks.** For each command: pass or fail, plus the error
   text if it failed. Then: which of the paired mock task's scenarios you
   replayed against the real backend, which slot is now real, whether reload
   and tenant-switch persisted, which failure path you triggered and how it
   surfaced, and whether the browser console stayed clean at all three
   viewports. Never report a check as passing if you did not run it.
6. **Risks, blockers and follow-up.** Anything you could not verify, any
   acceptance item you could not check off and why, and anything the next
   connection task needs to know. Write "None." if there is genuinely nothing.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `media` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
