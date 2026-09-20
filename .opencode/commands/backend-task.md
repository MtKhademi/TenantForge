---
description: Execute the next runnable backend task in the backend clone
agent: backend-mentor
subtask: false
---

Run exactly one backend task from `tasks/TASKS.md` in the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `B001` or `001`, or the exact live Spec path
from the ledger. Reject multiple, front or ambiguous arguments and show valid
examples:

```text
/backend-task
/backend-task B001
/backend-task 001
/backend-task tasks/backend/B001-development-login-api.md
```

Work through phases 0–10 in order. `/backend-task` has **exactly two blocking
approval gates**: one at the end of Phase 4 and one at the end of Phase 8.
Between and after them, execute continuously — a progress update is information,
never a permission request.

---

## Phase 0 — Git preflight

A short routing step, not a diagnostic task. Run only the commands listed here,
never an equivalent twice, and never inspect the Git executable, aliases, PATH,
config or installation.

1. `git rev-parse --show-toplevel` once. That root is the active **backend
   clone**; never inspect or edit a sibling directory. Never create a worktree.
2. `git branch --show-current` once.
3. If the branch matches `backend/bxxx-<slug>`, route by evidence:
   - infer the B task ID from the branch;
   - `git status --porcelain=v1` exactly once;
   - substantive staged, untracked or unstaged changes → preserve everything and
     recover this task in place. Do not fetch, switch, pull, restore, stash,
     reset or clean;
   - non-empty but
     `git diff --cached --quiet && test -z "$(git ls-files --others --exclude-standard)" && git diff --ignore-cr-at-eol --quiet`
     succeeds → CRLF/LF noise only. Run `git restore --worktree -- .` once and
     treat it as clean;
   - clean → `git fetch origin main` once, then read only the matching row from
     `origin/main:tasks/TASKS.md` with `git show`;
   - that row is `done` with Spec `—` → the task is already delivered. Report
     the transition, `git switch main`, `git pull --ff-only`, one clean
     `git status --porcelain=v1`, then continue at Phase 1. With no argument
     pick the next runnable task; with that completed ID supplied explicitly,
     report it done and stop;
   - otherwise recover in place: read its ledger row, live Spec and current
     diff, then rebuild the plan and todos from Phase 4;
   - never delete the completed local branch automatically.
4. If step 3 did not route onward, `git status --porcelain=v1` once.
   - Empty → continue.
   - Non-empty → run the one classifier from step 3. If it succeeds, run
     `git restore --worktree -- .` once and continue. If it fails, show the
     captured status and stop. Preserve the real changes; do not diagnose them.
5. `git switch main`, `git pull --ff-only`, one final
   `git status --porcelain=v1`. Not empty → report and stop.

## Phase 1 — Resolve task

1. `tasks/TASKS.md` is the only source of status, order and dependencies.
2. Determine the exact requested row. With an explicit argument, resolve exactly
   one Backend row. With no argument, the requested row is the lowest-numbered
   `planned` Backend row whose dependencies are all `done`.
3. **Never silently select a different task.** If the requested row is `done`,
   report that it was already delivered and stop. If it is blocked, report each
   pending dependency, its status, owning clone and exact command, then stop.
4. A dependency is complete only when its ledger row is `done` on current
   `main`.
5. The selected non-done row must contain one valid Spec link and that file must
   exist. Stop on a missing, duplicate or mismatched Spec.
6. Read the complete Spec file and its `source` slice.
7. Work only on this task. Implement a dependency's work only when the Spec
   explicitly declares it in scope.
8. If the Spec and `TASKS.md` disagree, resolve it against the current
   repository and code, then record the discrepancy in the plan and the PR body.
9. Create and switch to `backend/<id-lowercase>-<slug>` from the updated `main`
   now, before deep analysis.

## Phase 2 — Load agent knowledge

Read, in this order:

1. `AGENTS.md` — shared rules, ownership, ledger lifecycle, Git safety.
2. `docs/knowledge/AGENT-backend.md` — **complete**, every time.
3. Only the additional documents the Spec names (for example an
   `docs/architecture.md` section or `docs/design/shop/http-contracts.md`).
4. Load the `vertical-slice-delivery` skill, and `module-contract-project` when
   the task touches any `*.Contract` project.

Then detect ownership and read the matching handbook **completely, now**, as
part of discovery:

| Task touches | Read completely |
| --- | --- |
| `src/modules/iam/**`, or an IAM contract in `TenantForge.BuildingBlocks`/the API host | `docs/modules/IAM.md` |
| `src/building-blocks/**`, a consumer's `ProjectReference` to it, or a public BuildingBlocks contract (`IModuleConfig`, `TsidId`) | `docs/building-blocks/README.md` |
| `src/modules/iam/TenantForge.Modules.Iam.Contract/**`, or a consumer's reference to it | `docs/contracts/iam.md` |

