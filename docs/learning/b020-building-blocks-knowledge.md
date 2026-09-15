# B020 — Create a living BuildingBlocks knowledge and admission guide

## 1. Files changed and why

- `docs/building-blocks/README.md` (new, 12 sections) — the current,
  searchable starting point for `TenantForge.BuildingBlocks` questions:
  strict purpose/non-purpose, fast facts, the verified dependency rule
  (`Api → Iam → BuildingBlocks`, zero reverse edges), the complete
  two-type exported catalog, the `IModuleConfig` and `TsidId` contracts with
  exact delivered member signatures, the admission checklist that rejects
  speculative shared code, the explicit exclusion table (EF converter,
  pagination, auth/seeding, permission catalog, entities/DTOs/migrations,
  API host behavior), the change-and-compatibility policy, the test/
  verification map, decision records, and the change-impact checklist. It
  documents current merged behavior only — no task-status language, no
  historical narrative.
- `README.md`, `docs/architecture.md` — each gained one link to the new
  handbook (mirroring the existing `IAM.md` link), instead of duplicating
  any of its content.
- `AGENTS.md`, `.opencode/agents/backend-mentor.md`,
  `.opencode/commands/backend-task.md`, `.opencode/commands/review-slice.md`,
  `.opencode/skills/vertical-slice-delivery/SKILL.md` — extended B019's
  "read first, update or declare no-impact" mechanism with one parallel
  BuildingBlocks rule: discovery now detects BuildingBlocks ownership
  (`src/building-blocks/**`, a consumer's project reference to it, or a
  public `IModuleConfig`/`TsidId` contract change) and reads the guide before
  planning; the second approval gate now requires either an updated guide or
  the exact declaration `BuildingBlocks docs impact: none — <reason>`; the
  review command rejects a vague "docs not needed" and cross-checks a shared
  TSID/module-config change against `docs/modules/IAM.md` too.
