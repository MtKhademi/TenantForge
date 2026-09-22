---
description: Execute the next runnable frontend task in the front clone
agent: ui-engineer
subtask: false
---

Run exactly one frontend task from `tasks/TASKS.md` in the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `F004` or `004`, or the exact live Spec path
from the ledger. Reject multiple, backend or ambiguous arguments and show valid
examples:

```text
/front-task
/front-task F004
/front-task 004
/front-task tasks/front/F004-refactor-persian-rtl-interface.md
```

Work through phases 0–10 in order. `/front-task` runs autonomously: after the
task is resolved, never ask the user to approve, review, confirm or continue.
The autonomy rule at the end of this file lists the only reasons to stop.

## Session length management (applies throughout)

Monitor token usage across phases. When usage exceeds roughly 120K tokens, run
`/compact` to compress the session context, then immediately resume the
currently `in_progress` todo. Compaction is routine session hygiene: it never
pauses for user approval and does not break the autonomous flow described
above.

---

## Phase 0 — Git preflight

A short routing step, not a diagnostic task. Run only the commands listed here,
never an equivalent twice, and never inspect the Git executable, aliases, PATH,
config or installation.

1. `git rev-parse --show-toplevel` once. That root is the active **front
   clone**; never inspect or edit a sibling directory. Never create a worktree.
2. `git branch --show-current` once.
3. If the branch matches `front/fxxx-<slug>`, route by evidence:
   - infer the F task ID from the branch;
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
   one Front row. With no argument, the requested row is the lowest-numbered
   `planned` Front row whose dependencies are all `done`.
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
   repository and code, then record the discrepancy in the completion report and
   the PR body.
9. Create and switch to `front/<id-lowercase>-<slug>` from the updated `main`
   now, before deep analysis.

## Phase 2 — Load agent knowledge

Read, in this order:

1. `AGENTS.md` — shared rules, ownership, ledger lifecycle, Git safety.
2. `docs/knowledge/AGENT-frontend.md` — **complete**, every time.
3. Only the additional documents the Spec names (for example
   `docs/design-system.md`, `docs/design/shop/http-contracts.md`, the paired
   backend Spec).
4. Load the `tenantforge-ui-system` skill, and `vertical-slice-delivery` when
   the Spec's delivery shape is unfamiliar.

Do **not** read `docs/knowledge/HUMAN-frontend.md` as implementation context.
Do not re-read the whole repository: agent knowledge first, then only
task-relevant code.

## Phase 3 — Search and inspect

Use the Codebase Memory MCP (`codebase-memory-mcp`, also shown as
`MCP-CodeBaseServer`) **first** for every lookup of a file, component, page,
hook, type, contract, symbol, existing implementation or usage.

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
6. Never guess a file path, component name, route, type or contract member.
7. Inspect only relevant `src/web/**` code. Do not inspect, create, update or
   run frontend tests.

## Phase 4 — Plan internally

Convert the Spec into a short execution checklist and hold it for the whole
task:

- expected visible outcome and demo path;
- files and areas likely involved;
- the existing pattern to follow (name it);
- verification commands;
- acceptance criteria;
- documentation likely to need updating.

Then:

1. Present the plan and the complete todo list **as information**: visible
   outcome, UI states, accepted API contract, expected files, browser demo,
   validation, branch `front/<id-lowercase>-<slug>` and explicit out-of-scope
   work. Do not ask the user to approve, change, confirm or continue.
2. Call `todowrite` immediately and continue.
3. When the Spec has a "Do this in order" numbered section, treat it as the
   already-sequenced implementation list: turn each numbered step into its own
   todo (or a small group of adjacent steps into one), and read the sections it
   points to for the exact details. Do not re-derive a different step order.
4. Add separate visible todos for: the ledger update, implement, verify, update
   knowledge, review diff, deliver. Add no frontend test todos.
5. Change only the active ledger row from `planned` to `in_progress`.

## Phase 5 — Implement

1. Stay on the selected or recovered task branch.
2. Keep exactly one todo `in_progress`. After each step succeeds, mark it
   `completed`, start the next, and show the evidence produced plus the next
   step. Never batch-complete hidden work.
3. Perform every step yourself in this `ui-engineer` conversation. Never call
   the `task` tool or delegate a phase.
4. Keep edits under `src/web/**` unless the Spec explicitly names one shared
   file. Never edit `src/api/**`, `src/modules/**`, backend tests, migrations or
   authorization policies.
5. Mock data only when the Spec allows it, and only in the backend contract's
   exact shape. Never invent a permanent frontend contract that differs from the
   backend.
