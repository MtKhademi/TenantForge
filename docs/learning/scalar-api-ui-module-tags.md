# Host slice — group the API reference by module (IAM / Shop / Host)

Follow-up to `scalar-api-ui.md` (the same host feature). This slice makes the
Scalar reference browsable per module by giving every operation one module tag.

## 1. Files changed and why

- `src/api/TenantForge.Api/Program.cs` — `AddOpenApi` now takes an options
  callback that registers one `AddDocumentTransformer`. After the generator
  builds the document it walks `document.Paths` and, for every operation,
  replaces the generator's default tag with the owning module's tag. A small
  static `ModuleTagForPath` maps a path to its tag: a `/shop` segment → `Shop`
  (both the public `/api/shop/{tenantId}/...` and the tenant-scoped admin
  `/api/tenants/{tenantId}/shop/...` prefixes), `/health` → `Host`,
  everything else → `IAM`.
- `docs/knowledge/AGENT-backend.md` — the API-docs fast-fact row now records
  that a document transformer tags operations by module.
- `docs/knowledge/HUMAN-backend.md` — the API-reference section now says the
  reference is grouped per module.

## 2. Request flow from endpoint to response

No endpoint changed. The tag is a document-level concern applied on demand:

1. A client (or the Scalar UI) requests `/openapi/v1.json`.
2. The generator builds the document from the registered endpoints.
3. The host's document transformer runs over the finished document and sets each
   operation's `tags` to exactly its module.
4. `/scalar/v1` fetches the document and renders one section per tag, so IAM,
   Shop and Host appear as three groups in the sidebar.

## 3. Backend concepts introduced

- `IOpenApiDocumentTransformer` (via `OpenApiOptions.AddDocumentTransformer`)
  runs against the whole finished document, after all operations exist — the
  right seam for a rule that is about paths, not a single operation.
- Tagging is done in the host, not with `.WithTags(...)` on each route: it is a
  presentation concern, it is computed from the real paths so it cannot drift
  from them, and it keeps module code untouched.
- In the built-in OpenAPI support a tag is an `OpenApiTagReference`
  (name, document, reference-id), not an `OpenApiTag`. The default per-operation
  tag is the assembly name (`TenantForge.Api`); replacing the set with a single
  module tag is what keeps each operation under one group.

## 4. Important security decisions

- Documentation-only. No route, request/response, auth, permission, tenancy,
  migration or module code changed; the document still exposes routes only, no
  data or secrets.

## 5. Alternatives deliberately postponed

- `.WithTags(...)` on each of the 59 route registrations (19 IAM + 40 Shop) was
  rejected: invasive, touches both modules, and would duplicate a rule that a
  path prefix already encodes.
- Finer sub-groups (Storefront / Cart / Orders inside Shop) were rejected: the
  user asked for module-level groups only; sub-grouping is a follow-up if it is
  wanted.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test                       # whole integration suite (needs Docker)
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run \
  --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

With the API running, from the WSL host curl the gateway IP (a Windows process
is not reachable from WSL on `127.0.0.1`) and inspect the tags:

- `curl http://<gateway-ip>:5000/openapi/v1.json` → `200`; every operation has
  exactly one tag, and the tag totals are IAM 19 / Shop 40 / Host 1 (60 ops).
- `curl http://<gateway-ip>:5000/scalar/v1` → `200`; the sidebar shows IAM,
  Shop and Host as three groups.

## 7. Review questions

1. Why is a document transformer the right place to add module tags instead of
   `.WithTags(...)` on each endpoint, and what does that cost you?
2. Why does the path rule test for a `/shop` segment rather than a full prefix,
   and what would break if Shop ever added a route that does not contain it?
3. In the built-in OpenAPI support, why is a tag an `OpenApiTagReference`
   (name, document, id) rather than a plain string?
