# B019 — Create a living IAM knowledge base and update gate

## 1. Files changed and why

- `docs/modules/IAM.md` (new, 563 lines) — the one current, searchable
  starting point for IAM questions: purpose/non-goals, fast facts, the
  post-B018 dependency/composition boundary, a source-code map, the
  configuration/secrets table, the TSID identity contract, domain model,
  persistence, authentication/JWT, authorization/permission catalog, the
  19-row endpoint catalog, pagination/error semantics, invitations/audit/
  seeding/startup behavior, the test map, current limitations and a
  change-impact checklist that ties future code changes back to specific
  sections. It documents current merged behavior only — no task-status
  language, no historical narrative.
- `README.md`, `docs/architecture.md` — each gained one link to the handbook
  instead of duplicating any of its content, so a reader lands on the
  current-state document first.
- `AGENTS.md`, `.opencode/agents/backend-mentor.md`,
  `.opencode/commands/backend-task.md`, `.opencode/commands/review-slice.md`,
  `.opencode/skills/vertical-slice-delivery/SKILL.md` — wired the "read
  first, update or declare no-impact" rule directly into the existing backend
  workflow: discovery now detects IAM ownership and reads the handbook before
  planning; the second approval gate now requires either an updated `IAM.md`
  or the exact declaration `IAM.md impact: none — <reason>`; the review
  command rejects a vague "docs not needed" and spot-checks edited claims
  against code.