For BuildingBlocks and IAM Contract, also list the expected affected sections
and enumerate every current consumer from that guide's exported-type catalog.

Do **not** read `docs/knowledge/HUMAN-backend.md` or `docs/learning/**` as
implementation context. Do not re-read the whole repository: agent knowledge
first, then only task-relevant code.

## Phase 3 — Search and inspect

Use the Codebase Memory MCP (`codebase-memory-mcp`, also shown as
`MCP-CodeBaseServer`) **first** for every lookup of a file, class, endpoint,
entity, contract, symbol, existing implementation or usage.

1. Project name for this clone: run `list_projects` and pick the entry whose
   `root_path` is this clone's Git root. Note that `index_status` and
   `detect_changes` take `project`, while `index_repository` takes `name` —
   passing the wrong one returns a "missing required argument" error, not a
   silent failure.
2. Useful calls: `search_graph` (find a symbol by name or label),
   `get_code_snippet` (exact source of a qualified name), `search_code`
   (graph-augmented text search), `trace_path` (call chains),
   `get_architecture` (project structure), `query_graph` (complex patterns).
3. If the project is not indexed, run `index_repository` first.
4. **Always verify an MCP result against the actual current file before
   editing.** The graph can be stale.
5. If the MCP returns nothing, a partial result or a stale path, fall back to
   `rg`/`glob` over the repository. A missing MCP result is never a reason to
   stop.
6. Never guess a file path, type name, endpoint, column or contract member.
7. Inspect only relevant `src/api/**`, `src/modules/**`, backend tests and the
   nearest working example. Never redesign or edit frontend files.

## Phase 4 — Plan internally, then **approval gate 1**

Convert the Spec into a short execution checklist and hold it for the whole
task:

- expected outcome and the fixed API contract;
- files, projects and migrations likely involved;
- the existing pattern to follow (name it);
- verification commands;
- acceptance criteria;
- documentation likely to need updating.

Present, once:

1. the fixed API contract restated, the request flow, the learning goal,
   expected files, integration tests, validation, branch
   `backend/<id-lowercase>-<slug>` and explicit out-of-scope work;
2. for each ownership detected in Phase 2, the handbook sections the plan will
   update, or the exact reason none apply;
3. the complete todo list. When the Spec has a "Do this in order" numbered
   section, turn each numbered step into its own todo (or a small group of
   adjacent steps into one) and read the sections it points to for the exact
   details; do not re-derive a different step order. Include separate todos for
   the ledger update, implement, verify, learning note, update knowledge, review
   diff and deliver.

Then ask `Approve`, `Change` or `Cancel` and **wait**.

Before approval, do not edit product or task files, install packages or run
build commands. Do not split the plan into several permission questions.

After approval: call `todowrite` with the approved sequence and change only the
active ledger row from `planned` to `in_progress`.

## Phase 5 — Implement

1. Stay on the selected or recovered task branch.
2. Keep exactly one todo `in_progress`. After each step succeeds, mark it
   `completed`, start the next, and show the evidence produced plus the next
   action, then continue automatically. Never batch-complete hidden work and
   never ask permission to start or continue an ordinary approved todo.
3. Perform every step yourself in this `backend-mentor` conversation. Never call
   the `task` tool or delegate a phase.
4. Keep edits under backend ownership. Never change frontend layout, styling,
   routes or interaction design.
5. Implement the smallest direct request path the current browser demo needs.
   Normally no more than one or two endpoints. No speculative abstraction,
   package, background service or future-slice infrastructure.
6. Security-sensitive behavior: default to deny, validate authentication, tenant
   membership and permissions on the server, never log credentials or tokens,
   and make development-only shortcuts fail closed outside `Development`.
7. On failure, keep the current todo active, report the blocker and add the
   smallest recovery todo.
8. Between the two gates, pause only for a real blocker, a material
   scope/API-contract change, missing authority, a destructive action requiring
   approval, or a direct user interruption.

## Phase 6 — Verify

Run as separate visible todos and capture the output:

```bash
dotnet.exe build TenantForge.sln
dotnet.exe test --filter FullyQualifiedName~<TaskTestClass>
dotnet.exe test
```

Use `dotnet.exe`, not `dotnet` — there is no Linux .NET CLI on this machine.
The integration suite needs Docker running for Testcontainers. Cover the happy
path and the relevant unauthorized/forbidden path. Never weaken a test or narrow
a filter to obtain a pass.

When validation succeeds, change only the active ledger row from `in_progress`
to `review`.

