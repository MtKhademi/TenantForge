# B024 — Create a living IAM Contract knowledge and admission guide

## 1. Files changed and why

- `docs/contracts/iam.md` (new, 10 sections) — the current, searchable
  starting point for `TenantForge.Modules.Iam.Contract` questions: strict
  purpose/non-purpose, fast facts, the verified dependency rule (zero
  outgoing; `TenantForge.Modules.Iam` is the only incoming reference), the
  complete 32-type exported catalog (one row per type: path, purpose,
  members, current consumer, endpoint(s), locking test fact), the two-part
  admission checklist, the explicit exclusion table (`AuthenticatedAccount`,
  `PaginationSupport`, the four roles-internal records, `CatalogGroups` data,
  `UserResponse.FromAccount`, all domain entities/enums, `TsidId`), the
  change-and-compatibility policy, the test/verification map, decision
  records, and the change-impact checklist. It documents current merged
  behavior only — no task-status language, no historical narrative.
- `AGENTS.md` — new "Living IAM Contract knowledge" section, immediately
  after "Living BuildingBlocks knowledge", structurally parallel to it
  (read-first rule, change-impact classification, the exact
  `impact: updated` / `impact: none — <reason>` declaration pair).
- `docs/modules/IAM.md` — one cross-link added to the existing Contract
  project row in Section 4 (source-code map); no other IAM.md fact changed.
