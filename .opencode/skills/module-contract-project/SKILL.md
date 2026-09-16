---
name: module-contract-project
description: Split a TenantForge module into `Module` (implementation) and `Module.Contract` (its public HTTP request/query/response surface), so other modules depend only on the contract. Use when creating a module's first Contract project, when a second module needs to reference another module's request/response shapes, or when reviewing a diff that touches any `*.Contract` project.
---

# Module contract project

## What this pattern is for

Every business module in TenantForge (today: IAM; more will follow) is a
class library with domain entities, EF Core persistence, feature handlers
and endpoint mapping. Almost all of that is implementation detail. The
small part that matters to the rest of the system is the shape of the
HTTP requests it accepts and the responses it returns.

`Module.Contract` is a second, deliberately tiny class library beside
`Module` that holds exactly that shape — nothing else — so:

- the API host and any future module can depend on "what does `Module`
  promise" without pulling in `Module`'s EF Core, Npgsql, migrations,
  seeding and feature-handler internals;
- what is a public promise and what is an implementation detail becomes
  an explicit, reviewable, test-locked boundary instead of an accident of
  which `internal`/`public` modifier someone typed;
- a module can be read, reasoned about and changed by an agent that never
  needs to open its `.Contract` project's consumer to know what will
  break.

This is a different problem from `TenantForge.BuildingBlocks`.
BuildingBlocks holds primitives every module needs to agree on
(`TsidId`, `IModuleConfig`) and only admits a type after real
cross-module evidence. `Module.Contract` holds one module's own
request/response DTOs — it has exactly one natural owner (the module
that defines the endpoints) and is populated the moment that module has
a delivered HTTP contract, not after a second consumer exists. Read
`docs/building-blocks/README.md` to keep the two ideas distinct before
proposing either kind of extraction.

The worked example is IAM: `tasks/slices/023-iam-contract-separation.md`
and `tasks/slices/024-iam-contract-knowledge-base.md` record the decision
and the mechanical move; `docs/contracts/iam.md` (once B024 delivers it)
is the living handbook to imitate for any later module.

## When to create `Module.Contract`

Create it the moment `Module` has at least one delivered, exercised HTTP
request or response type — never earlier. `AGENTS.md`'s "do not create
empty projects or folders for future modules" applies here without
exception: a brand-new module with zero endpoints does not get a
`.Contract` project scaffolded "to be ready." A module that already has
real, tested endpoints (like IAM did when S23 was registered) is not
speculative — S23 relocated existing, delivered code; it invented no new
shape.

## Project shape

- Name: `TenantForge.Modules.<Module>.Contract`, directory
  `src/modules/<module>/TenantForge.Modules.<Module>.Contract/`.
- `net10.0`, `Nullable` and `ImplicitUsings` enabled, **zero**
  `PackageReference`, `FrameworkReference` or `ProjectReference`. If a
  type you are about to move needs something beyond plain
  string/number/bool/collection members, that is a sign it is not a
  contract type — see the admission rule.
- Root namespace `TenantForge.Modules.<Module>.Contract`, one
  sub-namespace/folder per kind:

  | Sub-namespace | Folder | Holds |
  | --- | --- | --- |
  | `...Contract.Requests` | `Requests/` | Types bound from a JSON request body |
  | `...Contract.Queries` | `Queries/` | Types bound from query-string parameters |
  | `...Contract.Responses` | `Responses/` | Types returned to the HTTP caller |
  | `...Contract.Commands` | `Commands/` | A distinct command object, only when one exists that is not already a `Requests` type — reserved, do not create empty |
  | `...Contract.Enums` | `Enums/` | A domain enum actually serialized as an enum (not as a string) at the HTTP boundary — reserved, do not create empty |

  Only create `Commands/`/`Enums/` when a real type belongs there. IAM's
  own delivery (S23) needed none — every mutation is already a `Request`
  type and every status/role crosses HTTP as a plain string — so it has
  only `Requests/`, `Queries/` and `Responses/`. Do not create the other
  two folders "for symmetry."
- Every contract type is `public sealed record`, member names/order/types
  identical to the endpoint's actual JSON shape. No inheritance, no
  generic wrapper, no `IContractType` marker interface — each record
  stands alone.
- `TenantForge.Modules.<Module>` (the implementation project) gets exactly
  one `<ProjectReference>` to `Module.Contract`, in addition to whatever
  else it already references (its `TenantForge.BuildingBlocks` reference,
  if any, is untouched). The API host does not reference `Module.Contract`
  unless it genuinely consumes one of its types directly — normally it
  does not, since it only calls the module's two-method composition seam.

## Admission rule — what moves, what stays

A type belongs in `Module.Contract` only when both are true:

1. it is already part of a real, delivered HTTP request or response shape
   for that module (present in the module's living handbook's endpoint
   catalog, directly or as a nested type of a listed response);
