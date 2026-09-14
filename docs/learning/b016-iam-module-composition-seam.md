# B016 — Collapse IAM startup behind one registration and one activation seam

## 1. Files changed and why

- `src/api/TenantForge.Api/Program.cs` — removed the three separate IAM calls
  the host used to make after `Build()` (`ValidateIamModuleConfiguration`,
  `UseAuthentication`/`UseAuthorization`, `SeedIamModuleAsync`, `MapIamModule`).
  The host now knows about IAM only as `AddIamModule` (before `Build`) and
  `await UseIamModuleAsync()` (after `Build`). CORS, health mapping and `Run`
  stay host-owned, unchanged.
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs` — added the new public
  `UseIamModuleAsync(this WebApplication app)` extension method that performs,
  in order: configuration validation, authentication middleware, authorization
  middleware, migration, idempotent seeding, endpoint mapping. The four
  methods that used to be public (`ValidateIamModuleConfiguration`,
  `SeedIamModuleAsync`, `MapIamModule`) became `private`, called only from
  `UseIamModuleAsync`. `AddIamModule` is unchanged.
- `tests/integration/TenantForge.Api.IntegrationTests/IamModuleCompositionTests.cs`
  (new) — a reflection test locking the module's public static surface down to
  exactly `AddIamModule` and `UseIamModuleAsync` with the expected signatures,
  plus two tests that exercise the real host: first login after fresh startup,
  and a second independent host built against the same database to prove
  restart does not duplicate the seeded administrator.
- `tests/integration/TenantForge.Api.IntegrationTests/IamDbFixture.cs` — added
  `CompositionSeamDbFixture` + its xunit collection, an isolated database for
  the new tests (kept off the shared fixture so a second seeded admin never
  disturbs another test class's row-count assumptions).
- `docs/architecture.md`, `.opencode/agents/backend-mentor.md`,
  `.opencode/skills/vertical-slice-delivery/SKILL.md` — updated the module
  composition section/guidance from the old three-call contract to the
  two-phase contract this slice introduces.

## 2. Request flow: from `Program.cs` to a served request

Before this slice, `Program.cs` had to know about five distinct IAM concerns
and get their order right itself. Now:

```csharp
builder.Services.AddIamModule(builder.Environment);   // registration, pre-Build
var app = builder.Build();
app.UseCors();                                        // host-owned
await app.UseIamModuleAsync();                        // activation, post-Build
app.MapHealth();                                      // host-owned
app.Run();
```

`UseIamModuleAsync` is the only thing the host calls that knows about IAM
internals. Inside it, in this fixed order:

1. `ValidateIamModuleConfiguration` — reads the fully assembled
   `app.Configuration` (every source, including `WebApplicationFactory` test
   overrides, has been merged by now) and throws if IAM's connection string or
   seed section is missing/malformed/unsafe. This must run first: nothing
   downstream (migration, seeding, serving requests) should happen against
   configuration that is already known to be wrong.
2. `app.UseAuthentication()` / `app.UseAuthorization()` — installs the JWT
   bearer scheme and the `PlatformAdmin` policy into the middleware pipeline.
   Must run before endpoint mapping, or `RequireAuthorization()` on IAM routes
   would have no middleware to enforce it.
3. `SeedIamModuleAsync` — applies pending EF Core migrations, then seeds the
   platform administrator if configured and not already present. Must run
   after validation (the connection string is now known-good) and before
   endpoint mapping (nothing should be reachable against an unmigrated table).
4. `MapIamModule` — maps every IAM feature's endpoints. Runs last so the
   pipeline (CORS, auth) is fully assembled before any route becomes
   reachable.

A request to, say, `/api/auth/me` is unaffected by this refactor: it still
hits the same middleware pipeline and the same endpoint handler. What changed
is only *how the host assembles that pipeline* at startup, not what happens
once a request arrives.

## 3. Backend concepts introduced

**Registration vs. activation as two distinct module-composition phases.**
`AddIamModule` is a *pure DI registration* step: it can run before
`WebApplication.CreateBuilder` has consumed every configuration source (host
`appsettings.*.json`, environment variables, and — critically for tests —
`WebApplicationFactory`'s in-memory overrides), because it never reads a
configuration *value*, only registers *how* to resolve one later. Activation
is a genuinely different kind of step: it needs the configuration to be fully
assembled (to validate it), it needs the DI container to be built (to resolve
scoped services like the seeder), and it performs real asynchronous I/O
(EF Core migration and inserts). Conflating the two — e.g. validating during
registration — would validate a configuration that is not yet final and would
silently pass in the host while still being wrong once
`WebApplicationFactory` finishes injecting test values afterward.

**A module's public seam as an explicit, minimal, testable contract.** Before
this slice, `IamModule` had four public static members; a caller (or a test)
could reach any of them individually, in any order, which is exactly what
made ordering a house-of-cards responsibility living in `Program.cs` instead
of the module. Reducing the public surface to two methods — one the host
calls once before `Build`, one it calls once after — moves "what order do IAM's
startup steps run in" from *host knowledge* to *module implementation detail*,
and the reflection test in `IamModuleCompositionTests.cs` makes that boundary
enforceable rather than just documented.

## 4. Important security decisions

- **Fail-closed validation still runs first, unconditionally, before anything
  else in activation.** Nothing changed here in effect — I preserved the
  existing invariant (validation before migration, seeding or mapping) by
  keeping it as literally the first statement inside `UseIamModuleAsync`.
- **Authentication/authorization middleware installation still precedes
  endpoint mapping.** If this order were reversed, IAM's protected endpoints
  would be registered against a pipeline that has no auth middleware yet,
  which would either let unauthenticated requests through or throw at request
  time depending on ASP.NET Core internals — either way, a security
  regression. The new `UseIamModuleAsync` preserves the exact prior sequence.
- **No new logging was added, and the existing rule against logging
  credentials/tokens/hashes is unaffected** — the only log statements
  (seeding outcome + email) are unchanged, just now called from a private
  method instead of a public one.
- **The public composition surface itself is now a security-relevant
  boundary**: a smaller public API means fewer ways for a future change to
  accidentally call, e.g., `MapIamModule()` before auth middleware is
  installed. Locking the surface down with a reflection test converts "please
  don't call these out of order" from a code-review convention into something
  a test suite actively defends.

## 5. Alternatives deliberately postponed

- **A generic multi-module lifecycle/discovery framework** (e.g. an
  `IModule` interface with `RegisterAsync`/`ActivateAsync` that every future
  module implements, discovered via reflection or a DI-scanned list) — there
  is exactly one module today; building that abstraction now would be
  speculative generality with no second consumer to validate the design
  against. The Spec explicitly rules this out.
- **A hosted service to run migration/seeding in the background** — this
  would let the host "start" before IAM is actually ready to serve traffic
  correctly, silently reintroducing the exact race the current synchronous
  `await` in `Program.cs` prevents. Rejected per the Spec's explicit
  prohibition.
- **Making `UseIamModuleAsync` synchronous with `.Result`/`.Wait()`** — would
  reintroduce a classic ASP.NET Core deadlock risk and was explicitly
  forbidden by the Spec; the two-phase seam stays asynchronous end to end.

## 6. Commands and manual steps to verify this slice

Build and full test suite:

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
```

