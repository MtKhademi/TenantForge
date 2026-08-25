---
description: Execute the next runnable frontend task in the front clone
agent: build
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

## Synchronize this clone

1. Resolve the Git root. Report it as the active **front clone**; never read or
   edit a sibling directory.
2. Require a clean worktree. Stop on dirty paths without stashing, resetting,
   cleaning or discarding them.
3. Switch this clone to `main` and run `git pull --ff-only`.
4. Require clean, current `main`. Never create a worktree.

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

1. After approval, create the proposed branch in this clone.
2. Invoke `ui-engineer` with the executable task, source slice and approved plan.
3. Keep all product edits under `src/web/**` and frontend tests unless the task
   explicitly names one shared file.
4. Run frontend build, lint, tests and real desktop/mobile browser demo.
5. Review the diff for backend edits, secrets, generated evidence, future work
   and unrelated changes.

## Review and delivery

1. Present evidence and ask `Approve`, `Change` or `Auto-review`; stop and wait.
2. `Auto-review` invokes read-only `task-reviewer`; user approval remains
   required after findings are handled.
3. After final approval, change only this task's `status: planned` to
   `status: done`. Do not edit `tasks/ROADMAP.md` or a backend task.
4. Stage the front implementation, tests, required docs and this one status
   change; commit with the F-ID, push once without force and open a PR to `main`.
5. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
