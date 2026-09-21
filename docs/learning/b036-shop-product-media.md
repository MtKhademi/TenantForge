# B036 — Persist and serve safe product galleries

## 1. Files changed and why

| File | Why |
| --- | --- |
| `domain/ShopProductImage.cs` (new) | The gallery-image entity: server-generated `StorageKey`, decoded `Width`/`Height`, `DisplayOrder`, no original filename/path. `Create(...)` validates every field; `MoveTo`/`MoveToTemporarySlot` are the only ways `DisplayOrder` changes. |
| `domain/ShopProduct.cs` | Adds `GalleryVersion` (starts at `1`) and `IncrementGalleryVersion()` — the optimistic-concurrency guard every gallery mutation must check and bump. |
| `infrastructure/ShopProductImageMap.cs` (new) | EF map: unique `(product_id, display_order)`, unique `storage_key`, non-unique `(tenant_id, product_id)`, cascade delete from `shop_products`. |
| `infrastructure/ShopProductMap.cs`, `ShopDbContext.cs` | Column mapping for `GalleryVersion`; new `DbSet<ShopProductImage>` and its `ApplyConfiguration` call. |
| `features/media/{ProductMediaContracts,IShopMediaStorage,LocalShopMediaStorage,ShopImageValidator,ProductMediaFeature}.cs` (new) | The feature: request/response records; the storage seam and its local-disk implementation (stage → commit-after-DB-success → serve → delete); the decode-and-validate-then-re-encode pipeline; the five HTTP endpoints. |
| `ShopConfig.cs`, `ShopModule.cs` | Registers `IShopMediaStorage`/`ShopImageValidator`, validates `Shop:MediaRoot` at activation (fail closed), wires `MapProductMediaFeature` into the fixed endpoint-mapping order. |
| `features/products/ProductContracts.cs`, `ProductsFeature.cs`; `features/storefront/StorefrontContracts.cs`, `StorefrontCatalogFeature.cs` | Admin `ProductResponse` and both storefront responses now carry an ordered `Images` array (empty for pre-existing products — the JSON shape is additive only). |
| `infrastructure/Migrations/20260920185646_AddShopProductMedia.{cs,Designer.cs}`, `ShopDbContextModelSnapshot.cs` | Generated: the `shop_product_images` table and `shop_products.gallery_version` column. |
| `tests/.../ShopProductMediaIntegrationTests.cs` (new) | One test per required scenario (16 total) — round trip, cap, conflict, format/SVG/corrupt/oversized rejection, path-traversal-safe storage key, EXIF/GPS stripping, tenant/permission denial, protected-vs-public serving, staged-file cleanup on a forced commit failure, backward-compatible `Images: []`. |
| `tests/.../ApiFactory.cs` | Adds the required `Shop:MediaRoot` config value and exposes it as `ShopMediaRoot` so tests can assert filesystem side effects. |
| `tests/.../PlatformAdminSeederTests.cs`, `ShopModuleIntegrationTests.cs` | Two pre-existing test fixtures needed the same new required config key and the new table added to the exact-roster assertion — see §4 below. |
| `TenantForge.Api.IntegrationTests.csproj` | Pins the same `SixLabors.ImageSharp` version as the module, only to synthesize real JPEG/PNG/WebP (and EXIF-bearing) test fixtures. |
| `docs/modules/SHOP.md` (new) | The Shop module handbook, written from current code (composition, config, domain, persistence, auth, the full 28-route endpoint catalog, product media, inventory reservation, sandbox payment, tests, limitations). |
| `AGENTS.md` | Added the "Living Shop knowledge" read-first/change-impact gate, mirroring IAM's. |
| `docs/design/shop/http-contracts.md` | B036 section already matched the delivered contract; verified line-by-line (routes, records, nullability, problem codes) with no changes needed. |

## 2. Request flow — `POST /api/tenants/{tenantId}/shop/products/{productId}/images`

