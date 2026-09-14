# B020 — Create a living BuildingBlocks knowledge and admission guide

**Status:** planned

**Owner:** backend/docs-workflow

**Source:** S22

**Depends on:** B019

**Source slice:** [S22](../slices/022-building-blocks-knowledge.md)

## Goal

Create `docs/building-blocks/README.md` as the current, searchable handbook for
TenantForge BuildingBlocks and extend the agent workflow so changes to
BuildingBlocks automatically trigger a documented impact decision.

This task runs after B019 because it reuses the living-document impact gate
introduced for IAM. It changes documentation/workflow only: no production C#,
project references, packages, database schema, endpoint or frontend behavior.

The learning goal is to prevent a shared project from becoming an ownerless
dumping ground by making admission evidence, dependency direction, consumers
and compatibility costs visible.

## Preconditions

Before writing, verify B018 and B019 are `done` on current `main`. Read:

- `AGENTS.md`;
- `docs/modules/IAM.md`;
- `docs/architecture.md`;
- the complete `src/building-blocks/TenantForge.BuildingBlocks/**` tree;
- IAM/API project references and the architecture tests delivered by B018;
- backend-task, review-slice, backend-mentor and vertical-slice guidance.

Do not copy the B018 Spec or assume its planned layout equals delivered code.
The post-B018 code is authoritative.

## Deliverables

Create/update:

- `docs/building-blocks/README.md`;
- root `README.md` navigation;
- `docs/architecture.md` with one link and the durable dependency rule;
- `AGENTS.md`;
- `.opencode/commands/backend-task.md`;
- `.opencode/commands/review-slice.md`;
- `.opencode/agents/backend-mentor.md`;
- `.opencode/skills/vertical-slice-delivery/SKILL.md`;
- `docs/learning/b020-building-blocks-knowledge.md`;
- only B020 ledger/Spec lifecycle files at delivery.

Do not create a new agent, command or skill. Extend the existing B019
read-first/impact mechanism with one parallel BuildingBlocks rule.

## Document design

Write concise technical English so namespaces/types remain searchable. Use
stable headings and compact tables. The guide describes current merged truth,
not task history.

Start with:

- “Read this first for BuildingBlocks questions”;
- one-sentence purpose;
- source-of-truth warning: code/tests win on disagreement;
- compact table of contents;
- exact impact declaration syntax.

Do not include credentials, task statuses, old paths, full source listings,
generic .NET tutorials or speculative future building blocks.

## Required sections

### 1. Purpose and strict non-purpose

Explain that BuildingBlocks contains only:

- stable contracts used across module boundaries; or
- explicitly accepted system-wide primitives.

State that it is not:

- a `Common`, `Shared`, `Utils` or `Helpers` bucket;
- a home for code that is merely reusable in theory;
- a business-domain module;
- a shortcut for one module to expose internals to another;
- a place for generic repositories, unit of work, base entities, result
  wrappers, service locators or reflection-based module discovery.

New code begins in its owning module. Extraction requires evidence.

### 2. Fast facts

Provide a table derived from the delivered code:

- project directory and assembly name;
- target framework;
- allowed project-reference direction;
- direct framework/package dependencies;
- exported public type count;
- current consuming projects;
- architecture test location;
- handbook update declarations.

Never hard-code a count unless the validation command proves it during B020.

### 3. Dependency rule

Document and render:

```text
TenantForge.Api
    -> TenantForge.Modules.Iam
        -> TenantForge.BuildingBlocks
```

Then verify the real solution/project files. Explain:

- API may depend on modules;
- modules may depend on BuildingBlocks;
- BuildingBlocks must have no `ProjectReference` to API or modules;
- one business module must not reference another module to obtain a shared
  primitive;
- a direct API → BuildingBlocks reference requires a real host-owned consumer
  and explicit architecture review; it is not added for convenience.

List allowed external dependencies from the actual BuildingBlocks project file.
Any new package is a public architectural cost and requires handbook impact.

### 4. Exported-type catalog

Create exactly one row per public production type exported by the delivered
assembly. Required columns:

| Type | Namespace/path | Purpose | Current consumers | Dependencies | Contract tests | Change risk |
| --- | --- | --- | --- | --- | --- | --- |

At minimum reconcile the delivered B018 types:

- `TenantForge.BuildingBlocks.Modules.IModuleConfig`;
- `TenantForge.BuildingBlocks.Identifiers.TsidId`.

