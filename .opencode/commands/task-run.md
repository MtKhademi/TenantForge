---
description: Resume an approved TenantForge task on its existing slice branch
agent: build
---

Resume one already-approved TenantForge task in the current worktree.
Normal entry is `/task`; use this command only after an interrupted session.

Arguments: `$ARGUMENTS`

Require exactly one slice ID, numeric ID or task path accepted by `/task`.

## Guard

1. Resolve the Git root and read the complete matching task.
2. Load `vertical-slice-delivery` and read `AGENTS.md`.
3. Derive the expected branch `slice/<slice-id-lowercase>-<slug>`.
4. Require the current branch to equal that branch. Do not switch branches,
   create worktrees, stash, reset, clean or discard files.
5. Report the existing dirty paths; they are the recoverable task state, not a
   reason to erase work.
6. Confirm every dependency is `Done` in both its task file and
   `tasks/ROADMAP.md`.
7. Reconstruct the last approved plan from conversation context. If the exact
   plan is unavailable or ambiguous, present a reconstructed plan and wait for
   `Approve`, `Change`, or `Cancel` before editing.

## Resume

1. Compare the current diff with the task acceptance criteria and rebuild the
   todo list.
2. Continue from the first incomplete phase with the task's named implementation
   agent.
3. Follow `/task` from execution through validation, review gate and delivery.
4. Do not create another branch or start another slice.
