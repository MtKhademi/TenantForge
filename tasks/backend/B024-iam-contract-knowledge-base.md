---
id: B024
slice: S24
title: Create a living IAM Contract knowledge and admission guide
agent: backend-mentor
source: tasks/slices/024-iam-contract-knowledge-base.md
---

# Objective

Write `docs/contracts/iam.md`, the current, searchable handbook for
`TenantForge.Modules.Iam.Contract`, in the same shape
`docs/building-blocks/README.md` established for
`TenantForge.BuildingBlocks`. Add a "Living IAM Contract knowledge"
section to `AGENTS.md` mirroring the existing "Living BuildingBlocks
knowledge" section. No code changes.

# Context

Read the complete `tasks/slices/024-iam-contract-knowledge-base.md` — it
is the authoritative contract for this task. Read the complete
`docs/building-blocks/README.md` before writing anything; it is the
template to follow section-for-section (purpose/non-purpose, fast facts,
dependency rule, exported-type catalog, per-type contract detail,
admission checklist, explicit exclusions, change/compatibility policy,
test/verification map, decision records, change-impact checklist). Read
the complete `docs/modules/IAM.md` (this task's cross-link target and
source of the current dependency diagram). Read `AGENTS.md`'s existing
"Living BuildingBlocks knowledge" section — the new "Living IAM Contract
knowledge" section follows its exact structure with `IAM Contract` in
place of `BuildingBlocks`.

B021, B022 and B023 must all be `done` on `main` before starting. This
task documents the code they delivered — do not write this handbook from
`tasks/slices/023-iam-contract-separation.md` alone; every fact must be
re-verified against the actual merged
`src/modules/iam/TenantForge.Modules.Iam.Contract/` tree,
`TenantForge.Modules.Iam.Contract.csproj`, and
`IamContractArchitectureTests.cs`.

# Scope

1. Create `docs/contracts/` and write `docs/contracts/iam.md` with, at
   minimum, these sections (numbering and headings may match
   `docs/building-blocks/README.md`'s table of contents style):
   - Purpose and strict non-purpose — this project holds only IAM's
     delivered HTTP request/query/response shapes; it is not a place for
     new speculative types, not a `Common`/`Shared` bucket, and adding a
     type here requires the admission rule below, not "it might be
     useful."
   - Fast facts table: project path/assembly, target framework, allowed
     reference direction (zero outgoing; `TenantForge.Modules.Iam` is the
     only current incoming reference), exported type count (32 — verified
     by `IamContractArchitectureTests`), architecture test location.
   - Dependency rule with the diagram from
     `tasks/slices/023-iam-contract-separation.md`.
   - Exported-type catalog: one row per type — namespace/path, purpose,
     current consumer(s) (today: state plainly that only
     `TenantForge.Modules.Iam`'s own feature files consume these types;
     there is no second module yet), the endpoint(s) in
     `docs/modules/IAM.md` Section 11 that use it, and which
     `IamContractArchitectureTests` fact locks it.
   - Admission checklist: reproduce and, if needed, sharpen the two-part
     rule from the S23 slice (already a real, delivered HTTP shape; no
     ASP.NET/EF/Npgsql/internal-domain dependency).
   - Explicit exclusions table: `AuthenticatedAccount`, `PaginationSupport`,
     the four roles-internal-only records, every domain entity/enum — one
     row each with its current owner and why it stays out, matching the
     style of `docs/building-blocks/README.md` Section 8.
   - Change and compatibility policy: classify additive vs. breaking
     changes to a contract type, same shape as
     `docs/building-blocks/README.md` Section 9.
   - Test and verification map: `IamContractArchitectureTests` plus the
     existing IAM integration test classes that exercise each contract
     type's shape.
   - Decision records: why this project exists alongside
     `TenantForge.BuildingBlocks` rather than folding into it (different
     problem: module-public-contract vs. cross-module shared primitive),
     why it was created before a second module exists (S23's rationale,
     restated briefly).
   - Change-impact checklist: mirror
     `docs/building-blocks/README.md` Section 12's shape, adapted to this
     project's fast-facts/exported-catalog/exclusions/test-map sections.
2. Add a "Living IAM Contract knowledge" section to `AGENTS.md`,
   immediately after the existing "Living BuildingBlocks knowledge"
   section, with the same structure: any task touching
   `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a consumer's
   reference to it reads `docs/contracts/iam.md` first, classifies its
   diff against the guide's change-impact checklist before review, and
   records exactly one of
   `IAM Contract docs impact: updated — <sections/types>` or
   `IAM Contract docs impact: none — <specific reason>` in self-review and
   the PR body.
3. In `docs/modules/IAM.md`, add one line near the existing Contract-project
   row in Section 4 (added by B021) pointing to `docs/contracts/iam.md` as
   the detailed handbook. Do not change any other IAM.md fact unless you
   find one that is now stale — if so, fix it and expand your
   `IAM.md impact:` declaration accordingly; otherwise state
   `IAM.md impact: none — one cross-link added, no other documented fact
  changed`.
4. Check `docs/architecture.md` for any system-boundary description that
   should mention the new project (likely a short addition next to its
   existing `TenantForge.BuildingBlocks` mention). Update only if such a
   description exists and is now incomplete; do not add a new section if
   none exists today.

# Non-goals

- No file under `src/**` changes.
- No new contract type is added, and no existing type's shape changes.
- Do not describe a hypothetical second-module consumer as if it exists.

# Acceptance

- `docs/contracts/iam.md` exists, covers every section listed above, and
  every fact in it (file paths, namespaces, the 32-type list, test class
  names) is verified against the current repository state at review time.
- `AGENTS.md` contains the new "Living IAM Contract knowledge" section,
  structurally parallel to "Living BuildingBlocks knowledge."
- `docs/modules/IAM.md` has the new cross-link and states its own accurate
  impact declaration.
- No test, endpoint or persisted behavior changed — the full integration
  suite passes unmodified, proving this task touched documentation only.

# Verification

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Both commands are expected to produce identical results to the state
right after B023 — this task changes no `.cs` file under `src/` or
`tests/`. Manually re-verify every path and type name written into
`docs/contracts/iam.md` with `grep`/`find` against the actual repository
before finishing (do not trust the S23/S24 slice text alone — it describes
intent at registration time, the code is what actually shipped).

# Lifecycle

Add row `B024` to the Backend queue in `tasks/TASKS.md` with status
`planned`, dependency `B023`, and Spec link
`tasks/backend/B024-iam-contract-knowledge-base.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/024-iam-contract-knowledge-base.md` is the permanent
record and is never deleted.