6. Make reasonable implementation decisions autonomously using the Spec, the
   backend contract, the design system, existing repository patterns and
   automated checks as the sources of truth. Record the important ones for the
   PR body.
7. On failure, keep the current todo active, report the error and add the
   smallest recovery todo.

## Phase 6 — Verify

Run as separate visible todos and capture the output:

```bash
cd src/web && npm run build
cd src/web && npm run lint
```

Then verify the real application in a browser: the happy path and the relevant
failure path, desktop and mobile viewports, every UI state the Spec names,
visible focus, RTL behavior and no new console error. Capture screenshots when
browser tooling is available.

Do **not** run `npm test` or `npm run test:e2e`.

When validation succeeds, change only the active ledger row from `in_progress`
to `review`.

## Phase 7 — Update knowledge

Before committing, as its own visible todo:

1. Update `docs/knowledge/AGENT-frontend.md` with durable facts this task
   learned or changed — a new path, boundary, convention, reusable pattern,
   command, contract rule or trap.
2. Read `docs/knowledge/HUMAN-frontend.md`, then add or revise the section that
   explains the completed feature: what it does, how it works, decisions and
   reasons, limitations and follow-up. Preserve unrelated content.
3. Update `AGENTS.md` only if a genuinely shared rule changed.
4. Update task documentation or an index only when its current content is now
   wrong.
5. Refresh the MCP index with `index_repository` (`name` = this clone's
   project, `path` = its Git root, `mode: "full"`) when the delivered change is
   not yet in the graph. Report the exact outcome. If the server exposes no
   refresh operation, or the refresh fails, say so — never report a refresh
   that did not succeed.
6. Re-run the Phase 6 commands if a documentation or generated artifact can
   affect them.
7. Confirm no knowledge file now describes behavior that is not in the code.

Knowledge describes the final verified implementation, not the plan. Never add
debugging detail, speculation, abandoned approaches, task-specific noise,
secrets or large copied code blocks. If the task produced no durable knowledge
change, add nothing and state that in the completion report.

## Phase 8 — Review diff

1. Review the complete diff yourself against the Spec, source slice, visual
   contract and acceptance criteria.
2. Check for backend edits, frontend test edits, secrets, generated evidence,
   future-slice work and unrelated changes. Remove anything unrelated.
3. Confirm every acceptance criterion is satisfied, with evidence.
4. Present findings, browser evidence, checked criteria, remaining risks and
   todo state **as an informational update**.
5. An in-scope defect is not a reason to wait for the user: add the smallest
   correction and revalidation todos, execute them one at a time and repeat this
   phase until it is clean.

## Phase 9 — Commit and create pull request

After self-review is clean, continue automatically with separate visible todos:

1. Change only the active `tasks/TASKS.md` row from `review` to `done`.
2. Replace that row's Spec link with `—`.
3. Delete exactly the active tracked Spec with
   `git rm -- <exact-active-spec-path>`.
4. Verify every non-done row still has one live Spec, every done row has none,
   all dependency IDs exist and the graph has no cycle.
5. Stage only task-related changes: the frontend implementation, the knowledge
   and docs updates, `tasks/TASKS.md` and the Spec deletion.
6. Show `git status` and the staged diff, then commit on
   `front/<id-lowercase>-<slug>` with the F-ID in the message.
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
- human knowledge updated;
- codebase MCP refresh status;
- known limitations or follow-up work.

Never claim a commit, push, MCP refresh or pull request succeeded without
verifying it. On a validation, push or PR failure, preserve the branch and
report the exact blocker. Never reset, clean, stash, amend or retry a failed
push automatically.

## Phase 10 — Report results

Report: branch, commit hash, PR URL, validation evidence, agent knowledge
updated, human knowledge updated, MCP refresh status, any ledger/Spec
discrepancy found in Phase 1, and remaining risks. Then stop. Never begin the
next task.

---

## Autonomy rule

After Phase 1 resolves the task, do not stop for a frontend technical decision,
an implementation review, a plan confirmation, a step-by-step confirmation, or
permission to commit, push or open the PR. Continue through implementation,
validation, knowledge updates, review, commit, push and PR creation.

Stop only when continuation is genuinely impossible:

- missing authentication or repository permission;
- an inaccessible required dependency;
- a destructive action with an unresolved target;
- a fundamental business decision that cannot be inferred safely from the Spec,
  the backend contract, the codebase or existing patterns;
- an explicit user interruption.

When blocked, report the exact blocker and the evidence for it. Do not ask broad
frontend questions. Preserve the branch, ledger state, Spec and current files.
