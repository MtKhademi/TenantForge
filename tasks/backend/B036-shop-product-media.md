# B036 — Persist and serve safe product galleries

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S32`.
- Depends on: `B035`.
- Immediate browser consumer: `F054`. Do not widen this API for an unnamed future screen.
- Visible outcome: A catalog manager uploads, orders and removes real product images; published storefront product cards and detail pages receive authorized media URLs.

## Read before editing

Read `AGENTS.md`, the `B036` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/032-shop-product-media.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change


        - `src/modules/shop/TenantForge.Modules.Shop/domain/ShopProductImage.cs`
        - `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopProductImageMap.cs`
        - `src/modules/shop/TenantForge.Modules.Shop/features/media/{ProductMediaContracts.cs,ProductMediaFeature.cs,IShopMediaStorage.cs,LocalShopMediaStorage.cs,ShopImageValidator.cs}`
        - `ShopDbContext.cs`, `ShopConfig.cs`, `ShopModule.cs`, product/storefront contracts and projections
        - one EF migration + snapshot, Shop integration tests, `docs/modules/SHOP.md`, B036 learning note


Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract


        | Method | Route | Auth | Result |
        |---|---|---|---|
        | POST | `/api/tenants/{tenantId}/shop/products/{productId}/images` | JWT + `Shop.Catalog.Manage` | `201 ProductGalleryResponse` |
        | PUT | `/api/tenants/{tenantId}/shop/products/{productId}/images/order` | same | `200 ProductGalleryResponse` |
| DELETE | `/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}?expectedGalleryVersion={n}` | same | `204` |
        | GET | `/api/tenants/{tenantId}/shop/products/{productId}/images/{imageId}/content` | JWT + membership | image bytes |
        | GET | `/api/shop/{tenantId}/media/{imageId}` | anonymous, only active category/product | image bytes or generic `404` |


All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation


        1. Add `ShopProductImage`: `Id`, `TenantId`, `ProductId`, server-generated `StorageKey`, `ContentType`, `ByteLength`, `Width`, `Height`, `AltText`, `DisplayOrder`, `CreatedAtUtc`. Add unique `(ProductId, DisplayOrder)` and indexes `(TenantId, ProductId)`. Do not store original file names or public file-system paths.
        2. Add `GalleryVersion` to `ShopProduct`; initialize to `1`. Upload/order/delete require `expectedGalleryVersion`, execute in one transaction and increment exactly once. Cap a gallery at eight images.
        3. `ShopImageValidator` must decode bytes, not trust extension or `Content-Type`. Accept JPEG/PNG/WebP only; reject SVG and animated/multi-frame input; limit input to 5 MiB, each dimension to 4096, and pixels to 16 million. Re-encode to WebP and strip EXIF/GPS. The implementer must choose and pin a maintained .NET 10-compatible decoder and record license/version in the learning note.
        4. `LocalShopMediaStorage` resolves generated random keys below `Shop:MediaRoot`, stages a sanitized file before DB commit, deletes it after a failed commit, and deletes removed files only after a successful commit. Validate an absolute writable root at activation. Never call `UseStaticFiles` for this directory. Because the upload binds `IFormFile` on a bearer-auth API, explicitly call `.DisableAntiforgery()` on that endpoint and keep the documented JWT/permission check; do not add cookie antiforgery middleware accidentally.
        5. The protected byte route allows catalog editors to preview drafts. The public byte route joins image -> product -> category and returns only same-tenant active products/categories. Both set `X-Content-Type-Options: nosniff`; protected responses use `Cache-Control: no-store`, public immutable content may use one-year caching because keys never change.
        6. Extend admin `ProductResponse` and storefront summary/detail responses with ordered images. Existing products return `[]`. The first ordered image is the card thumbnail. Preserve all existing JSON members.
        7. Create `docs/modules/SHOP.md` from current code: module seam/config, routes, entities, tenant/auth rules, inventory reservation behavior, payment sandbox, tests and limitations. Add the same read-first/change-impact gate to `AGENTS.md` used by IAM. Every later Shop task updates it or states `SHOP.md impact: none — <reason>`.


## Required code shape


        ```csharp
        internal sealed class ShopProductImage
        {
            public Tsid Id { get; private set; } = TsidId.NewId();
            public Tsid TenantId { get; private set; }
            public Tsid ProductId { get; private set; }
            public string StorageKey { get; private set; } = string.Empty;
            public string ContentType { get; private set; } = "image/webp";
            public long ByteLength { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            public string AltText { get; private set; } = string.Empty;
            public int DisplayOrder { get; private set; }
            public DateTimeOffset CreatedAtUtc { get; private set; }
        }

        public sealed record UploadProductImageRequest(IFormFile File, string? AltText, int ExpectedGalleryVersion);
        public sealed record ReorderProductImagesRequest(IReadOnlyList<string>? ImageIds, int ExpectedGalleryVersion);
        public sealed record ProductImageResponse(string Id, string AltText, int DisplayOrder, int Width, int Height, string ContentUrl);
        public sealed record ProductGalleryResponse(IReadOnlyList<ProductImageResponse> Images, int GalleryVersion);

        internal interface IShopMediaStorage
        {
            Task<StagedShopMedia> StageAsync(Stream sanitized, CancellationToken ct);
            Task CommitAsync(StagedShopMedia media, CancellationToken ct);
            Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
            Task DeleteIfExistsAsync(string storageKey, CancellationToken ct);
        }
        ```


The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

valid upload/reload/order/delete; eight-image cap; stale gallery version; fake MIME, SVG, corrupt and oversized images rejected; traversal filename harmless; metadata stripped; foreign tenant and unauthorized editor denied; draft image denied publicly; published active image served; last image deletion is allowed because current publication has no media invariant; staged file removed on DB failure; pre-B036 responses remain backward compatible.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F054` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Cloud object storage, video, image cropping UI, CDN signing, or changing product publication rules.

## Acceptance checklist

- [ ] The visible outcome works through `F054` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