1. `RequireAuthorization()` rejects an unauthenticated caller with `401` before the handler runs; `.DisableAntiforgery()` is required because the endpoint binds a multipart form (`[FromForm]`) under bearer auth, and ASP.NET Core's default antiforgery would otherwise reject it — the JWT check is the real protection here, not a CSRF cookie.
2. `ShopAuthorization.AuthorizeTenantAccessAsync(..., ShopAuthorization.CatalogManagePermission)` re-checks tenant membership and the `Shop.Catalog.Manage` key server-side (B035's gate) — never trusts the UI.
3. `ShopImageValidator.ValidateAndReencodeAsync` streams the upload into a capped buffer (rejecting >5 MiB before it ever fully buffers), decodes the *actual* bytes with ImageSharp (never the filename or `Content-Type` header), rejects anything not JPEG/PNG/WebP, rejects animated/multi-frame and over-limit dimensions, strips EXIF/ICC/XMP, and re-encodes to WebP.
4. Inside one transaction: load the product, compare `GalleryVersion` to the client's `ExpectedGalleryVersion` (mismatch → `409`), check the eight-image cap, stage the sanitized bytes to a random `StorageKey` under `.staging`, persist the new `ShopProductImage` row, and increment `GalleryVersion` exactly once.
5. Only after the transaction commits does the handler call `storage.CommitAsync` (moves the staged file to its final path) — if anything above throws first, the staged file is deleted in a `catch` block, so a DB failure never leaves an orphaned file, and a storage failure never leaves an orphaned row.
6. The response is the full `ProductGalleryResponse` (all images, in order, with the new `GalleryVersion`) so the client always has the fresh state to retry against.

## 3. Backend concepts introduced

- **Decode-then-trust validation.** Both the format check and the dimension/frame check operate on ImageSharp's *decoded* result, not on `Content-Type` or the filename extension — a `.jpg`-named text file or a disguised SVG is rejected the same way a genuinely malformed file is.
- **Stage-then-commit storage, keyed to the database transaction.** The file only becomes visible at its real path after the row that references it is durably committed; deletion of a removed image's file only happens after *its* transaction commits. This keeps the filesystem and the database from ever disagreeing about which images exist.
- **Optimistic concurrency via a version counter, not a row lock.** `GalleryVersion` is the same "client must echo back what it last saw" pattern as other Shop optimistic-concurrency guards; a stale value returns `409`, never a silent overwrite or a blocking lock.
- **Two-phase reorder to satisfy an eager unique index.** PostgreSQL checks `(product_id, display_order)` immediately, not at commit, so a swap (not just a compaction) needs an intermediate state that cannot collide — every image first moves to a guaranteed-negative "parking" position, then to its real 0-based order, both inside the same transaction.
- **A storage seam (`IShopMediaStorage`) instead of a hardcoded filesystem call**, so a later task could swap in cloud storage without touching the feature handler.

## 4. Security decisions

- **Never trust client-declared media type.** Format and dimensions are decided by decoding, not by headers.
- **Never store or serve a client-supplied filename.** `StorageKey` is 24 cryptographically random bytes, hex-encoded; a path-traversal-style upload filename never reaches the filesystem (proven by `PathTraversalStyleFileName_IsHarmless`).
- **Strip EXIF/GPS/ICC/XMP unconditionally** before anything is staged — no accidental leak of the uploader's location or device metadata.
- **Fail closed on missing/invalid storage configuration.** `ShopConfig.ValidateConfiguration` (at `UseShopModuleAsync`, not at `AddShopModule`) throws if `Shop:MediaRoot` is blank, not absolute, or not writable — the host never starts half-configured.
- **`GetCommittedPath` re-validates every storage key against path traversal** (`..`, invalid filename characters, and a `GetFullPath` prefix check) even though keys are server-generated — defense in depth against a future caller of the storage seam that might not go through the validator.
- **Public byte route re-derives tenant/active state from a live join every request** (`image → product → category`, all three same-tenant, both `IsActive`) — a product or category can be deactivated after an image is served once, and the next request reflects that immediately.
- **The protected byte route is membership-only (no extra permission key)** — any authenticated tenant member may preview drafts, matching the same split IAM/Shop already use elsewhere between "can view" and "can mutate."
- **No credentials/tokens logged** anywhere in the new code.

## 5. Alternatives deliberately postponed

- **Cloud object storage / CDN signing** — `IShopMediaStorage` is the seam for this; `LocalShopMediaStorage` is the only implementation this slice needs (explicit non-goal).
- **Image cropping UI, video** — explicit non-goals; only still JPEG/PNG/WebP is accepted.
- **A distributed/queued re-encode pipeline** — re-encoding happens synchronously in the request; acceptable at this scale, revisit only if upload latency becomes a real problem.
- **Locking the product row for the whole upload** — the transaction plus the `GalleryVersion` check already gives the needed correctness without a broader lock.

## 6. Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopProductMediaIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Results: build succeeded (0 errors, 2 pre-existing unrelated nullability warnings in
`BuildingBlocksArchitectureTests.cs`/`IamContractArchitectureTests.cs`). The
targeted filter passed 16/16. The full suite initially failed 3 tests because
two pre-existing fixtures did not yet supply the new required `Shop:MediaRoot`
key and one exact-table-roster assertion had not been extended for
`shop_product_images` — both were fixed in this same task (see the files
above) and the full suite now passes 204/204.

Manual (real host):
1. Start Postgres (`docker compose up -d postgres`), run the API with a valid
   `Shop:MediaRoot` set.
2. As a catalog editor, `POST` a real JPEG to
   `/api/tenants/{tenantId}/shop/products/{productId}/images` with
   `expectedGalleryVersion: 1` → `201` with one image and `galleryVersion: 2`.
3. Reorder with the returned `imageIds` reversed and the new
   `expectedGalleryVersion` → `200` with the new order.
4. `GET /api/shop/{tenantId}/media/{imageId}` for an active product/category →
   image bytes with a one-year `Cache-Control`.
5. Deactivate the product, repeat step 4 → `404`.

## 7. Three review questions

1. `LocalShopMediaStorage.CommitAsync` uses `File.Move(..., overwrite: false)`. What happens if the process crashes *after* the database transaction commits but *before* this move runs — and does anything in this slice recover from that state, or is it an accepted gap?
2. The two-phase reorder (negative parking slots, then real positions) exists only because of the *immediate* unique-index check. If the index were deferred (`INITIALLY DEFERRED`) instead, would the extra `SaveChangesAsync` round trip still be necessary — what would change and what would stay the same?
3. `GetCommittedPath` revalidates a `StorageKey` that is always server-generated by `GenerateStorageKey`. Is this defense-in-depth pulling its weight, or would you argue it is validating an invariant the type system/caller graph should guarantee instead — and how would you decide which one is worth keeping?