## Phase 7 — Update knowledge

Before the second gate and before committing, as its own visible todo:

1. Classify the actual diff against every handbook detected in Phase 2, using
   that guide's change-impact checklist. Either update the affected sections in
   this same task, or record the exact declaration — a vague "docs not needed"
   does not satisfy the gate:
   - `IAM.md impact: none — <specific reason>`
   - `BuildingBlocks docs impact: none — <specific reason>`
   - `IAM Contract docs impact: none — <specific reason>`

   A shared TSID or module-config change also triggers `docs/modules/IAM.md`.
   A change to an exported IAM Contract shape triggers `docs/modules/IAM.md`
   too; a TSID-format change additionally triggers
   `docs/building-blocks/README.md`.
2. Update `docs/knowledge/AGENT-backend.md` with durable facts this task learned
   or changed — a new path, boundary, module responsibility, convention,
   reusable pattern, command, contract rule, authorization rule, trap or
   decision later tasks must preserve.
3. Write the required learning note at `docs/learning/<task-id>-<slug>.md` using
   the structure in `AGENTS.md`.
4. Read `docs/knowledge/HUMAN-backend.md`, then add or revise the section that
   explains the completed change: what it does, how it works, decisions and
   reasons, limitations and follow-up. Preserve unrelated content.
5. Update `AGENTS.md` only if a genuinely shared rule changed.
6. Update task documentation or an index only when its current content is now
   wrong.
7. Refresh the MCP index with `index_repository` (`name` = this clone's project,
   `path` = its Git root, `mode: "full"`) when the delivered change is not yet
   in the graph. Report the exact outcome. If the server exposes no refresh
   operation, or the refresh fails, say so — never report a refresh that did not
   succeed.
8. Re-run the Phase 6 commands if a documentation, migration or generated
   artifact can affect them.
9. Confirm no knowledge file now describes behavior that is not in the code.

Knowledge describes the final verified implementation, not the plan. Never add
debugging detail, speculation, abandoned approaches, task-specific noise,
secrets or large copied code blocks. If the task produced no durable knowledge
change beyond the learning note, add nothing and state that in the completion
report.

## Phase 8 — Review diff, then **approval gate 2**

1. Review the complete diff yourself against the Spec, source slice, API
   contract, security rules and acceptance criteria.
2. Check for frontend edits, secrets, generated artifacts, weakened tests,
   future-slice work and unrelated changes. Remove anything unrelated.
3. Confirm every acceptance criterion is satisfied, with evidence.
4. Present findings, the complete diff summary and evidence, the checked
   acceptance criteria, every handbook impact declaration from Phase 7,
   remaining risks and the final todo state.
5. Ask `Approve`, `Change` or `Cancel` and **wait** before commit, push and
   pull-request creation.
   - `Change` → add correction and revalidation todos, execute them one at a
     time and return to this gate.
   - `Cancel` → preserve the branch, ledger state, Spec and current files and
     stop without delivery.

## Phase 9 — Commit and create pull request

After the second `Approve`, do not ask for another permission. Continue with
separate visible todos:

1. Change only the active `tasks/TASKS.md` row from `review` to `done`.
2. Replace that row's Spec link with `—`.
3. Delete exactly the active tracked Spec with
   `git rm -- <exact-active-spec-path>`.
4. Verify every non-done row still has one live Spec, every done row has none,
   all dependency IDs exist and the graph has no cycle.
5. Stage only task-related changes: the backend implementation, tests, the
   learning note, the knowledge and docs updates, `tasks/TASKS.md` and the Spec
   deletion.
6. Show `git status` and the staged diff, then commit on
   `backend/<id-lowercase>-<slug>` with the B-ID in the message.
7. `git push` once, without force, never to `main`.
8. Create a pull request to `main` and capture its URL.

The PR description must contain:

- task ID and title;
- summary of the implementation;
- important decisions;
- files or areas changed;
- tests and validation commands executed;
- validation results;
- agent knowledge updated;
- human knowledge updated (including the learning note path);
- codebase MCP refresh status;
- every handbook impact declaration from Phase 7;
- known limitations or follow-up work.

Never claim a commit, push, MCP refresh or pull request succeeded without
verifying it. On a validation, push or PR failure, preserve the branch and
report the exact blocker. Never reset, clean, stash, amend or retry a failed
push automatically.

## Phase 10 — Report results

Report: branch, commit hash, PR URL, build/test evidence, manual demo steps,
agent knowledge updated, human knowledge updated, MCP refresh status, any
ledger/Spec discrepancy found in Phase 1, remaining risks, and three review
questions for the learner. Then stop. Never begin the next task.
