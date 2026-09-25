# Host slice — Scalar API reference UI

## 1. Files changed and why

- `src/api/TenantForge.Api/TenantForge.Api.csproj` adds two host-only packages:
  `Microsoft.AspNetCore.OpenApi` (the official OpenAPI document generator,
  replacing the deprecated Swashbuckle generator) and `Scalar.AspNetCore` (the
  interactive reference UI).
- `src/api/TenantForge.Api/Program.cs` wires three calls:
  `builder.Services.AddOpenApi()` before `Build`, then `app.MapOpenApi("openapi/v1.json")`
  and `app.MapScalarApiReference()` after every module has mapped its routes.
- `docs/knowledge/AGENT-backend.md` records the new fast-fact row (the two
  routes and the host-only wiring).
- `docs/knowledge/HUMAN-backend.md` explains the reference and how to open it.

## 2. Request flow from endpoint to response

No business endpoint changed. The document is produced on demand:

1. At startup `AddOpenApi()` registers the document services; the host never
   calls the deprecated Swashbuckle generator.
2. `MapOpenApi("openapi/v1.json")` maps the document route; `MapScalarApiReference()`
   maps the UI route (`/scalar/v1`). Both are registered last, so every IAM,
   Shop and health route the modules mapped earlier is already present when the
   document is generated.
3. A browser hits `/scalar/v1`, which fetches `openapi/v1.json` and renders the
   interactive reference from that live document.

## 3. Backend concepts introduced

- .NET 9/10's built-in OpenAPI support: the document is generated from the
  already-registered endpoints, so the reference always matches the real routes
  without any hand-maintained spec.
- The document route is what Scalar reads; if you add `AddOpenApi` but forget
  `MapOpenApi`, the UI loads but its document 404s.
- A third-party UI package (`Scalar.AspNetCore`) needs its own `using
  Scalar.AspNetCore;` in `Program.cs`; the built-in OpenAPI extensions live in
  `Microsoft.AspNetCore.Builder`, which `WebApplication` already imports.

## 4. Important security decisions

- The reference is host-only and read-only; it adds no business endpoint and
  changes no request/response contract, auth policy or permission check.
- The OpenAPI document lists routes but exposes no data, secrets or tokens.
- No module code, `BuildingBlocks`, IAM or Shop contract, or migration changed.

## 5. Alternatives deliberately postponed

- Swashbuckle's own generator was not used; it is deprecated in favor of the
  built-in `Microsoft.AspNetCore.OpenApi`.
- The UI is not gated behind an environment flag — the host serves it in every
  environment. Gating is a deliberate follow-up if the host ever ships to a
  public network.
- No client code generation (a reason to pick NSwag instead) — not needed for a
  local demo reference.

## 6. Commands and manual steps to verify

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test                       # whole integration suite (needs Docker)
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run \
  --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

With the API running, from the WSL host curl the gateway IP (a Windows process
is not reachable from WSL on `127.0.0.1`):

- `curl http://<gateway-ip>:5000/health` → `200`
- `curl http://<gateway-ip>:5000/openapi/v1.json` → `200`, valid OpenAPI 3.1 with the full route list
- `curl http://<gateway-ip>:5000/scalar/v1` → `200`, the interactive UI

Open `http://localhost:5000/scalar/v1` in the browser from Windows to see the
rendered reference.

## 7. Review questions

1. Why does `MapOpenApi` have to be called separately from `AddOpenApi`, and what
   breaks in the UI if it is omitted?
2. Why is the document and UI mapped after `UseIamModuleAsync` and
   `UseShopModuleAsync`, not before?
3. Why did we choose `Microsoft.AspNetCore.OpenApi` + `Scalar` over Swashbuckle,
   and when would NSwag be the better fit?