If delivered code differs, stop and report the mismatch before documenting it.
Do not silently describe a planned type that does not exist.

### 5. IModuleConfig contract

Record the exact delivered members and signatures:

- `SectionName`;
- `RegisterServices`;
- `ValidateConfiguration`.

Explain:

- implementations remain module-owned;
- registration happens before `Build`;
- validation happens after `Build` through the module activation seam so late
  configuration sources remain visible;
- migration, seeding, endpoints and activation do not belong on this interface;
- adding/changing a member affects every module implementation and is therefore
  a breaking cross-module review item.

Link the exact interface, IAM implementation, IAM module seam, API composition
and composition tests.

### 6. TsidId contract

Document the exact delivered public API:

- `CanonicalLength`;
- `NewId`;
- `IsDefault`;
- `Format`;
- `TryParse`;
- `TryParseNullable`.

Explain the three representations without moving persistence ownership:

| Boundary | Representation |
| --- | --- |
| PostgreSQL module table | `bigint` |
| .NET module/domain | `Tsid` |
| HTTP/JWT | canonical 13-character string |

Document lower-case normalization and rejection of blank, wrong-length,
all-digit, GUID-shaped, non-ASCII, invalid Crockford and default-zero values.
State that generation/parsing/formatting is shared, while EF converters and
entity mappings remain owned by each module's infrastructure.

Link TSID tests and current IAM consumers. Record the multi-writer
`TSIDCREATOR_NODE` operational rule without including environment values.

### 7. Admission checklist

For every proposed public type/addition, require a completed table:

| Evidence | Required answer |
| --- | --- |
| Problem | What dependency/duplication exists now? |
| Consumers | Two real module consumers, or which accepted system-wide rule? |
| Ownership | Why is no business module the correct owner? |
| Minimal API | Smallest public contract and why each member is needed |
| Dependencies | New packages/frameworks and why unavoidable |
| Compatibility | Source/binary/data/transport impact on consumers |
| Security | Boundary, validation, secret or authorization implications |
| Tests | Architecture/contract behavior locked by tests |
| Alternatives | Why keeping it module-local is insufficient |

Missing evidence means reject extraction and keep the type module-local.
“Cleaner”, “reusable”, “future modules may need it” and “best practice” are not
sufficient evidence.

### 8. Explicit exclusions

Inventory the post-B018 exclusions and their current owners. At minimum verify:

- IAM `TsidValueConverter` and EF maps;
- IAM pagination query/binding/execution;
- IAM configuration implementation and options;
- authentication/JWT/seeding;
- permission catalog and tenant authorization;
- IAM entities, DTOs, features and migrations;
- API health/CORS/startup host behavior.

For each, explain the concrete dependency or ownership reason it stays out.
Do not promise future extraction.

### 9. Change and compatibility policy

Classify changes:

- implementation-only/non-breaking;
- additive public API;
- signature/semantic breaking change;
- package/framework dependency change;
- project-reference/dependency-direction change;
- identifier/serialization/storage change.

For each class, state required consumers/tests/docs review. A BuildingBlocks
public API change must enumerate every current consumer before implementation.
Transport or persistence changes require their owning module docs too—for
example, a `TsidId` semantic change also triggers `docs/modules/IAM.md`.

No semantic-versioning/package publication system is introduced; this is an
internal project-reference library.

### 10. Test and verification map

Map architecture and behavior tests to their contracts:

- assembly/type ownership;
- allowed/rejected project references;
- exact exported surface;
- `IModuleConfig` implementation/composition;
- TSID generation/parsing/formatting;
- integration behavior that proves extraction did not alter IAM.

Record exact post-B018 test class/file names. Include:

```bash
dotnet.exe build TenantForge.sln --nologo
dotnet.exe test tests/integration/TenantForge.Api.IntegrationTests/TenantForge.Api.IntegrationTests.csproj --nologo
```

Add focused filters based on actual class names, not planned names copied from
B018.

### 11. Decision records

Keep a compact current decision table:

| Decision | Reason | Revisit only when |
| --- | --- | --- |
| Name BuildingBlocks, not Common | semantic ownership/admission boundary | never for convenience |
| One small project today | only two stable shared concepts | dependency sets require a proven split |
| EF converter remains module-owned | persistence-specific, one consumer | second real module proves same mapping |
| Pagination remains module-owned | HTTP + validation + EF coupling | stable shared contract has multiple consumers |

This is not a chronological changelog. Replace obsolete decisions rather than
stacking contradictory notes.