- `tasks/TASKS.md` — B020 row flipped to `in_progress` at plan approval (this
  task's only tracked-file lifecycle edit until final delivery).
- No production C#, `.csproj`, migration, HTTP contract or frontend file
  changed. This slice is documentation and agent-workflow only, exactly as
  scoped.

## 2. Request flow from endpoint to response

Nothing about a live request changed. Like B019, this slice instead *proves*
the existing flow still works by running the real system and cross-checking
observed behavior against what the new guide claims:

- Solution build: `TenantForge.BuildingBlocks` compiles standalone with zero
  `ProjectReference` entries, confirmed against
  `src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj`.
- `BuildingBlocksArchitectureTests` re-verify, on every test run, the exact
  claims the guide makes: only two exported types, no `EntityFrameworkCore`/
  `Npgsql`/`TenantForge.Api`/`TenantForge.Modules.*` assembly reference, and
  the `Api → Iam → BuildingBlocks` project-reference direction with no
  reverse edge.
- `TsidIdTests` (13 tests) re-verify every rejection rule the guide documents
  (blank, wrong length, all-digit, GUID-shaped, non-ASCII, non-Crockford,
  all-zero default) plus generation/uniqueness/round-trip/case-normalization.
- A live API run (migrate → seed idempotent → "Now listening on", matching
  the documented startup order) plus `POST /api/auth/login` → `200` with a
  13-character TSID `sub`/`id`, `GET /api/auth/me` → `200`/`401` with/without
  the token, and `GET /api/auth/me/tenants` → `200` with a paginated,
  TSID-identified tenant list — proving this doc-only change introduced no
  runtime regression to the IAM module that consumes both BuildingBlocks
  types.

## 3. Backend concepts introduced

**A second living handbook, generalized from the first.** B019 proved the
read-first/impact-gate pattern for one module (IAM). B020 proves the same
pattern generalizes to a *shared library* with a different, sharper concern:
not "what does this module do" but "what is allowed to enter this project,
and what would break if it changed." The guide's structure reflects that:
sections 1 and 7 (purpose/non-purpose and the admission checklist) exist
only because BuildingBlocks has an admission problem IAM does not — anyone
can propose adding a type to a shared project, and the guide is the
reviewable gate that rejects "might be reusable later."

**Documenting a dependency-direction invariant as a first-class artifact.**
Section 3 doesn't just describe the current reference graph; it explains
*why* each direction is or isn't allowed and points at the exact test
(`ProjectReferences_FlowFromApiToIamToBuildingBlocksOnly`) that would fail if
someone added a `ProjectReference` from BuildingBlocks back to a module. This
is architecture-as-code made discoverable, not just architecture-as-test.

**Explicit exclusions as a decision record, not a to-do list.** Section 8
lists what stays module-owned (`TsidValueConverter`, pagination, auth/JWT,
seeding, permission catalog, entities/DTOs/migrations, API host behavior) and
*why*, without promising future extraction. The Spec was explicit that this
must not read as a roadmap — each row is a closed decision until a documented
admission checklist reopens it.

**Extending an existing workflow gate instead of inventing a second one.**
The Spec required reusing B019's mechanism, not duplicating it. The workflow
edits therefore add one parallel clause per file (detect BuildingBlocks scope
→ read the guide → classify the diff → declare impact) rather than a
separate BuildingBlocks-specific command, agent or skill — matching the
"do not create a new agent, command or skill" constraint.

**Documentation validation as executable checks, again.** As in B019, every
factual claim was checked against real code or a real test, not memory:
internal anchor links resolved programmatically against the actual `##`
headings; every cited `src/`/`tests/`/`docs/` path was verified to exist on
disk (one mismatch was caught this way — a `HealthEndpoint.cs` guess was
corrected to the real `HealthEndpoints.cs`); the exported-type count (2) and
zero-reverse-reference claim were verified against the compiled project file
and the existing `BuildingBlocksArchitectureTests`, not assumed from the
B018 Spec.

## 4. Important security decisions

- **No documentation of secret values.** The guide names the
  `TSIDCREATOR_NODE` operational rule (unique per writer process) without
  ever including an example value, consistent with the no-secrets rule
  proven for `IAM.md` in B019.
- **The guide restates, without softening, the identifier rejection rules
  that are themselves security-relevant.** Section 6 documents that
  `TsidId.TryParse` rejects all-digit input specifically so a client can
  never send or learn to depend on the raw `bigint` backing value, and
  rejects non-ASCII input before it reaches the underlying package to avoid
  an unhandled `IndexOutOfRangeException`. Getting this description slightly
  wrong (e.g., implying the backing integer is ever an acceptable public
  input) would be a security-relevant documentation defect, not just a
  clarity one.
- **No credential was used in a way that reached a tracked file.** The
  manual smoke test's bearer token lived only in a shell variable for the
  duration of the curl calls, matching B019's practice.

## 5. Alternatives deliberately postponed

- **A generated architecture-diagram/doc tool** — rejected by the Spec, same
  reasoning as B019: a curated admission/ownership guide that explains *why*
  a type is or isn't admitted is not replaceable by a dependency-graph dump.
- **A `scripts/` validation script** — no `scripts/` convention exists yet
  in this repository (confirmed again for B020), so the documentation
  validation (link resolution, exported-type/path cross-check, dependency-
  direction verification, no stale-symbol/secret sweep) was run as ad hoc
  commands recorded in this note, consistent with B019.
- **Moving `TsidValueConverter` or `PaginationSupport` into BuildingBlocks
  now** — explicitly out of scope; both remain module-owned per Section 8's
  admission-evidence reasoning, and this task changes no production code.
- **A third living handbook for a hypothetical future module** — out of
  scope; IAM is still the only business module, and BuildingBlocks is still
  the only shared library. The read-first/impact-gate shape is designed to
  generalize, but no third handbook is created speculatively.

## 6. Commands and manual steps to verify the slice

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
git diff --check
```

Results from this slice:

- solution build: `1 Warning(s)` (a pre-existing `CS8631` nullability
  warning in `BuildingBlocksArchitectureTests.cs`, unrelated to this
  documentation-only change — that file was not touched), `0 Error(s)`;
- focused filter
  (`BuildingBlocksArchitectureTests|TsidIdTests|IamModuleCompositionSurfaceTests|IamModuleActivationIntegrationTests`):
  `28` passed, `0` failed;
- full integration suite: `112` passed, `0` failed — identical to B019's
  count, confirming no test was added, removed or altered;
- `git diff --check`: no whitespace errors.

Documentation validation (ad hoc, no `scripts/` convention exists):

```bash
# every internal anchor link resolves against an actual ## heading (Python,
# comparing slugified headings against every [text](#slug) link)
python3 - <<'PY'
import re
text = open('docs/building-blocks/README.md').read()
headings = re.findall(r'^## (.+)$', text, re.M)
def slugify(h):
    h = h.lower(); h = re.sub(r'[`]', '', h)
    h = re.sub(r'[^a-z0-9\- ]', '', h); return h.replace(' ', '-')
slugs = {slugify(h) for h in headings}
links = re.findall(r'\]\(#([a-z0-9-]+)\)', text)
missing = [l for l in links if l not in slugs]
assert not missing, missing
PY
# -> no output: zero missing anchors

# every cited src/tests/docs path exists on disk (caught one mismatch:
# HealthEndpoint.cs -> corrected to the real HealthEndpoints.cs)
grep -oE '`(src|tests|docs)/[a-zA-Z0-9_./-]+`' docs/building-blocks/README.md \
  | tr -d '`' | sort -u | while read -r p; do [ -e "$p" ] || echo "MISSING $p"; done
# -> no output: every path exists

# no stale IamId symbol, no task-status language, no secret-looking value
grep -n "IamId\b" docs/building-blocks/README.md                              # -> none
grep -niE "signingkey.*=|password.*:.*[\"'][a-zA-Z0-9]{6,}" docs/building-blocks/README.md  # -> none

# zero project references on BuildingBlocks (matches the guide's claim)
grep -n "ProjectReference" src/building-blocks/TenantForge.BuildingBlocks/TenantForge.BuildingBlocks.csproj  # -> none
```

Manual live truth check (Postgres already running; API started with
`ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --project
src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5099`,
reached from WSL via the gateway IP from `ip route`):

1. Confirmed startup log order: migrate (already up to date) → seed
   (already-present) → "Now listening on".
2. `POST /api/auth/login` with the seeded admin credentials → `200` with
   `accessToken`, `expiresAtUtc`, and a `user.id` that is a 13-character
   TSID string (`0RM4B8A9M001C`).
3. `GET /api/auth/me` with/without the token → `200`/`401`.
4. `GET /api/auth/me/tenants` → `200` with `pagination.pageSize: 50` and
   every tenant `id` a 13-character TSID string.
5. Stopped the API process; confirmed the port was no longer reachable,
   leaving no background process running.
6. Answered the twelve-question drill from `docs/building-blocks/README.md`
   alone (see the plan conversation for the full answer set), each citing a
   heading and a source path, with zero repository-wide searching required.

## 7. Three review questions

1. `docs/building-blocks/README.md`'s admission checklist requires "two real
   module consumers, or the accepted system-wide convention" before any new
   type is admitted. With IAM as the only business module today, what kind
   of proposed type could ever satisfy the "two real module consumers"
   branch, and what kind never could until a second module exists?
2. Section 9's compatibility policy classifies a `TsidId` semantic change as
   an "identifier/serialization/storage change" that also triggers
   `docs/modules/IAM.md`. Why does a change to a *shared* contract need a
   *module-owned* handbook to react, instead of only the shared handbook?
3. The documentation-validation script caught a wrong file name
   (`HealthEndpoint.cs` vs. the real `HealthEndpoints.cs`) before this note
   was written. What does that near-miss suggest about writing a handbook
   from memory of the B018 Spec versus verifying every cited path against
   the delivered filesystem?
