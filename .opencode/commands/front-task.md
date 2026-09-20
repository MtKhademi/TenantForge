---
description: Execute the next runnable frontend task in the front clone
agent: ui-engineer
subtask: false
---

Run exactly one frontend task listed in `tasks/TASKS.md` from the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `F004` or `004`, or the exact live Spec
path from the ledger. Reject multiple, backend or ambiguous arguments and show
valid examples:

```text
/front-task
/front-task F004
/front-task 004
/front-task tasks/front/F004-refactor-persian-rtl-interface.md
```

## Fast entry or recovery

The Git preflight is a short routing step, not a diagnostic task.

1. Run `git rev-parse --show-toplevel` once. Report that root as the active
   **front clone**; never inspect or edit a sibling directory.
2. Run `git branch --show-current` once.
3. If the branch matches `front/fxxx-<slug>`, route by evidence:
   - infer the matching F task ID from the branch;
   - run `git status --porcelain=v1` exactly once;
   - if it contains substantive staged, untracked or unstaged changes, preserve
     everything and recover this task in place. Do not fetch, switch, pull,
     restore, stash, reset or clean;
   - if it is non-empty but
     `git diff --cached --quiet && test -z "$(git ls-files --others --exclude-standard)" && git diff --ignore-cr-at-eol --quiet`
     succeeds, run `git restore --worktree -- .` once and treat it as clean;
   - when clean, run `git fetch origin main` once and inspect only the exact
     matching row from `origin/main:tasks/TASKS.md` using `git show`;
   - if that remote-main row is `done` with Spec `—`, the task is already
     delivered. Report the transition, run `git switch main`, then
     `git pull --ff-only`, verify one clean `git status --porcelain=v1`, and
     continue directly at **Select**. With no argument choose the next runnable
     task; with the completed ID explicitly supplied, report it done and stop;
   - otherwise recover the current task in place: read its local ledger row,
     live Spec when present and current diff, then rebuild the plan and visible
     todos;
   - never delete the completed local branch automatically.

4. If step 3 did not already route to **Select**, run
   `git status --porcelain=v1` exactly once.
   - If empty, continue immediately.
   - If non-empty, run exactly this one classifier:
     `git diff --cached --quiet && test -z "$(git ls-files --others --exclude-standard)" && git diff --ignore-cr-at-eol --quiet`
   - If it succeeds, the only changes are CRLF/LF noise. Run
     `git restore --worktree -- .` once and continue.
   - If it fails, show the already captured status and stop. Preserve the real
     changes; do not diagnose or modify them.
5. Run `git switch main`, then `git pull --ff-only`, then one final
   `git status --porcelain=v1`. If it is not empty, report it and stop.
6. Never create a worktree.
7. Never inspect the Git executable, aliases, PATH, config or installation.
   Never repeat an equivalent Git command. Preflight gets at most the commands
   explicitly listed above.

## Select

1. Read `tasks/TASKS.md`; it is the only source of status and dependencies.
2. A dependency is complete only when its ledger row is `done` on current
   `main`.
3. With no argument, select the lowest numeric `planned` Front row whose
   dependencies are all `done`.
4. With an explicit argument, resolve exactly one Front row and require
   `status: planned` with complete dependencies. If it is `done`, report that
   it has already been delivered and stop.
5. A selected non-done row must contain one valid Spec link and that complete
   file must exist. Stop on a missing, duplicate or mismatched Spec.
6. If blocked, report each pending dependency, its status, owning clone and
   exact command, then stop.
7. For a fresh task, immediately create and switch to
   `front/<id-lowercase>-<slug>` from the updated `main`. Do this before deep
   analysis so every task starts on its own branch.
8. Read the complete Spec, its `source` slice, `AGENTS.md`,
   `docs/design-system.md` and load `vertical-slice-delivery` plus
   `tenantforge-ui-system`.

## Automatic plan handoff

1. Inspect only relevant `src/web/**` code. Do not inspect, create or update frontend tests.
2. Present visible outcome, UI states, accepted API contract, expected files,
   browser demo, validation, branch `front/<id-lowercase>-<slug>` and explicit
   out-of-scope work.
3. Present the complete todos for visibility, then immediately call
   `todowrite` and continue. Do not ask the user to approve, change, confirm or
   continue and do not stop at the plan.
4. The selected task branch already exists. Begin edits, package installation
   and implementation commands only after the complete informational plan and
   todo list have been shown.

## Execute

1. After the automatic plan handoff, stay on the already selected or recovered
   task branch.
2. Add separate visible todos to change only the active ledger row from
   `planned` to `in_progress`, implement, demo, review and deliver. Do not add
   frontend test todos.
3. Use the complete sequence already passed to `todowrite` and keep exactly one
   item `in_progress`.
4. Perform every step yourself in this same `ui-engineer` conversation. Never
   call the `task` tool or delegate any phase.
5. After each step succeeds, immediately update `todowrite`: complete that one
   todo and start the next one. Show the evidence and next step; never
   batch-complete hidden work.
6. Keep product edits under `src/web/**` unless the Spec explicitly names one
   shared file. Do not inspect, create or update frontend tests.
7. Run frontend build, lint and real desktop/mobile browser demo as separate
   visible todos. Do not run frontend test commands.
8. Review the diff for backend edits, frontend test edits, secrets, generated
   evidence, future work and unrelated changes.

## Review and delivery

1. When validation succeeds, change only the active ledger row from
   `in_progress` to `review`.
2. Compare the complete diff with the live Spec, source slice, visual contract
   and acceptance criteria.
3. Perform an automated self-review. Present findings, screenshots/browser
   evidence, checked acceptance criteria, remaining risks and todo state as an
   informational progress update; do not ask the user to approve or continue.
4. If self-review finds an in-scope defect, add the smallest correction and
   revalidation todos, execute them one at a time and repeat automated
   self-review until it is clean. A review finding is not a reason to wait for
   the user.
5. Pause only for a real blocker, material scope or contract change, missing
   authority, destructive action requiring approval, or explicit user
   interruption. Preserve the branch, ledger state, Spec and current files
   when pausing.
6. After automated self-review is clean, capture the checked acceptance
   criteria and evidence for the PR body, then continue automatically with
   these separate visible todos:
   - change only the active `tasks/TASKS.md` row from `review` to `done`;
   - replace that row's Spec link with `—`;
   - delete exactly the active tracked Spec with
     `git rm -- <exact-active-spec-path>`;
   - verify every non-done row still has one live Spec, every done row has none,
     all dependency IDs exist and the graph has no cycle.
7. Using your own judgment, update the knowledge base only if this delivery
   needs it, as its own visible todo:
   - re-index this project with codebase-memory-mcp (index_repository,
     moderate or full mode) when the delivered change is not yet reflected
     in the graph;
   - update or add the Markdown documentation this change affects (e.g.
     docs/design-system.md, a UI/page composition convention, or the
     tenantforge-ui-system skill file) only when this slice introduced or
     changed that structure;
   - skip entirely, with a one-line note why, when neither the graph nor any
     doc/skill needs a change for this delivery. Never invent speculative
     documentation.
8. Stage the frontend implementation, required docs, `tasks/TASKS.md` and the
   active Spec deletion. Inspect the staged diff, commit with the F-ID,
   push once without force and open a PR to `main`. These are automatic
   delivery steps; do not request a final user review or approval.
9. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