### 12. Change-impact checklist

End with mappings:

| Changed area | Required handbook action |
| --- | --- |
| public type/member/semantics | exported catalog + type section + compatibility |
| package/framework reference | fast facts + dependency + compatibility |
| project consumer/reference | dependency graph + consumers |
| TSID validation/format | TsidId + tests; also IAM.md impact |
| module-config signature/lifecycle | IModuleConfig + tests; also affected module docs |
| exclusion becomes admitted | admission evidence + catalog + remove exclusion |
| test path/command | test map |
| no documented fact | exact justified no-impact declaration |

Use exactly:

```text
BuildingBlocks docs impact: updated — <sections/types>
BuildingBlocks docs impact: none — <specific reason>
```

## Automatic workflow integration

Extend the B019 policy without duplicating its full text.

### AGENTS.md

Require agents to read `docs/building-blocks/README.md` before answering or
planning a BuildingBlocks question. Any task touching
`src/building-blocks/**`, its consumers' project references, or a public
BuildingBlocks contract must run the impact checklist.

### backend-task

During discovery:

1. detect BuildingBlocks scope from paths/contracts/project references;
2. read the complete handbook;
3. list expected affected handbook sections and all consumers.

Before review, create a separate visible todo to update the guide or record the
exact no-impact declaration.

### review-slice

Block review when:

- public surface/package/project references changed but the guide did not;
- an addition lacks completed admission evidence;
- the dependency graph reverses;
- consumer/compatibility impact is missing;
- no-impact wording is vague.

Cross-check a shared TSID/module-config change against IAM.md as well.

### backend-mentor and delivery skill

Add concise read-first, admission and impact-declaration rules. Do not paste the
entire guide into agent instructions.

## Documentation validation

Provide a dependency-free validation command/script consistent with B019's
chosen approach. It must prove:

- every relative Markdown link resolves;
- every public BuildingBlocks production type appears exactly once in the
  exported-type catalog;
- every catalog path exists;
- every `ProjectReference` direction is allowed;
- BuildingBlocks has no reverse TenantForge project reference;
- required admission/impact phrases exist;
- no stale `IamId`, old IAM-owned `IModuleConfig` path or secret value
  appears.

Do not add a documentation generator or third-party architecture framework.

## Question drill

A fresh agent must answer from the guide first, citing a heading and owning
path:

1. Why is the project named BuildingBlocks instead of Common?
2. What are its current exported types?
3. Can BuildingBlocks reference IAM?
4. May API reference BuildingBlocks directly?
5. Where should an EF TSID converter live?
6. What evidence is required before moving pagination?
7. Who implements `IModuleConfig` and when is validation executed?
8. Why does `TsidId` reject all-digit input?
9. What changes if a new public method is added to `TsidId`?
10. Which tests protect dependency direction?
11. When must IAM.md also change?
12. What exact no-impact declaration is accepted?

If answering requires a repository-wide search because the guide lacks the fact
or path, update it and repeat the drill.

## Verification and demo

Run link/catalog/reference validation, focused architecture/contract tests,
solution build and the full backend suite. Run `git diff --check`.

Manual review/demo:

1. answer the twelve questions from the guide;
2. open every cataloged source/test path;
3. show API → IAM → BuildingBlocks references;
4. confirm no reverse edge;
5. start the existing API and sign in/current-account/tenant smoke test to prove
   documentation-only changes introduced no runtime regression.

No frontend file or frontend test is touched.

## Acceptance checklist

- [ ] B018 and B019 are done before starting.
- [ ] `docs/building-blocks/README.md` has all twelve required sections.
- [ ] Exported types, consumers, dependencies and test paths match delivered code.
- [ ] Admission checklist rejects speculative shared code.
- [ ] AGENTS/commands/mentor/skill enforce read-first and impact declarations.
- [ ] Shared contract changes cross-check affected module handbooks.
- [ ] Links, public-type coverage and dependency-direction validation pass.
- [ ] Twelve-question drill is answerable without repository-wide searching.
- [ ] Build/full backend tests and existing API smoke pass.
- [ ] No production, migration, API, database or frontend change is included.

## Delivery discipline

Read S22 and this entire Spec after synchronizing merged B018/B019. Present the
delivered BuildingBlocks inventory, guide outline, workflow changes and explicit
out-of-scope work, then wait for approval. Work only on B020. On delivery mark
only B020 done, remove exactly this live Spec, preserve S22, validate the ledger
and open one PR to `main`.
