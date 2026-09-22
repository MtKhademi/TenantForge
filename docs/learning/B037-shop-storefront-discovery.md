# B037 — Storefront search, sorting and sale discovery

A guest can now browse a tenant's whole active catalog through one anonymous
endpoint, searching by name, filtering by category or sale, and choosing a
deterministic sort order. This is the direct request path for `F055`.

## 1. Files changed and why

Production:

- `features/storefront/StorefrontCatalogFeature.cs` — added the
  `GET /api/shop/{tenantId}/products` handler, the `StorefrontSortOption`
  enum, `TryParseStorefrontSort`, and the inline card-price projection. Also
  extended the existing category-products handler so it fills the new summary
  members (`isOnSale`, `isSoldOut`, `thumbnailUrl`) truthfully — the record
  is shared with that route.
- `features/storefront/StorefrontContracts.cs` — extended
  `StorefrontProductSummaryResponse` with `IsOnSale`, `IsSoldOut`,
  `ThumbnailUrl` (kept `Images` so the B027 category route's contract is not
  broken).
- `features/pagination/PaginationSupport.cs` — added a cancellation-aware
  `PageAsync(queryable, query, ct)` overload so the new route's `Count`/page
  I/O observe the request token; the existing no-token overload now forwards
  to it.

Tests:

- `ShopStorefrontCatalogIntegrationTests.cs` — 10 new facts (one per Spec
  scenario) plus small helpers: pricing-capable `CreateProductAsync` overload,
  a real-JPEG image uploader (B036 media API), and a non-null list reader.

Docs: `docs/design/shop/http-contracts.md` (S33), `docs/modules/SHOP.md`
(endpoint catalog 28→29, test map, limitations), `tasks/TASKS.md` (row →
`review`), and this note.

## 2. Request flow

1. `TsidId.TryParse(tenantId)` — a malformed segment is a clean `404`.
2. `PaginationSupport.TryBind` — invalid `pageNumber`/`pageSize` → `400`.
3. `TryParseStorefrontSort` — anything outside the four values → `400` with a
   `sort` problem key.
4. `categorySlug` (if present) resolves to an active same-tenant category or
   `404`.
5. The base query applies, in order: `TenantId`, `IsActive`, and the
   active-category `Any(...)` subquery — before any optional filter and before
   `Count`.
6. Optional filters: `q` (trimmed, truncated to 100, `ILIKE`), `categoryId`,
   `saleOnly`.
7. The projection computes, per product, the card price as a correlated
   `MIN(...)` over in-stock variants (`PriceOverride ?? BasePrice`), then
   `IsSoldOut`/`DisplayPrice`/`IsOnSale`.
8. Ordering: `IsSoldOut` first (sold-out last), then the chosen sort, then a
   final `Id` tie-break.
9. `PageAsync` (`Count` + `Skip`/`Take`), then per-page galleries are loaded
   and mapped into `StorefrontProductSummaryResponse`.

## 3. Backend concepts

- **Correlated scalar subquery in a projection.** The card price is one SQL
  `MIN(...)` subquery that the same value reuses for the `saleOnly` filter, the
  price sorts, and the response — one definition, no N+1 re-query.
- **EF Core translation limits.** A *method call* returning an
  `IQueryable` is not recognized as a translatable subquery; the `Min` had to
  be written **inline** in the projection, or EF throws
  `InvalidOperationException` at request time.
- **`decimal?` for an empty aggregate.** C# `Min()` on a non-nullable sequence
  cannot express "no rows", so the subquery returns `decimal?`; a NULL means
  sold out and the display falls back to `BasePrice`.
- **LINQ ordering semantics.** `OrderBy` after `OrderBy` **replaces** the
  first ordering; `ThenBy` appends. Sold-out-last must be the *primary* key, so
  the chosen sort and the `Id` tie-break are `ThenBy`/`ThenByDescending`.
- **Time-sortable TSID.** `newest` is simply `Id` descending.
- **Bound-parameter search.** `q` is trimmed and truncated *before* reaching
  the query, so the pattern is a captured constant Npgsql sends as a parameter
  — never concatenated SQL.

## 4. Security decisions

- Anonymous read, tenant-scoped **only** by the `{tenantId}` segment and the
  `IsActive`/active-category filters in the first SQL predicates (default-deny
  is the `TenantId` filter itself).
- No SKU and no raw stock count ever leave the endpoint; stock is reduced to
  the boolean `isSoldOut`.
- Unknown/inactive/cross-tenant `categorySlug` and malformed `tenantId` return
  the same generic, non-leaking `404`.
- Invalid `sort`/pagination are RFC 7807 `400`s, not 500s.
- The request `CancellationToken` is threaded through category lookup,
  pagination and gallery loads (abort-aware).

## 5. Alternatives deliberately postponed

- **Full-text / trigram search** — a `ILIKE '%q%'` name match is enough for the
  browser demo; a real search index is an explicit non-goal.
- **A `Shop.Contract` project** — no second .NET consumer of these shapes
  exists yet, so the record stays beside the feature.
- **Popularity/rating/recommendation sorts, tags, faceted color/size filters,
  subcategory hierarchy** — later slices, not this one.
- **A generic sort-enum abstraction** — a small private enum + switch is the
  direct, readable choice for four values.

## 6. Commands and manual steps

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test TenantForge.sln --nologo --filter FullyQualifiedName~ShopStorefrontCatalogIntegrationTests
dotnet.exe test TenantForge.sln --nologo
```

Manual (Docker running): start the API, create a tenant with a few products,
then:

```
GET /api/shop/{tenantId}/products?sort=price-asc
GET /api/shop/{tenantId}/products?q=<name fragment>&saleOnly=true
GET /api/shop/{tenantId}/products?sort=popularity        # 400, "sort" key
```

Confirm: sold-out products always last, equal prices tie-break by id, and an
unknown `categorySlug` is a `404`.

## 7. Review questions

1. Why must the card-price `Min` be written inline in the projection instead of
   via a helper method that returns an `IQueryable`? What happens at runtime if
   you keep the helper?
2. If the chosen sort were applied with `OrderBy` instead of `ThenBy`, which
   product (if any) could move above a sold-out product, and why?
3. The category-products route and this all-products route share
   `StorefrontProductSummaryResponse`, but `effectivePrice` means `BasePrice`
   on one and the in-stock card price on the other. Is that a defect or an
   acceptable per-route contract, and how would the frontend know which to
   trust?
