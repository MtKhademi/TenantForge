# F054 — Bind product media client to B036 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F054` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F044**.
- Slice: `S32`; depends on `F053, B036`.
- Backend source of truth: `B036` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

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

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F044`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `media` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F044` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `media` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
