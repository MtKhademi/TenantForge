---
id: B016
slice: S18
title: Collapse IAM startup behind one registration and one activation seam
agent: backend-mentor
source: tasks/slices/018-iam-module-composition-seam.md
---

# Objective

Make the API host compose IAM through only two module-owned operations: service
registration before `Build` and asynchronous activation after `Build`. Hide
IAM configuration validation, middleware installation, database migration,
idempotent seeding and endpoint mapping inside IAM without changing observable
HTTP behavior.

## Required final host shape

Preserve host-owned setup while reducing IAM-specific knowledge in
`src/api/TenantForge.Api/Program.cs` to these exact calls:

```csharp
builder.Services.AddIamModule(builder.Environment);

var app = builder.Build();

await app.UseIamModuleAsync();
```

`UseIamModuleAsync` is intentionally asynchronous because EF migration and seed
work are asynchronous. Never replace it with blocking sync-over-async. Read the
complete ordering and ownership contract in the S18 source before planning.

## Context and invariants

- Currently `Program.cs` calls validation, seeding and endpoint mapping
  separately and installs auth middleware itself. Those are IAM details leaking
  through the host boundary.
- `AddIamModule` stays registration-only. Lazy configuration binding must still
  see sources injected by `WebApplicationFactory`.
- Validation remains after `builder.Build()`; moving it into registration is a
  regression.
- Activation order is security-sensitive: validate; authentication;
  authorization; migrate; seed; map all IAM endpoints.
- `Program.cs` continues to own CORS, health and `Run`. Do not move unrelated
  host behavior into IAM.
- Preserve all HTTP and persistence contracts. No migration/frontend change.

## Scope and expected files

Expected primary edits:

- `src/api/TenantForge.Api/Program.cs`
- `src/modules/iam/TenantForge.Modules.Iam/IamModule.cs`
- focused integration test files
- `docs/architecture.md`
- `.opencode/agents/backend-mentor.md`
- `.opencode/skills/vertical-slice-delivery/SKILL.md`
- `docs/learning/B016-iam-module-composition-seam.md`

Touch `IAMConfig.cs`, `IModuleConfig.cs` or test-host setup only if required by
the two-phase boundary, and explain why first. Do not add an architecture-test
package: reflection and existing test infrastructure are enough.

## Implementation requirements

1. Keep `IamModule` as the single public static host seam.
2. Expose only `AddIamModule` and `UseIamModuleAsync` as its public static
   composition methods. Make old public operations private/internal or inline.
3. Make activation an extension for the built `WebApplication`, returning
   `Task` or `Task<WebApplication>` consistently.
4. Obtain environment, configuration and services from the built app; the host
   must not pass them again.
5. Use a correctly disposed async-capable scope and await migration/seeding.
6. Preserve structured logging; never log credentials, keys, tokens or hashes.
7. Install middleware and map endpoints once. Never call `Run` in the module.
8. Do not invent a generic lifecycle framework for one module.

## Required tests

- Add a reflection-based test asserting that `IamModule` declares only the two
  intended public static composition methods, including meaningful signatures.
- Prove activation honors late `WebApplicationFactory` configuration and
  current Production fail-closed behavior.
- Prove fresh startup migrates/seeds before successful login and repeat startup
  is idempotent.
- Run the entire existing integration suite to catch missing routes,
  middleware-order, tenant-isolation or authorization regressions.
- Never rewrite unrelated tests just to obtain green output.

## Acceptance evidence

- Show final `Program.cs` and classify every remaining line as Host-owned or one
  of the two allowed IAM calls.
- Show focused seam/startup test results and complete build/test totals.
- Demonstrate real health, Development login, refresh/session recovery and an
  unauthenticated protected request returning `401`.
- Show `git diff --check`, `git status --short` and the final scoped diff.
- Confirm no frontend, migration, package, HTTP-contract or unrelated change.

## Explicitly out of scope

- F022 or any frontend work.
- New endpoints or request/response changes.
- Generic multi-module discovery/orchestration.
- Production deployment/migration redesign.
- Refresh tokens, logout revocation or other auth expansion.

## Verification

Run separately from the repository root:

```text
dotnet.exe build TenantForge.sln
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj
```

Then perform the S18 real browser/API demo. If PostgreSQL, Windows .NET interop
or browser access is blocked, report the exact blocker and leave that criterion
incomplete.

## Lifecycle

Keep this Spec while B016 is `planned`, `in_progress` or `review`. After final
delivery approval, change only B016 to `done`, replace its Spec with `—` and
delete exactly this file in the delivery commit. Preserve S18 and Git history.