2. it carries no ASP.NET (`HttpRequest`, `IEndpointRouteBuilder`), EF Core
   (`IQueryable`, `DbContext`) or Npgsql dependency, and no reference to
   an `internal` domain/infrastructure type.

Stays module-owned even though it looks related:

- anything that mints, validates or checks credentials/tokens against the
  database before a response type is built (compare IAM's
  `AuthenticatedAccount` — a credential-check result, not itself
  serialized to a caller);
- `HttpRequest`-binding or `IQueryable`/EF-execution helpers (compare
  IAM's `PaginationSupport.TryBind`/`PageAsync` — the plain
  `PaginationQuery`/`PaginationMetadata` records moved, the ASP.NET/EF
  binding code around them did not);
- intermediate, handler-only computation records never returned as-is to
  an HTTP caller (compare IAM's `RolesFeature.TenantAccess`/
  `ActorSnapshot`/`AssignmentValidation`/`RemovedAssignment`);
- domain entities and their enums — a module's persistence/domain model
  is never a contract type merely because it's public-sounding; if an
  enum is genuinely serialized to JSON as an enum (not restated as a
  string, which is IAM's current convention everywhere), only then does
  it earn a place in `Enums/`.

"Might be useful to a future module" and "cleaner" are not admission
evidence, exactly as `docs/building-blocks/README.md`'s own checklist
already insists for BuildingBlocks.

## Mechanical move recipe (apply per type, per task)

Never move more than one coherent batch of types per task — see
`tasks/slices/023-iam-contract-separation.md` for how IAM's ~30 types
were split into three dependency-chained tasks (foundation; then two
feature-area batches) so every diff stays small enough for a
less-experienced agent to execute without reconstructing the whole
module's context.

1. Create the new file under `Module.Contract` at the documented path,
   `namespace TenantForge.Modules.<Module>.Contract.<Kind>;`, type marked
   `public sealed record`, exact same members as today. Do not guess a
   member list from memory — read the current declaration first.
2. Delete the type from its old file. If the old file becomes empty,
   delete the file. If other types remain (feature handler logic,
   excluded handler-only records), remove only the moved declaration.
3. In every referencing file, add the matching
   `using TenantForge.Modules.<Module>.Contract.<Kind>;` and drop any
   now-unused old `using`. Grep for the type name across the module
   before and after editing to prove every reference resolved and no
   duplicate declaration remains.
4. Add the one `<ProjectReference>` and the new project to the solution
   file only once, in the first task of the batch; later tasks in the
   same batch reuse it.
5. Run the module's full test suite — that suite passing unmodified is
   the entire proof this was a namespace move, not a contract change. Do
   not narrow the test filter to "make it pass faster."
6. In the batch's last task, add an `<Module>ContractArchitectureTests`
   class mirroring `BuildingBlocksArchitectureTests.cs`: assert zero
   outgoing `ProjectReference`s from `Module.Contract`, no
   `TenantForge.Modules.*`/`TenantForge.Api`/ASP.NET/EF/Npgsql assembly
   reference in the compiled Contract assembly, exactly one incoming
   `ProjectReference` from `Module`, and an exact, hand-enumerated list of
   every exported production type (copy the list into the test; never
   compute it dynamically and call that a lock).

## Documentation

Every module that gains a `.Contract` project also gets a living handbook
at `docs/contracts/<module>.md`, written in a **separate, later task**
after the code move is `done` — not in the same task, exactly how
`docs/building-blocks/README.md` (B020) followed `TenantForge.BuildingBlocks`
(B018). The handbook follows `docs/building-blocks/README.md`'s section
shape: purpose/non-purpose, fast facts, dependency rule, exported-type
catalog, admission checklist, explicit exclusions, change/compatibility
policy, test/verification map, decision records, change-impact checklist.
Add a matching "Living `<Module>` Contract knowledge" section to
`AGENTS.md`, parallel to the existing "Living BuildingBlocks knowledge"
section, requiring every task touching the Contract project to read the
handbook first and record
`<Module> Contract docs impact: updated — <sections>` or
`<Module> Contract docs impact: none — <specific reason>`.

## Common mistakes this pattern exists to prevent

- A future module adding a `<ProjectReference>` straight to `Module`
  (pulling in its EF Core/Npgsql/migrations) instead of to
  `Module.Contract`, just to read one response shape.
- Treating `Module.Contract` as a place to also put shared helpers,
  base classes or anything not literally an HTTP request/response shape —
  that instinct is what BuildingBlocks' admission rule and this rule both
  exist to block, in their respective directions (cross-module primitive
  vs. one module's own public contract).
- Scaffolding a module's `.Contract` project, or its `Commands/`/`Enums/`
  folders, before a real type exists for it.
- Moving every type in one giant task instead of the dependency-chained
  small batches this Skill and `tasks/slices/023-iam-contract-separation.md`
  describe, producing a diff too large for reliable review.
