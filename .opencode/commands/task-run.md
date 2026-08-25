---
description: Resume an approved front or backend task in the current clone
agent: build
---

Resume one interrupted task on its existing branch. Normal entry uses
`/front-task` or `/backend-task`.

Arguments: `$ARGUMENTS`

Require exactly one `Fxxx` or `Bxxx` ID.

1. Resolve the matching task and its source slice.
2. For `Fxxx`, require branch `front/<id-lowercase>-<slug>` and front ownership.
3. For `Bxxx`, require branch `backend/<id-lowercase>-<slug>` and backend
   ownership.
4. Never switch clones, access sibling directories or create a worktree.
5. Treat current dirty files as recoverable task state. Never stash, reset,
   clean or discard them.
6. Confirm dependencies are `done` on the task branch's base. If the exact
   approved plan is unavailable, reconstruct it from the task and diff, present
   it and wait for approval.
7. Rebuild todos from acceptance criteria and current diff.
8. Resume with `ui-engineer` for F tasks or `backend-mentor` for B tasks.
9. Follow the owning command from validation through review and delivery. Do not
   start another task.
