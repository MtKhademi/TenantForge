# S18 — Keep IAM composition inside the IAM module

## Executable tasks

- Front: None
- Backend: `B016`

## Owner

`backend-mentor`

## Depends on

- `B015`

## Visible outcome

The existing application starts and behaves exactly as before: health, login,
session recovery and protected IAM screens continue to work. The API host no
longer knows how IAM validates configuration, installs authentication and
authorization middleware, migrates or seeds its database, or maps its feature
endpoints.

This is a behavior-preserving architecture slice with existing browser-visible
consumers. It adds no screen or endpoint.

## Demo

1. Start PostgreSQL, the API and the current web application from a clean database.
2. Observe `GET /health` returning `200 ok`.
3. Sign in with the documented Development platform administrator and observe
   the existing dashboard.
4. Refresh the browser and observe authenticated session recovery.
5. Request a protected IAM endpoint without a bearer token and observe `401`.
6. Restart against the same database and confirm startup succeeds without a
   duplicate administrator or destructive reseeding.

The demo must use the real API and database and take less than three minutes
after infrastructure is available.

## API contract

No HTTP contract changes. Routes, methods, request/response bodies, status
codes, claims, authorization behavior and pagination contracts remain
serialization-compatible.

The host-to-module composition contract becomes exactly:

```csharp
builder.Services.AddIamModule(builder.Environment);

var app = builder.Build();

await app.UseIamModuleAsync();
```

These are the only IAM-specific composition calls allowed in `Program.cs`.
Host-owned concerns remain explicit in the host: CORS registration/use, health
mapping and `app.Run()`. Their relative order must remain correct.

`AddIamModule` owns registration only and performs no database I/O or premature
configuration decision. `UseIamModuleAsync` owns, in this deterministic order:

1. configuration validation using the fully built application's environment
   and configuration;
2. authentication middleware;
3. authorization middleware;
4. pending IAM migrations;
5. idempotent IAM seed work;
6. all IAM endpoint mapping.

Database work remains genuinely asynchronous. Do not create a synchronous
`UseIamModule()` wrapper using `.Wait()`, `.Result` or
`.GetAwaiter().GetResult()`. Do not hide startup in a hosted service merely to
avoid awaiting it.

The public IAM seam must not expose separate host-callable methods such as
`ValidateIamModuleConfiguration`, `SeedIamModuleAsync` or `MapIamModule` after
this slice. They may become private/internal helpers called by activation.

## Backend learning goal

Teach the difference between module service registration and module runtime
activation, and why configuration validation and asynchronous startup work
belong behind the module boundary after `builder.Build()`.

## Scope

- Refactor the IAM public seam to one registration method and one asynchronous
  activation method.
- Move every IAM-owned startup responsibility out of `Program.cs`.
- Preserve late test configuration support: validation remains after Build.
- Preserve fail-closed authentication when no signing key is configured.
- Preserve migration-before-seed and seed-before-serving ordering.
- Keep IAM feature types/helpers internal unless an existing constraint proves
  a public type is required.
- Update current architecture documentation and agent/skill guidance that still
  teaches the old three-call composition contract.
- Add focused integration/architecture coverage protecting the seam and
  activation behavior.
- Add the normal B016 backend learning note.

## Out of scope

- No endpoint, DTO, route, database schema or migration change.
- No frontend edit, redesign or pagination work.
- No generic module framework, reflection discovery or lifecycle orchestrator.
- No shared base abstraction for modules that do not yet exist.
- No JWT lifetime, claim, password, CORS or seed-data change.
- No hosted service for migrations/seeding.
- No production deployment or migration-strategy redesign.

## Acceptance criteria

- [ ] `Program.cs` has one IAM registration call before Build:
      `builder.Services.AddIamModule(builder.Environment)`.
- [ ] `Program.cs` has one IAM activation call after Build:
      `await app.UseIamModuleAsync()`.
- [ ] The host does not separately invoke IAM validation, auth middleware,
      migration, seeding or endpoint mapping.
- [ ] Activation validates after Build, installs auth middleware, migrates,
      seeds idempotently and maps every IAM feature exactly once.
- [ ] Late-injected test configuration and Production fail-closed behavior are
      preserved.
- [ ] Fresh startup creates the development administrator once; restart neither
      duplicates nor overwrites it.
- [ ] Existing login, current-account, platform, tenant-isolation, permission,
      invitation, audit and pagination integration tests pass.
- [ ] A focused test protects both intended public static seam methods and
      activation behavior without brittle source-text assertions.
- [ ] `dotnet.exe build TenantForge.sln` and the full backend tests pass.
- [ ] The browser demo proves health, login, refresh and unauthenticated `401`
      with no new console error.
- [ ] Architecture docs, backend agent and vertical-slice skill describe the
      two-phase contract and no active guidance teaches the removed contract.
- [ ] `docs/learning/B016-iam-module-composition-seam.md` explains files,
      startup order, security, alternatives, verification and three questions.
- [ ] No unrelated or future work is started.
