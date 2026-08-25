---
description: Execute the next runnable backend task in the backend clone
agent: build
---

Run exactly one task from `tasks/backend/` in the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `B001` or `001`, or an exact backend task
path. Reject multiple, front or ambiguous arguments and show valid examples:

```text
/backend-task
/backend-task B001
/backend-task 001
/backend-task tasks/backend/B001-development-login-api.md
```

## Synchronize this clone

1. Resolve the Git root. Report it as the active **backend clone**; never read or
   edit a sibling directory.
2. Require a clean worktree. Stop on dirty paths without stashing, resetting,
   cleaning or discarding them.
3. Switch this clone to `main` and run `git pull --ff-only`.
4. Require clean, current `main`. Never create a worktree.

## Select

1. Read all `tasks/backend/B*.md`, both task queues for dependency status, and
   `tasks/ROADMAP.md`.
2. A dependency is complete only when its task file says `status: done` on
   current `main`.
3. With no argument, select the lowest numeric `planned` backend task whose
   dependencies are complete.
4. With an explicit argument, require one exact backend task with complete
   dependencies.
5. If blocked, report each pending ID and the exact command and clone that owns
   it, then stop.
6. Read the complete selected task, its `source` slice, `AGENTS.md`, relevant
   architecture sections and load `vertical-slice-delivery`.

## Plan gate

1. Inspect only relevant `src/api/**`, `src/modules/**`, backend tests and nearest
   working examples.
2. Restate the fixed API contract and present request flow, learning goal,
   expected files, integration tests, validation, branch
   `backend/<id-lowercase>-<slug>` and explicit out-of-scope work.
3. Present todos and ask `Approve`, `Change` or `Cancel`; stop and wait.
4. Before approval, do not edit, install packages, create a branch or run build
   commands.

## Execute

1. After approval, create the proposed branch in this clone.
2. Invoke `backend-mentor` with the executable task, source slice and approved
   plan.
3. Keep product edits under backend ownership and create the required learning
   note. Never redesign or edit frontend files.
4. Run focused integration tests, affected backend tests and solution build.
5. Review the diff for frontend edits, secrets, generated artifacts, weakened
   tests, future work and unrelated changes.

## Review and delivery

1. Present evidence and ask `Approve`, `Change` or `Auto-review`; stop and wait.
2. `Auto-review` invokes read-only `task-reviewer`; user approval remains
   required after findings are handled.
3. After final approval, change only this task's `status: planned` to
   `status: done`. Do not edit `tasks/ROADMAP.md` or a front task.
4. Stage the backend implementation, tests, learning note, required docs and
   this one status change; commit with the B-ID, push once without force and
   open a PR to `main`.
5. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
