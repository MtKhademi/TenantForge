---
description: Execute the next runnable frontend task in the front clone
agent: ui-engineer
subtask: false
---

Run exactly one task from `tasks/front/` in the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `F001` or `001`, or an exact front task path.
Reject multiple, backend or ambiguous arguments and show valid examples:

```text
/front-task
/front-task F001
/front-task 001
/front-task tasks/front/F001-ui-foundation.md
```

## Enter or recover this task

1. Resolve the Git root. Report it as the active **front clone**; never read or
   edit a sibling directory.
2. Inspect the current branch and `git status --short` before changing anything.
3. If the current branch matches `front/fxxx-<slug>`:
   - infer or verify the matching F task from the branch and argument;
   - treat current changes as recoverable task state;
   - never stash, reset, clean, discard, switch branches or pull;
   - read the task and current diff, rebuild the plan and todo state, then ask
     for approval before resuming when the exact previous approval is unclear.
4. Otherwise require a clean worktree, switch this clone to `main`, run
   `git pull --ff-only`, and require clean current `main`.
5. Never create a worktree.

## Select

1. Read all `tasks/front/F*.md`, both task queues for dependency status, and
   `tasks/ROADMAP.md`.
2. A dependency is complete only when its task file says `status: done` on
   current `main`.
3. With no argument, select the lowest numeric `planned` front task whose
   dependencies are complete.
4. With an explicit argument, require one exact front task with complete
   dependencies.
5. If blocked, report each pending ID and the exact command and clone that owns
   it, then stop.
6. Read the complete selected task, its `source` slice, `AGENTS.md`,
   `docs/design-system.md` and load `vertical-slice-delivery` plus
   `tenantforge-ui-system`.

## Plan gate

1. Inspect only relevant `src/web/**` code and frontend tests.
2. Present visible outcome, UI states, accepted API contract, expected files,
   browser demo, validation, branch `front/<id-lowercase>-<slug>` and explicit
   out-of-scope work.
3. Present todos and ask `Approve`, `Change` or `Cancel`; stop and wait.
4. Before approval, do not edit, install packages, create a branch or run build
   commands.

## Execute

1. After approval, create the proposed branch when this is a fresh task; keep the
   existing matching branch in recovery mode.
2. Immediately call `todowrite` with every approved implementation, test,
   browser, review and delivery step. Keep exactly one todo `in_progress`.
3. Perform every step yourself in this same `ui-engineer` conversation. Never
   call the `task` tool or delegate any step.
4. After each step succeeds, immediately update `todowrite`: complete that one
   todo and start the next one. Show the user a short progress update with the
   evidence and next step. Never batch-complete hidden steps.
5. Keep all product edits under `src/web/**` and frontend tests unless the task
   explicitly names one shared file.
6. Run frontend build, lint, tests and real desktop/mobile browser demo as
   separate visible todos.
7. Review the diff for backend edits, secrets, generated evidence, future work
   and unrelated changes.

## Review and delivery

1. As the same primary agent, compare the complete diff with the executable
   task, source slice, visual contract and acceptance criteria.
2. Present findings, screenshots/browser evidence, remaining risks and the final
   todo state. Ask the user to choose `Approve`, `Change` or `Cancel`, then stop
   and wait.
3. On `Change`, add the requested change and revalidation as visible todos,
   execute them one at a time, and return to this review gate.
4. On `Cancel`, preserve the branch and current files and stop without delivery.
5. After final `Approve`, continue updating the same todo list and change only
   this task's `status: planned` to
   `status: done`. Do not edit `tasks/ROADMAP.md` or a backend task.
6. Stage, inspect, commit, push and PR creation are separate visible todos. Stage
   the front implementation, tests, required docs and this one status change;
   commit with the F-ID, push once without force and open a PR to `main`.
7. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
