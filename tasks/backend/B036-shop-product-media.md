# B036 — Persist and serve safe product galleries

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S32`.
- Depends on: `B035`.
- Immediate browser consumer: `F054`. Do not widen this API for an unnamed future screen.
- Visible outcome: A catalog manager uploads, orders and removes real product images; published storefront product cards and detail pages receive authorized media URLs.

## Do this in order

Before step 1, follow the boilerplate already described in `AGENTS.md` and in
"Read before editing" below: read `AGENTS.md`, read `tasks/slices/032-shop-product-media.md`,
read the `B036` row in `tasks/TASKS.md`, read `docs/modules/SHOP.md` if it
exists, read the matching section of `docs/design/shop/http-contracts.md`,
follow the branch-naming and ledger-update rules from the Ownership section,
and get plan approval before writing code (backend tasks always wait for
approval after the plan).

1. Create `src/modules/shop/TenantForge.Modules.Shop/domain/ShopProductImage.cs`
   with the exact class shown in "Required code shape" below (all properties,
   default values and access modifiers as written).
2. Add a `GalleryVersion` integer property to the existing `ShopProduct`
   entity. Default/initialize new products to `1`.
3. Create `src/modules/shop/TenantForge.Modules.Shop/infrastructure/ShopProductImageMap.cs`,
   an EF Core `IEntityTypeConfiguration<ShopProductImage>` (follow the same
   pattern the module already uses for other entity maps in that folder).
   In it configure: a unique index on `(ProductId, DisplayOrder)`, a
   non-unique index on `(TenantId, ProductId)`, and column mappings for every
   property on `ShopProductImage`. Do not add a column for original file name
   or a public file-system path — none exists on the entity.
4. Register `ShopProductImage` as a `DbSet` on `ShopDbContext.cs` and add its
   configuration alongside the other `ApplyConfiguration` calls.
5. Create the feature folder
   `src/modules/shop/TenantForge.Modules.Shop/features/media/` and add these
   five files:
   - `ProductMediaContracts.cs` — the four records from "Required code
     shape" (`UploadProductImageRequest`, `ReorderProductImagesRequest`,
     `ProductImageResponse`, `ProductGalleryResponse`).
   - `IShopMediaStorage.cs` — the interface from "Required code shape" plus
     the `StagedShopMedia` type it references (a small internal record with
     at least `StorageKey` and a way to reach the staged file's stream/path;
     follow the module's existing naming convention for similar staging
     types).
   - `LocalShopMediaStorage.cs` — implements `IShopMediaStorage` per point 4
     of "Required implementation" below.
   - `ShopImageValidator.cs` — implements point 3 of "Required
     implementation" below.
   - `ProductMediaFeature.cs` — registers the five HTTP endpoints from "HTTP
     contract" below (upload, reorder, delete, protected content, public
     content), each doing exactly what "Required implementation" and
     "Security and transaction rules" describe.
6. Wire the new feature's endpoint group and any new options (for example
   `Shop:MediaRoot`) into `ShopConfig.cs` and `ShopModule.cs` the same way
   existing Shop features are registered.
7. Extend the admin `ProductResponse` contract and the storefront
   summary/detail response contracts (wherever they are currently defined)
   to add an ordered list of images, per point 6 of "Required
   implementation" below. Do not remove or rename any existing JSON member
   on those contracts.
8. Generate one EF Core migration ("EF migration" = running
   `dotnet ef migrations add <Name>` inside the Shop module project; this
   writes the SQL needed to create the new table/columns/indexes) that adds
   the `ShopProductImage` table and the `ShopProduct.GalleryVersion` column.
   Commit the migration file and its updated model snapshot.
9. Add or extend integration tests in the closest existing
   `Shop*IntegrationTests.cs` file (or a new file named after this feature)
   covering every scenario listed in "Integration tests required" below —
   one test method per scenario.
10. Create `docs/modules/SHOP.md` per point 7 of "Required implementation"
    below, and add the same read-first/change-impact gate language to
    `AGENTS.md` that the IAM module already has (copy the pattern from the
    "Living module knowledge" section of `AGENTS.md`, adapted to Shop).
11. Update the matching section of `docs/design/shop/http-contracts.md` so
    it matches exactly what you delivered (routes, records, nullability,
    enums, problem codes).
12. Write the `B036` learning note at `docs/learning/B036-<slug>.md`,
    including the decoder library you chose for image re-encoding (name,
    version, license — see point 3 of "Required implementation").
13. Run every command in "Validation" below, in order, and fix failures
    before moving on.
14. Follow the "Browser handoff" section to hand `F054` what it needs.
15. Work through every line of "Acceptance checklist" below and check it
    off only once you have evidence for it.

## Read before editing

Read `AGENTS.md`, the `B036` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/032-shop-product-media.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Baseline as of commit `34dc44e` (later commits changed only `src/web/**` and
`tasks/**`, so this still describes the backend you will find): Shop uses one module project, internal EF entities, TSID IDs (TSID = "a sortable numeric string ID, see `TenantForge.BuildingBlocks`" — it is what `Tsid`/`TsidId.NewId()` below produce), a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

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

All errors use the repository's existing Minimal API/RFC7807 shapes (RFC7807 = "the repo's standard JSON error body shape for HTTP errors — reuse the existing helper, don't invent a new error format"). Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add `ShopProductImage`: `Id`, `TenantId`, `ProductId`, server-generated `StorageKey`, `ContentType`, `ByteLength`, `Width`, `Height`, `AltText`, `DisplayOrder`, `CreatedAtUtc`. Add unique `(ProductId, DisplayOrder)` and indexes `(TenantId, ProductId)`. Do not store original file names or public file-system paths.
2. Add `GalleryVersion` to `ShopProduct`; initialize to `1`. Upload/order/delete require `expectedGalleryVersion`, execute in one transaction and increment exactly once. Cap a gallery at eight images.
3. `ShopImageValidator` must decode bytes, not trust extension or `Content-Type`. Accept JPEG/PNG/WebP only; reject SVG and animated/multi-frame input; limit input to 5 MiB, each dimension to 4096, and pixels to 16 million. Re-encode to WebP and strip EXIF/GPS. The implementer must choose and pin a maintained .NET 10-compatible decoder and record license/version in the learning note.
4. `LocalShopMediaStorage` resolves generated random keys below `Shop:MediaRoot`, stages a sanitized file before DB commit, deletes it after a failed commit, and deletes removed files only after a successful commit. Validate an absolute writable root at activation. Never call `UseStaticFiles` for this directory. Because the upload binds `IFormFile` on a bearer-auth API, explicitly call `.DisableAntiforgery()` on that endpoint and keep the documented JWT/permission check; do not add cookie antiforgery middleware accidentally. (Antiforgery = the anti-CSRF token check ASP.NET Core applies to form-posting endpoints by default; it is not needed here because this endpoint is already protected by a JWT bearer token, so it must be explicitly turned off with `.DisableAntiforgery()` rather than accidentally left on or replaced with cookie-based CSRF middleware.)
5. The protected byte route allows catalog editors to preview drafts. The public byte route joins image -> product -> category and returns only same-tenant active products/categories. Both set `X-Content-Type-Options: nosniff`; protected responses use `Cache-Control: no-store`, public immutable content may use one-year caching because keys never change.
6. Extend admin `ProductResponse` and storefront summary/detail responses with ordered images. Existing products return `[]`. The first ordered image is the card thumbnail. Preserve all existing JSON members.
7. Create `docs/modules/SHOP.md` from current code: module seam/config, routes, entities, tenant/auth rules, inventory reservation behavior, payment sandbox, tests and limitations. Add the same read-first/change-impact gate to `AGENTS.md` used by IAM. Every later Shop task updates it or states `SHOP.md impact: none — <reason>`.

## Required code shape

Below are the exact names, fields and invariants you must implement. Where a
member's body is not written out, add it as its own line item with the exact
name, type and one-sentence behavior — do not leave any method empty or
invent different names.

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

The snippets define names, ownership and invariants. Complete the omitted
mapping/validation/async code; do not paste placeholder comments into
production. Keep feature types `internal` except HTTP records already
following the module's current public-record convention.

Members you must still add yourself, one item per bullet, with exactly this
name/type/behavior:

- `ShopProductImage` needs a private/internal constructor plus a static
  factory (for example `Create(...)`) that sets every field above from
  validated inputs — follow the same constructor-plus-factory pattern the
  module's other domain entities already use.
- `StagedShopMedia` (referenced by `IShopMediaStorage` but not shown above):
  an internal record/class holding at minimum the generated `StorageKey`
  (`string`) and a way to read the staged bytes back (e.g. a `string`
  temp-file path or a `Stream`). Name it to match the module's existing
  staging-type conventions if one already exists.
- `LocalShopMediaStorage.StageAsync(Stream sanitized, CancellationToken ct)`:
  generate a random storage key, write `sanitized` to a temp location under
  `Shop:MediaRoot`, and return a `StagedShopMedia` describing it. Must not
  yet be visible at its final `StorageKey` path.
- `LocalShopMediaStorage.CommitAsync(StagedShopMedia media, CancellationToken ct)`:
  move/rename the staged file to its final path under `Shop:MediaRoot` keyed
  by `StorageKey`. Called only after the database transaction that persists
  the `ShopProductImage` row succeeds.
- `LocalShopMediaStorage.OpenReadAsync(string storageKey, CancellationToken ct)`:
  open a read stream for a committed file, or return `null` if it does not
  exist.
- `LocalShopMediaStorage.DeleteIfExistsAsync(string storageKey, CancellationToken ct)`:
  delete the committed file if present; used both for failed-commit cleanup
  of staged files and for permanent deletion of removed images after a
  successful DB commit.
- `ShopImageValidator` needs one public method, for example
  `ValidateAndReencodeAsync(Stream input, CancellationToken ct)`, that: reads
  and decodes the actual image bytes (never trusts the file extension or the
  `Content-Type` header), rejects anything that is not JPEG/PNG/WebP,
  rejects SVG and animated/multi-frame images, rejects input over 5 MiB,
  rejects images wider or taller than 4096px, rejects images over 16 million
  total pixels, then re-encodes the accepted image to WebP with EXIF/GPS
  metadata stripped, and returns the sanitized bytes plus the decoded
  `Width`/`Height`. Pick one maintained .NET 10-compatible image
  decoding/encoding library, pin its exact version in the project file, and
  record its name, version and license in the `B036` learning note.
- `ProductMediaFeature` must register five endpoints matching the "HTTP
  contract" table exactly (same routes, methods, auth and result codes).
  Each handler must: resolve tenant/product/image IDs as TSIDs and return a
  generic non-leaking `404` for malformed or cross-tenant IDs; check
  `expectedGalleryVersion` and return a conflict result on mismatch; run the
  DB write plus `GalleryVersion` increment inside one transaction; call
  `.DisableAntiforgery()` on the upload endpoint; and set
  `X-Content-Type-Options: nosniff` on both byte-serving endpoints, with
  `Cache-Control: no-store` on the protected one and a one-year
  `Cache-Control` on the public one.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O ("abort-aware": if the caller disconnects or the request times out, in-flight database/file work should observe the token and stop rather than run to completion unobserved).
- Use `TimeProvider` where this task adds time-dependent behavior (the repo's injectable clock abstraction — use it instead of `DateTime.UtcNow` directly so tests can control time).
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each is phrased as "do X, then assert Y":

- [ ] Write a test that uploads a valid image, reloads the gallery, reorders it and deletes an image, then asserts each step's response and persisted state are correct.
- [ ] Write a test that uploads a ninth image to a full gallery, then asserts it is rejected because of the eight-image cap.
- [ ] Write a test that sends a stale `expectedGalleryVersion`, then asserts the request is rejected as a conflict.
- [ ] Write a test that uploads a file with a fake/mismatched MIME type, then asserts it is rejected.
- [ ] Write a test that uploads an SVG file, then asserts it is rejected.
- [ ] Write a test that uploads a corrupt image file, then asserts it is rejected.
- [ ] Write a test that uploads an oversized image (over the limits in "Required implementation" point 3), then asserts it is rejected.
- [ ] Write a test that uploads a file with a path-traversal-style filename, then asserts the stored file/key is unaffected by it (traversal is harmless).
- [ ] Write a test that uploads an image containing EXIF/GPS metadata, then asserts the stored/served image has that metadata stripped.
- [ ] Write a test that attempts the operations as a foreign tenant, then asserts it is denied.
- [ ] Write a test that attempts the operations as an unauthorized editor (missing `Shop.Catalog.Manage`), then asserts it is denied.
- [ ] Write a test that requests a draft (unpublished) image through the public byte route, then asserts it is denied.
- [ ] Write a test that requests a published/active image through the public byte route, then asserts it is served.
- [ ] Write a test that deletes the last remaining image in a gallery, then asserts it is allowed, because current publication rules have no "must have media" invariant.
- [ ] Write a test that forces a DB commit failure after staging a file, then asserts the staged file is removed.
- [ ] Write a test against a pre-B036 product/storefront response, then asserts it still deserializes and matches the old shape (backward compatible), with `Images: []`.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F054` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

Run these from the repository root, in this order, and fix every failure
before moving to the next command.

On the reference WSL setup there is no Linux `dotnet` binary — use `dotnet.exe`
instead of `dotnet` in every command below. See
`docs/architecture.md#local-development-environment-wsl--windows-net-sdk`.

The integration tests start PostgreSQL through Testcontainers, so Docker must
be running before you run any test command. Start it with `docker compose up -d postgres`
if Docker Desktop is not already up (the compose service is not what the tests
connect to, but it confirms the Docker daemon is reachable).

1. Build everything:

   ```bash
   dotnet build TenantForge.sln --nologo
   ```

2. Run only this task's Shop integration tests first (replace
   `<ShopTestClass>` with the exact class name you added or extended, for
   example `ShopCatalogAdminIntegrationTests`):

   ```bash
   dotnet test TenantForge.sln --nologo --filter FullyQualifiedName~<ShopTestClass>
   ```

3. Run the full test suite and confirm it is green:

   ```bash
   dotnet test TenantForge.sln --nologo
   ```

4. Open the migration file you generated under
   `src/modules/shop/TenantForge.Modules.Shop/infrastructure/Migrations/` and
   read it line by line. Confirm it contains only the schema changes this Spec
   asked for and nothing else. Confirm `ShopDbContextModelSnapshot.cs` was
   updated in the same change.

5. Re-read `docs/modules/SHOP.md` and check every routes/entities/config/auth/tests
   statement against the code you actually delivered. Then finish the
   `docs/learning/B036-<slug>.md` learning note.

6. Confirm the frontend was not touched:

   ```bash
   git diff --name-only origin/main... -- src/web
   ```

   This must print nothing.

## Non-goals

Cloud object storage, video, image cropping UI, CDN signing, or changing product publication rules.

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   grouped as: production code, EF migration (generated), tests,
   documentation. Say which files are generated rather than hand-written.
2. **Implementation decisions.** Every decision this Spec left to you, with
   the option you picked and one sentence of why. If you followed an "if
   unsure, do X" default from this Spec, say so and name it.
3. **Commands executed.** Every command from "Validation" above, copied
   verbatim in the order you ran them.
4. **Results of those checks.** For each command: pass or fail, and for the
   test commands the actual passed/failed/skipped counts. If you had to re-run
   something after a fix, say that and give the final result. Never report a
   command as passing if you did not run it.
5. **Risks, blockers and follow-up.** Anything you could not verify, any
   scenario from "Integration tests required" you could not cover and why, any
   contract detail that differed from this Spec, and anything the next task
   (F054) must know. Write "None." if there is genuinely nothing.
6. **Documentation impact statement.** The exact line
   `SHOP.md impact: <what you updated>` or
   `SHOP.md impact: none — <specific reason>`, plus the same line for
   `IAM.md`, `BuildingBlocks docs` and `IAM Contract docs` if your diff touched
   any of them (see `AGENTS.md`). A vague "docs not needed" is not accepted.

## Acceptance checklist

- [ ] The visible outcome works through `F054` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
