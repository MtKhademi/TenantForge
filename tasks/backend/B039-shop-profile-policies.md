# B039 — Add tenant storefront identity and policy content

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S35`.
- Depends on: `B036, B035`.
- Immediate browser consumer: `F057`. Do not widen this API for an unnamed future screen.
- Visible outcome: A tenant publishes its store name, support details and customer policy pages instead of the hard-coded "فروشگاه" shell.

## Do this in order

Before step 1, follow the "Read before editing" section below: read `AGENTS.md`, the `B039` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, the linked slice file `tasks/slices/035-shop-profile-policies.md`, and the matching section of `docs/design/shop/http-contracts.md`. Follow the branch-naming, approval-gate and ledger-update rules already described in the "Ownership and dependency" section and in `AGENTS.md` — do not repeat them, just follow them.

1. Create a new EF entity class `ShopProfile` (one row per tenant) with exactly these members: `Id` (TSID — a sortable numeric string ID, see `TenantForge.BuildingBlocks` — this is the primary key), `TenantId`, `Name` (string, max length 100), `Tagline` (string, max length 180), `SupportPhone` (string, max length 30), `InstagramUrl` (nullable string, max length 300), `AboutText` (string, max length 4000), `ShippingPolicy` (string, max length 6000), `PaymentPolicy` (string, max length 6000), `ReturnPolicy` (string, max length 6000), `PrivacyPolicy` (string, max length 6000), `IsPublished` (bool), `Version` (int, used for optimistic concurrency), and the module's normal created/updated timestamp columns.
2. In the EF configuration/mapping class for `ShopProfile`, add a unique index/constraint on `TenantId` (exactly one profile row per tenant).
3. Add an EF migration for this new table (run `dotnet ef migrations add <Name>` in the Shop module project — this generates the schema-change script the module applies at startup). Inspect the generated migration afterward and confirm it only adds the intended columns/index, nothing else.
4. Add the plain-text and format rules everywhere you read or write `ShopProfile` fields:
   a. Trim outer (leading/trailing) whitespace on every text field before saving. Preserve internal newlines exactly as typed (do not collapse them).
   b. Never allow the stored text to be rendered as HTML anywhere — treat every field as plain text end to end.
   c. Validate `SupportPhone` against a conservative allowlist of characters used for phone-like display text (digits, spaces, `+`, `-`, `(`, `)` — do not invent new punctuation beyond what the module already treats as safe display text elsewhere).
   d. Validate `InstagramUrl`, when not null, as an HTTPS URL whose host is exactly `instagram.com` or a subdomain of it (e.g. `www.instagram.com` is allowed, `instagram.com.evil.example` is not). Reject anything else with a validation error.
5. Add the `Shop.Settings.Manage` permission key to the existing permission catalog, the same way other `Shop.*` permission keys are already registered in this module.
6. Add these HTTP endpoints (Minimal API, matching the module's existing route/handler conventions):
   a. `GET /api/tenants/{tenantId}/shop/profile` — requires a valid JWT (JSON Web Token — the repo's standard bearer auth) plus tenant membership. If no `ShopProfile` row exists yet for the tenant, return `200 OK` with `ShopProfileResponse { Profile: null }` (do not 404). If a row exists, map it to `ShopProfileDto` and return `200 OK` with `ShopProfileResponse { Profile: dto }`.
   b. `PUT /api/tenants/{tenantId}/shop/profile` — requires JWT plus the `Shop.Settings.Manage` permission (use the existing `ShopAuthorization` helper the module already uses for other manage-permission endpoints). Body is `SaveShopProfileRequest`. See step 7 for the save logic. Returns `200 OK` with `ShopProfileResponse` on success.
   c. `GET /api/shop/{tenantId}/profile` — anonymous (no auth required), for the public storefront. If no profile exists, or the profile's `IsPublished` is `false`, return `404` (use the repository's existing RFC7807 shape — RFC7807 is the repo's standard JSON error body format; reuse the existing helper, do not invent a new error format). Otherwise map to `PublicShopProfileResponse` (this DTO has no `Version` field — never include the internal version number in the public response) and return `200 OK`.
7. Implement the PUT handler's save logic exactly like this:
   - Re-apply every trim/validation rule from step 4 to the incoming `SaveShopProfileRequest` fields before touching the database. Return the repository's standard validation-error RFC7807 response if any rule fails.
   - If `ExpectedVersion` is `null`, this is a create: attempt to insert a new `ShopProfile` row for `tenantId`. Rely on the unique `TenantId` database constraint from step 2 to guarantee that if two requests race to create the first profile at the same time, exactly one insert succeeds and the other fails.
   - If `ExpectedVersion` is not `null`, this is an update: load the existing row for `tenantId`, compare its current `Version` to `ExpectedVersion`. If they differ, or the losing concurrent create/update from the point above happens, return `409 Conflict` with RFC7807 `type=stale_version` (do not silently overwrite). If they match, apply the new field values, increment `Version`, and save.
   - Always re-check `tenantId` server-side from the authenticated request context — never trust a tenant ID typed into the request body, and never let the UI's visible/hidden state substitute for this server check.
8. Confirm publication only gates the profile/policy pages, not the storefront catalog: do not add any check anywhere that blocks catalog/cart/checkout routes based on `IsPublished`. Existing storefront URLs must keep working during rollout regardless of `IsPublished`.
9. Add the integration tests listed in "Integration tests required" below, in the closest existing `Shop*IntegrationTests.cs` file (or a new file named after this feature if none fits). Use the real PostgreSQL test fixture the other Shop tests use. Every test must assert on the actual response body and/or the actual database row — a test that only checks the HTTP status code is not sufficient.
10. Update `docs/modules/SHOP.md` (create it if it does not exist yet) so its routes/entities/config/auth/tests sections match exactly what you built.
11. Update the matching section of `docs/design/shop/http-contracts.md` to the delivered wire contract (exact routes, field names, types, nullability, status codes) before this Spec file is deleted at task completion.
12. Write the `docs/learning/B039-<slug>.md` learning note per the "Backend learning note" rules in `AGENTS.md`.
13. Run every command listed under "Validation" below, in order, before asking for final approval.

## Read before editing

Read `AGENTS.md`, the `B039` row in `tasks/TASKS.md`, this complete Spec, `docs/modules/SHOP.md` when it exists, `tasks/slices/035-shop-profile-policies.md`, and only the current source files listed below. Verify every stated baseline against current code before presenting the first approval plan.

Also read the matching section in `docs/design/shop/http-contracts.md`; this backend task must update it to the delivered wire contract before its executable Spec is deleted.

Current baseline is commit `34dc44e`: Shop uses one module project, internal EF entities, TSID IDs, a separate migration-history table, raw-SQL IAM membership/role checks, anonymous storefront/cart/checkout/order/payment/lookup routes, and exact integration tests under `Shop*IntegrationTests.cs`. Preserve those conventions unless this Spec explicitly changes one.

## Files expected to change

new profile entity/map/contracts/feature/migration, `Shop.Settings.Manage` permission contribution, module mapping, integration tests, SHOP handbook and learning note.

Do not edit `src/web/**`. Do not create a `Shop.Contract` project: no second .NET consumer currently proves that boundary.

## HTTP contract

| Method | Route | Auth | Result |
|---|---|---|---|
| GET | `/api/tenants/{tenantId}/shop/profile` | JWT + membership | `200 ShopProfileResponse` (`profile:null` allowed) |
| PUT | same | JWT + `Shop.Settings.Manage` | `200 ShopProfileResponse` |
| GET | `/api/shop/{tenantId}/profile` | anonymous | `200 PublicShopProfileResponse` or `404` when unpublished |

All errors use the repository's existing Minimal API/RFC7807 shapes. Malformed TSIDs and cross-tenant resource IDs return the same non-leaking result. Every mutation rechecks tenant authorization on the server; UI visibility is never authorization.

## Required implementation

1. Add one `ShopProfile` per tenant: `Id`, `TenantId`, `Name(100)`, `Tagline(180)`, `SupportPhone(30)`, `InstagramUrl(300?)`, `AboutText(4000)`, `ShippingPolicy(6000)`, `PaymentPolicy(6000)`, `ReturnPolicy(6000)`, `PrivacyPolicy(6000)`, `IsPublished`, `Version`, timestamps. Unique `TenantId`.
2. Store plain text only. Trim outer whitespace; preserve internal newlines. Validate phone as display text with a conservative character allowlist, and require Instagram URL HTTPS with host exactly `instagram.com` or a subdomain. Never render stored text as HTML.
3. GET admin before first save returns `{profile:null}`. PUT uses `ExpectedVersion:null` for create and exact version for update; database uniqueness resolves concurrent first creates to one success and one `409 stale_version`.
4. Public GET returns only published profiles and omits `Version`. Publication does not control catalog availability; it controls only profile/policy UI so existing storefront URLs remain functional during rollout.
5. Add `Shop.Settings.Manage` to the catalog and use existing `ShopAuthorization` semantics. The F047 mock already renders the planned nav state; F057 binds it to the delivered permission.

## Required code shape

The definitions below give exact names and fields. Step 6/7 above (in "Do this in order") already describes the exact handler logic to write; treat that as the completed implementation of this shape — do not leave mapping/validation as a placeholder comment in production code. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

```csharp
public sealed record SaveShopProfileRequest(
    string? Name, string? Tagline, string? SupportPhone, string? InstagramUrl,
    string? AboutText, string? ShippingPolicy, string? PaymentPolicy,
    string? ReturnPolicy, string? PrivacyPolicy, bool IsPublished, int? ExpectedVersion);
public sealed record ShopProfileDto(
    string Id, string TenantId, string Name, string Tagline,
    string SupportPhone, string? InstagramUrl, string AboutText,
    string ShippingPolicy, string PaymentPolicy, string ReturnPolicy,
    string PrivacyPolicy, bool IsPublished, int Version,
    DateTimeOffset UpdatedAtUtc);
public sealed record ShopProfileResponse(ShopProfileDto? Profile);
public sealed record PublicShopProfileResponse(
    string Name, string Tagline, string SupportPhone, string? InstagramUrl,
    string AboutText, string ShippingPolicy, string PaymentPolicy,
    string ReturnPolicy, string PrivacyPolicy, bool IsPublished);
```

The snippets define names, ownership and invariants. Complete the omitted mapping/validation/async code; do not paste placeholder comments into production. Keep feature types `internal` except HTTP records already following the module's current public-record convention.

## Security and transaction rules

- Apply `TenantId` in the first database predicate for tenant-owned data.
- Never accept TenantId, totals, prices, stock deltas, permission keys or payment success from a browser body when the server can derive them.
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync` (an `AnyAsync` "does a row already exist" check followed by a separate insert is not safe under concurrency by itself — the database constraint or lock is what actually prevents the race).
- Thread `CancellationToken` (the standard .NET signal that lets a caller cancel an in-flight async operation, e.g. on client disconnect) through new I/O.
- Use `TimeProvider` (the repo's injectable clock abstraction, instead of calling `DateTimeOffset.UtcNow` directly, so tests can control time) where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

Write one test per scenario below. Each test must assert on the real response body and/or the real database row, not just the HTTP status code.

- [ ] Write a test that calls admin GET before any profile exists, then asserts the response is `200` with `Profile: null`.
- [ ] Write a test that creates a profile via PUT with `ExpectedVersion: null`, then updates it via PUT with the returned `Version`, then reloads it via GET, then asserts the final stored values and `Version` match what was sent.
- [ ] Write a test that submits an update with a stale (outdated) `ExpectedVersion`, then asserts it returns `409` with RFC7807 `type=stale_version`.
- [ ] Write a test that fires two concurrent create requests (`ExpectedVersion: null`) for the same tenant, then asserts exactly one succeeds and the other fails with a conflict, and exactly one `ShopProfile` row exists afterward.
- [ ] Write a test that creates profiles for two different tenants, then asserts tenant A cannot read or write tenant B's profile through either admin route.
- [ ] Write a test that calls PUT without the `Shop.Settings.Manage` permission, then asserts it is rejected, and a separate test that confirms a tenant Owner bypass (if the module already grants Owners all permissions) is respected consistently with other Shop endpoints.
- [ ] Write a test that submits text fields with leading/trailing whitespace and internal newlines, then asserts the stored value is trimmed on the outside only, and a test that submits a field exceeding its max length, then asserts a validation error is returned.
- [ ] Write a test that submits an `InstagramUrl` that is not HTTPS, or whose host is not `instagram.com`/a subdomain, then asserts a validation error is returned.
- [ ] Write a test that calls the public GET route for a tenant whose profile has `IsPublished: false` (or has no profile at all), then asserts `404`.
- [ ] Write a test that calls the public GET route for a published profile, then asserts the response body is `PublicShopProfileResponse` and contains no `Version` field.

Add tests to the closest existing `Shop*IntegrationTests.cs` file or create one named after the feature. Use the real PostgreSQL fixture. Test response bodies and persisted side effects; a status-code-only happy-path test is insufficient.

## Browser handoff

In the PR body give `F057` exact routes, sample JSON, error codes, permission key and seed/setup steps. Demonstrate the happy path and relevant failure path through the current or immediately dependent frontend.

## Validation

1. `dotnet build TenantForge.sln --nologo`
2. Targeted Shop integration test class.
3. Full `dotnet test TenantForge.sln --nologo` (or the repository's documented Windows `dotnet.exe` equivalent).
4. Inspect the generated migration for only intended schema changes.
5. Verify `docs/modules/SHOP.md` against routes/entities/config/auth/tests and update the Bxxx learning note.

## Non-goals

Custom domains, themes, arbitrary HTML, logo upload, SEO CMS, email or SMS provider settings.

## Acceptance checklist

- [ ] The visible outcome works through `F057` after the contract-shaped mock is accepted.
- [ ] Happy path, validation, authorization, tenant isolation and concurrency/idempotency behavior are proven.
- [ ] Existing Shop and IAM tests still pass without weakening exact-roster/security assertions.
- [ ] Migration/startup is repeatable and does not collide with IAM history.
- [ ] The matching persistent Shop HTTP contract section matches delivered routes, records, nullability, enums and problem codes.
- [ ] SHOP handbook impact is updated precisely.
- [ ] Only this ledger row moves to `in_progress`, then `review`; delivery marks it `done`, removes this Spec and preserves the source slice.
