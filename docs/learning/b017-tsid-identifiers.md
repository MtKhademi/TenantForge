# B017 — Replace persisted IAM GUID identifiers with TSIDs

## 1. Files changed and why

- `src/modules/iam/TenantForge.Modules.Iam/domain/IamId.cs` — added the IAM-owned identifier seam for generating, parsing and formatting TSIDs. It rejects malformed, GUID-shaped, decimal-only and all-zero values without throwing.
- IAM domain entities under `src/modules/iam/TenantForge.Modules.Iam/domain/` — replaced persisted identity and foreign-key values from `Guid` to `Tsid`.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/TsidValueConverter.cs` and IAM entity maps — persist `Tsid` values as PostgreSQL `bigint`, while keeping domain code strongly typed.
- `src/modules/iam/TenantForge.Modules.Iam/infrastructure/Migrations/20260914120008_TsidIdentifiers.cs` — migrates existing UUID identity columns to generated TSID bigint values and rewires IAM primary keys, foreign keys and indexes in one guarded migration.
- IAM feature endpoints under `src/modules/iam/TenantForge.Modules.Iam/features/` — parse route/body/JWT identifiers with `IamId.TryParse` and emit only canonical 13-character TSID strings in JSON and locations.
- `tests/integration/TenantForge.Api.IntegrationTests/**` — updated integration tests from GUID public IDs to TSID strings, added focused `IamId` coverage, and proved migration execution against PostgreSQL.

## 2. Request flow from endpoint to response

A browser request now crosses an explicit identifier boundary:

1. HTTP route/body/JWT data arrives as text, for example `/api/tenants/0123456789ABC/members` or a JWT `sub` claim.
2. The endpoint calls `IamId.TryParse` before querying. Bad input returns `401`, `403` or `400` depending on the existing endpoint contract; it does not fall through to EF and does not throw package exceptions.
3. Inside IAM, handlers, domain entities and EF queries use `Tsid` values.
4. EF converts each `Tsid` to a `long` through `TsidValueConverter`, so PostgreSQL stores identity and foreign-key columns as `bigint`.
5. Responses call `IamId.Format(...)`, so JSON IDs and JWT subjects stay canonical 13-character TSID strings and never expose the backing integer.

## 3. Backend concepts introduced

- **Boundary representation vs. storage representation.** Public IDs are strings because clients should not know the database encoding. The database stores bigint because TSIDs are 64-bit sortable values.
- **EF Core value converters on key properties.** `TsidValueConverter` translates the domain type to `long` for persistence. The maps apply the converter explicitly on ID/FK properties so EF keeps keys and relationships coherent.
- **Guarded irreversible migrations.** The migration creates a temporary old UUID to new bigint map, backfills all PK/FK columns, asserts no missing mappings, then swaps columns and constraints.
- **A single parsing seam.** Endpoint code does not call the TSID package directly. `IamId` owns all acceptance rules, which makes security review and test coverage much easier.

## 4. Important security decisions

- **Default deny on invalid identity.** GUID-shaped JWT subjects and decimal-looking subjects are rejected; they do not become anonymous fallback identities or database lookups.
- **No bigint leakage.** HTTP responses, `Location` headers and JWT subjects emit only canonical TSID strings. Tests cover that public IDs parse through `IamId`, not `Guid.TryParse`.
- **No client-provided backing integer.** `IamId.TryParse` rejects all-digit 13-character input so a client cannot send what looks like a bigint ID even if the text is valid Crockford-base32.
- **Tenant authorization remains server-side.** This slice changed identifier representation only; membership and permission checks still run on the server before tenant-scoped data is returned.
- **Migration fails loudly on unsafe state.** The migration raises exceptions if any generated value is non-positive, duplicated, or if any PK/FK mapping is missing before destructive column swaps.

## 5. Alternatives deliberately postponed

- **Changing invitation tokens away from `Guid.NewGuid()` text** — invitation tokens are random secrets, not IAM database identifiers. They remain out of scope for B017.
- **A global application-wide ID abstraction** — this task is IAM-scoped. Other modules can adopt their own seams when they have a visible slice requiring it.
- **Reversible UUID rollback migration** — once UUID identity columns are replaced and removed, recovering the exact old values requires backup restore. The migration documents this by making `Down()` unsupported.
- **Client/frontend changes** — the accepted contract keeps IDs as strings on the wire, so no frontend layout or interaction work was needed in this backend slice.

## 6. Commands and manual steps to verify the slice

Automated verification run in this task:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter "FullyQualifiedName~IamIdTests" --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --filter "FullyQualifiedName~PermissionKeyMigrationTests" --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Expected results from this slice:

- solution build: `0 Warning(s)`, `0 Error(s)`;
- `IamIdTests`: 20 passing tests;
- migration test: 1 passing PostgreSQL migration test;
- full integration suite: 108 passing tests.

Manual browser/API demo outline:

1. Start PostgreSQL for the backend.
2. Run the API host with `dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj` for normal Development smoke testing.
3. Login through `/api/auth/login` with the development admin credentials.
4. Confirm the response `user.id` and JWT `sub` are 13-character TSID strings, not GUIDs and not decimal numbers.
5. Call `/api/auth/me`, `/api/auth/me/tenants`, and one tenant-scoped endpoint with returned string IDs.
6. Try a GUID-shaped tenant ID or JWT subject and confirm the API fails closed instead of returning tenant data.

## 7. Three review questions

1. Why does `IamId.TryParse` reject a 13-character all-digit value even though those characters are technically valid Crockford-base32 text?
2. Why do the domain and EF model use `Tsid` while JSON and JWTs use strings instead of sharing one representation everywhere?
3. In the migration, why is it safer to backfill temporary bigint columns and assert every mapping before dropping the old UUID columns?