- `.opencode/agents/backend-mentor.md`, `.opencode/commands/backend-task.md`,
  `.opencode/commands/review-slice.md`,
  `.opencode/skills/vertical-slice-delivery/SKILL.md` — extended B020's
  mechanism with one parallel IAM Contract rule in each: discovery detects
  Contract ownership (`src/modules/iam/TenantForge.Modules.Iam.Contract/**`
  or a consumer's reference to it) and reads the new guide before planning;
  the review gate now requires either an updated `docs/contracts/iam.md` or
  the exact declaration `IAM Contract docs impact: none — <reason>`, and
  blocks a surface change whose hand-enumerated roster was not updated
  alongside. No separate command, agent or skill was created.
- `tasks/TASKS.md` — B024 row lifecycle (`planned → in_progress → review →
  done` across this task's delivery).
- No production C#, `.csproj`, migration, HTTP contract or frontend file
  changed. This slice is documentation and agent-workflow only, exactly as
  scoped.

## 2. Request flow from endpoint to response

Nothing about a live request changed. Like B019/B020, this slice instead
*proves* the existing flow still works by running the real system and
cross-checking observed behavior against what the new guide claims:

- Solution build: `TenantForge.Modules.Iam.Contract` still compiles
  standalone with zero `ProjectReference` entries, confirmed against
  `src/modules/iam/TenantForge.Modules.Iam.Contract/TenantForge.Modules.Iam.Contract.csproj`.
- `IamContractArchitectureTests` (4 facts) re-verify, on every test run, the
  exact claims the guide makes: zero project references, the compiled
  assembly-reference denylist, exactly one incoming reference from
  `TenantForge.Modules.Iam`, and the exact 32-type exported surface.
- The full integration suite re-verifies every JSON shape the guide's
  catalog documents — every IAM HTTP contract test still asserts the same
  bytes it asserted before the Contract project existed.

## 3. Backend concepts introduced

**A third living handbook, generalized once more.** B019 proved the
read-first/impact-gate pattern for a module; B020 proved it generalizes to a
shared library. B024 proves it generalizes to a *contract project* — the
third distinct project kind with its own concern: not "what does this module
do" (IAM.md), not "what may enter the shared primitive home" (BuildingBlocks),
but "what does this one module promise the outside world, and what would break
if that promise changed."

**Documenting 32 types without 32 sections.** The BuildingBlocks template has
a per-type contract section (Section 5/6) because it has exactly two types.
That shape does not scale to 32 — the guide instead uses one table row per
type inside a single catalog (Section 4), grouped by sub-namespace
(Queries/Requests/Responses). Each row carries the facts that would
otherwise be per-type sections: path, purpose, exact members, the one real
consumer (an IAM feature file — stated plainly, no invented second module),
the consuming endpoint(s) from `IAM.md` Section 11, and which architecture
test fact locks the type's existence. The hand-enumerated roster in
`IamContractArchitectureTests` is the mechanism that keeps the catalog
honest: adding a 33rd type without updating both the table and the roster
fails the build.

**Explicit exclusions as closed decisions.** Section 6 lists what stays
module-owned and *why* (each row is a closed decision until the admission
checklist reopens it), including subtler cases than S23's original four:
`CatalogGroups` (catalog *data* is business data, not a transport shape —
only the records that carry it moved) and `UserResponse.FromAccount` (a
mapper referencing the `internal` `Account` entity fails admission rule 2).

**Why the Contract project sits alongside BuildingBlocks, not inside it**
(Section 9): different problem — BuildingBlocks holds cross-module shared
primitives meaningful without any business module; the Contract project
holds one specific module's delivered HTTP surface, where every type exists
only because an IAM endpoint shipped it. Folding module-specific business
data into a shared-primitive home would defeat both admission rules.

**Documentation validation as executable checks, again.** Every factual
claim was checked against real code: the 32 catalog rows against the 32
compiled `public sealed record` files, the catalog against the test's
hand-enumerated roster (exact 32/32 set match), all 13 cited paths against
disk, all 4 cited test facts, all 10 cited integration test classes, and all
internal anchors against actual headings.

## 4. Important security decisions

- **No secret or credential value appears anywhere in the guide.** The
  catalog documents request shapes like `LoginRequest` by member name/type
  only; no example value, no connection string, no signing key.
- **The non-disclosure invariants stay visible through the docs split.** The
  guide states that string ID members are canonical 13-character TSID strings
  and links identity semantics to `IAM.md` Section 6, so the documentation
  split (module vs. contract handbooks) does not scatter the identifier
  rules into places where one copy could drift from the other.
- **The gate's blocking list includes the roster check.** A surface change
  that skips the hand-enumerated roster update is a blocked review, not a
  footnote — this keeps the "exact 32-type" guarantee reviewable by a human
  diff, not only by the test that would catch it later.

## 5. Alternatives deliberately postponed

- **Per-type sections instead of one catalog table** — rejected: 32 sections
  would make the guide harder to search, and the table matches how the
  architecture test itself enumerates the surface.
- **A generated catalog from the compiled assembly** — rejected: the guide
  must be curated (purpose, consumer, endpoint columns are human judgments),
  and the *test* already proves exhaustiveness; generating the doc would make
  that proof tautological.
- **A fourth living handbook for a hypothetical second module** — out of
  scope; IAM is still the only business module. The read-first/impact-gate
  shape is designed to generalize, but no speculative handbook is created.
- **Editing `docs/architecture.md`** — checked, not needed: its dependency
  diagram and Contract-project paragraph (lines 58–70) are already complete
  and accurate after B021–B023, so the Spec's "update only if now incomplete"
  condition did not trigger.

## 6. Commands and manual steps to verify the slice

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
git diff --check
```

Results from this slice:

- solution build: `0 Error(s)`, 2 warnings — both pre-existing `CS8631`
  nullability warnings in the two architecture test files (B020's and B023's),
  neither of which this documentation-only change touched;
- full integration suite: `116 passed, 0 failed, 0 skipped` — identical to
  post-B023, confirming no test was added, removed or altered;
- `git diff --check`: no whitespace errors.

Documentation validation (ad hoc, no `scripts/` convention exists):

```bash
# internal anchors resolve, all cited src/tests/docs paths exist on disk,
# the 32 catalog rows each match a real `public sealed record` file with the
# correct namespace, the catalog set == the hand-enumerated roster set in
# IamContractArchitectureTests, all 4 cited test facts and 10 cited
# integration test classes exist, no secret-looking value, IAM.md
# cross-link present (python3 heredoc — see the task conversation)
# -> ALL CHECKS PASSED (the only 'planned' grep hit is the boilerplate
#    "contains no planned/task-status language" line, identical in all three
#    handbooks)
```

## 7. Three review questions

1. `docs/contracts/iam.md` Section 4 gives every one of the 32 types the
   same "current consumer" answer: an IAM feature file. If a second module
   later adds a `ProjectReference` to the Contract project, which sections
   of the guide must change in that task, and which section must change even
   though the type count stays 32?
2. The decision record says a second module consuming IAM shapes references
   the Contract project "only — never the module itself." What concrete
   compile-time cost would a second module pay if it referenced
   `TenantForge.Modules.Iam` directly instead (list at least three things it
   would transitively pull in)?
3. Section 6 excludes `UserResponse.FromAccount` because it references the
   `internal` `Account` entity. Suppose the `Account` entity were later
   made `public`. Would that change the admission decision, and which
   admission rule — rule 1 (delivered HTTP shape) or rule 2 (no
   internal-domain dependency) — would a reviewer cite to keep the answer
   honest?