Expected: `0 Warning(s)`, `0 Error(s)`; **86/86** tests passed (82 pre-existing
+ 4 new composition-seam tests).

Real host demo (requires PostgreSQL from `docker-compose.yml` running):

```bash
dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --no-build
```

Then, from WSL, through the Windows-host gateway IP (`ip route` → `default via <gw>`):

```bash
curl http://<gw>:5000/health                       # -> 200 "ok"
curl http://<gw>:5000/api/auth/me                   # -> 401 (no bearer token)
curl -X POST http://<gw>:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@tenantforge.local","password":"local-development-password"}'
  # -> 200 with accessToken
curl http://<gw>:5000/api/auth/me -H "Authorization: Bearer <accessToken>"
  # -> 200 (session recovery)
```

Stop the process, start it again against the same database: the log shows
"No migrations were applied. The database is already up to date." and
"Platform administrator seeding already-present", and login still succeeds —
proving restart idempotency at the real host level, not just in tests.

## 7. Three review questions

1. `ValidateIamModuleConfiguration` runs as the first line inside
   `UseIamModuleAsync`, which itself runs after `builder.Build()`. Why would
   moving that validation call into `AddIamModule` (pre-`Build`) risk letting
   a broken configuration pass validation in a `WebApplicationFactory` test
   host?
2. `UseIamModuleAsync` installs `app.UseAuthentication()` / `UseAuthorization()`
   before it maps any IAM endpoint. What concretely could go wrong for a
   protected endpoint like `/api/auth/me` if endpoint mapping happened first?
3. The reflection test in `IamModuleCompositionTests.cs` asserts `IamModule`
   has exactly two public static methods with specific signatures. What kind
   of accidental regression would this test catch that a normal integration
   test (hitting real HTTP endpoints) would not?
