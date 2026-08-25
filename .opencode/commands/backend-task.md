---
description: Execute the next runnable backend task in the backend clone
agent: backend-mentor
subtask: false
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

## Enter or recover this task

1. Resolve the Git root. Report it as the active **backend clone**; never read or
   edit a sibling directory.
2. Inspect the current branch and `git status --short` before changing anything.
3. If the current branch matches `backend/bxxx-<slug>`:
   - infer or verify the matching B task from the branch and argument;
   - treat current changes as recoverable task state;
   - never stash, reset, clean, discard, switch branches or pull;
   - read the task and current diff, rebuild the plan and todo state, then ask
     for approval before resuming when the exact previous approval is unclear.
4. Otherwise require a clean worktree, switch this clone to `main`, run
   `git pull --ff-only`, and require clean current `main`.
5. Never create a worktree.

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

1. After approval, create the proposed branch when this is a fresh task; keep the
   existing matching branch in recovery mode.
2. Immediately call `todowrite` with every approved implementation, test,
   learning, review and delivery step. Keep exactly one todo `in_progress`.
3. Perform every step yourself in this same `backend-mentor` conversation. Never
   call the `task` tool or delegate any step.
4. After each step succeeds, immediately update `todowrite`: complete that one
   todo and start the next one. Show the user a short progress update with the
   evidence and next step. Never batch-complete hidden steps.
5. Keep product edits under backend ownership and create the required learning
   note. Never redesign or edit frontend files.
6. Run focused integration tests, affected backend tests and solution build as
   separate visible todos.
7. Review the diff for frontend edits, secrets, generated artifacts, weakened
   tests, future work and unrelated changes.

## Review and delivery

1. As the same primary agent, compare the complete diff with the executable
   task, source slice, API contract, security rules and acceptance criteria.
2. Present findings, evidence, remaining risks and the final todo state. Ask the
   user to choose `Approve`, `Change` or `Cancel`, then stop and wait.
3. On `Change`, add the requested change and revalidation as visible todos,
   execute them one at a time, and return to this review gate.
4. On `Cancel`, preserve the branch and current files and stop without delivery.
5. After final `Approve`, continue updating the same todo list and change only
   this task's `status: planned` to
   `status: done`. Do not edit `tasks/ROADMAP.md` or a front task.
6. Stage, inspect, commit, push and PR creation are separate visible todos. Stage
   the backend implementation, tests, learning note, required docs and this one
   status change; commit with the B-ID, push once without force and open a PR to
   `main`.
7. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