- `tasks/TASKS.md` — B019 row flipped to `in_progress` at plan approval (this
  task's only tracked-file lifecycle edit until final delivery).
- No production C#, migration, HTTP contract or frontend file changed. This
  slice is documentation and agent-workflow only, exactly as scoped.

## 2. Request flow from endpoint to response

Nothing about a live request changed. This slice instead *proves* the
existing flow by running it against the real database and comparing every
observed response to what `IAM.md` now claims:

- `POST /api/auth/login` → `200 {accessToken, expiresAtUtc, user}` with a
  JWT whose `sub` is the canonical 13-character TSID string — matched.
- `GET /api/auth/me` with the token → `200 {id, email, displayName,
  isPlatformAdmin}`; without a token → `401` — matched.
- `GET /api/auth/me/tenants` → `200 {tenants[], pagination}` with
  `pageSize: 50` default — matched.
- `GET /api/platform/dashboard-summary` (PlatformAdmin policy) → `200
  {environment, apiStatus, platformAdminCount, generatedAtUtc}` — matched.
- `GET /api/tenants/{tenantId}/members` and `GET
  /api/tenants/{tenantId}/me/permissions` for an owner membership → matched,
  including the owner-union permission set (`IAM.Audit.View`,
  `IAM.Invitations.Create`, `IAM.Invitations.View`, `IAM.Roles.Manage`).
- A non-member tenant id → `403`, proving cross-tenant default deny even for
  a platform administrator.
- Restarting the API against the same, already-migrated, already-seeded
  database logged "No migrations were applied. The database is already up to
  date." immediately followed by the seeder's idempotent "already-present"
  check, before "Now listening on" — confirming the documented
  migrate-then-seed startup order and idempotent restart behavior.

## 3. Backend concepts introduced

**A handbook is a claim that must be falsifiable against running code.**
This slice is not "write some docs" — it is: write the claims, then run the
real system and the real test suite to try to break each claim. Every
section of `IAM.md` that makes a factual assertion (a route's status code, a
JWT claim name, a pagination default, a permission key) was checked against
either a live HTTP response or an existing passing test, not against memory
or a previous learning note.

**Documentation validation as executable checks, not prose review.** The
Spec required proving structural properties of the handbook itself:

- every literal `MapGet/MapPost/MapPut/MapDelete/MapPatch` call under
  `src/modules/iam/TenantForge.Modules.Iam/features/**` was grepped and
  counted (19) against the endpoint catalog's stated "Expected row count:
  19" — an exact match, one row per route, no extra or missing route;
- every Markdown link in `IAM.md` was inspected: all are same-document
  anchor links (`(#section-slug)`), and every anchor resolves to an actual
  `##` heading — no dead links;
- a grep sweep confirmed no leftover `IamId` symbol and no reference to an
  IAM-owned `IModuleConfig` (both retired by B018), and no embedded
  secret-looking value (signing key, password) anywhere in the handbook.

**The question drill as a coverage test for the document, not the code.**
Twelve unrelated questions (module boundary, startup order, secrets, TSID
representation, permission ownership, cross-tenant isolation, last-admin
protection, invitation conflict handling, pagination limits, migration
history, change-impact process, current limitations) were each answered
using only `IAM.md`'s heading and a cited code path, with zero repository-
wide searching. This is the acceptance test for "searchable starting point":
if any answer had required scanning the whole repository, the handbook
itself would have needed a fix before this slice could close.

**The change-impact checklist turns a documentation rule into a workflow
gate.** Section 16 maps every category of production change (route,
config, entity, migration, JWT, permission, BuildingBlocks contract, test,
limitation) to the exact `IAM.md` sections a future task must touch. The
companion workflow edits (`backend-task.md`, `review-slice.md`,
`backend-mentor.md`, the delivery skill) make that mapping something the
*next* IAM-touching task is forced to consult — first during discovery
(read `IAM.md` before planning), then again before the second approval gate
(classify the diff, update the handbook, or record the exact no-impact
declaration).

## 4. Important security decisions

- **No documentation of secret values.** `IAM.md` names which configuration
  keys are secrets (`IAM:Auth:SigningKey`, `IAM:SeedAdmin:Password`) and
  their fail-closed behavior, but never a configured value, a demo password
  or a signing key. The documentation-validation sweep specifically checked
  for this.
- **The handbook itself documents the fail-closed rules it must not weaken.**
  Sections 5 and 9 describe the Production fail-closed behavior for a blank
  signing key (every protected endpoint stays `401`, never `500`) and the
  all-or-none seed-admin validation — restating these correctly, without
  softening them, is itself a security-relevant documentation responsibility.
- **The live truth check exercised the actual deny paths, not just the
  happy path.** The manual verification included an unauthenticated call
  (`401`) and a cross-tenant call with a valid admin token (`403`), matching
  the Spec's requirement to demonstrate the relevant failure path, not only
  login success.
- **No credential or token was logged or committed.** The bearer token used
  during manual verification lived only in a shell variable for the duration
  of the curl calls and was never written into a tracked file or the
  learning note.

## 5. Alternatives deliberately postponed

- **A generated API-doc tool** — rejected by the Spec; a curated
  security/ownership handbook that explains *why* (owner semantics,
  non-disclosure rules, last-administrator protection) is not replaceable by
  a route-signature dump.
- **A `scripts/` validation script** — the repository has no existing
  `scripts/` convention, so the lightweight checks (route count, dead links,
  no secrets, no obsolete symbols) were run as documented ad hoc grep
  commands in this learning note instead of adding new repository tooling
  for a one-time documentation gate.
- **Applying the same living-handbook pattern to a second module now** — out
  of scope; IAM is still the only backend module. B020 extends the same
  read-first/impact-gate shape to `TenantForge.BuildingBlocks` on its own.
- **Rewriting historical learning notes or source slices to match `IAM.md`'s
  tone** — explicitly rejected; they remain historical records of why a past
  change happened and never override the current handbook or current code.

## 6. Commands and manual steps to verify the slice

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
git diff --check
```

Results from this slice:

- solution build: `0 Warning(s)`, `0 Error(s)`;
- full integration suite: `112` passed, `0` failed;
- `git diff --check`: no whitespace errors.

Documentation validation (grep-based, since no `scripts/` convention exists
yet):

```bash
# route-count cross-check
grep -rhoE '\.(MapGet|MapPost|MapPut|MapDelete|MapPatch)\("[^"]+"' \
  src/modules/iam/TenantForge.Modules.Iam/features/ | sort | wc -l   # -> 19

# obsolete symbols
grep -n "IamId\b" docs/modules/IAM.md                                 # -> none
grep -n "TenantForge.Modules.Iam.*IModuleConfig" docs/modules/IAM.md  # -> none

# secret-looking values
grep -niE "signingkey.*=|password.*:.*[\"'][a-zA-Z0-9]{6,}" docs/modules/IAM.md  # -> none
```

Manual live truth check (Postgres already running; API started with
`dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj
--urls http://0.0.0.0:5099`, reached from WSL via the gateway IP from `ip
route`):

1. Confirmed startup log order: migrate → seed (idempotent) → "Now
   listening on".
2. `POST /api/auth/login` with the seeded admin credentials → `200` with the
   documented response shape and JWT claims.
3. `GET /api/auth/me` with/without the token → `200`/`401` as documented.
4. `GET /api/auth/me/tenants` → `200` with `pagination.pageSize: 50`.
5. `GET /api/platform/dashboard-summary` → `200` platform-admin summary.
6. `GET /api/tenants/{tenantId}/members` and `.../me/permissions` for an
   owned tenant → `200` with the owner-union permission set.
7. Same call with a non-member tenant id → `403`.
8. Restarted the API against the same database → migration/seed logged as
   already-applied/already-present, proving idempotent restart.
9. Answered the twelve-question drill from `IAM.md` alone (see the plan
   conversation for the full answer set).

## 7. Three review questions

1. `IAM.md` states a non-disclosure rule: `401`/`403` responses are
   content-identical across their differing causes. Why does documenting
   this rule matter as much as documenting the status codes themselves?
2. The change-impact checklist (Section 16) requires either an updated
   `IAM.md` or an exact `IAM.md impact: none — <reason>` declaration on every
   future IAM-touching task. What failure mode does a *vague* "docs not
   needed" allow that the exact-declaration requirement prevents?
3. The endpoint catalog records "Expected row count: 19" and ties it to a
   grep-based check against literal `Map*` calls. Why is counting literal
   calls in the feature files a more reliable signal than trusting the
   previous count in the document?
