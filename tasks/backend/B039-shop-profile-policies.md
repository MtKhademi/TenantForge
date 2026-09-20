# B039 — Add tenant storefront identity and policy content

## Ownership and dependency

- Owner: backend mentor; run from the `backend` clone with `/backend-task`.
- Slice: `S35`.
- Depends on: `B036, B035`.
- Immediate browser consumer: `F057`. Do not widen this API for an unnamed future screen.
- Visible outcome: A tenant publishes its store name, support details and customer policy pages instead of the hard-coded “فروشگاه” shell.

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
- Use a transaction where more than one persisted invariant changes. Handle the named race with a database constraint/row lock/guarded update, not only an earlier `AnyAsync`.
- Thread `CancellationToken` through new I/O. Use `TimeProvider` where this task adds time-dependent behavior.
- Log stable IDs and reason codes; never log credentials, bearer tokens, phone numbers, tracking codes, coupon codes, gateway authority or customer address.

## Integration tests required

null initial read; create/update/reload; stale and concurrent create; tenant isolation; permission/Owner bypass; plain text and length validation; invalid external URL; unpublished public 404; published public response omits internal version.

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
