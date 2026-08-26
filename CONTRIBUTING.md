# Contributing to TenantForge

TenantForge welcomes focused contributions that preserve its visible-first learning model.

## Start with a task

Every product change must have one row in `tasks/TASKS.md`, one complete live
executable Spec under `tasks/front/` or `tasks/backend/`, and one permanent
source specification under `tasks/slices/`.

1. use the `main` clone and `/task` to see runnable work;
2. run `/front-task Fxxx` in the `front` clone or `/backend-task Bxxx` in the
   `backend` clone;
3. review and approve the generated plan before implementation;
4. keep the source slice contract and out-of-scope list intact;
5. work only on `front/fxxx-<slug>` or `backend/bxxx-<slug>` in that clone.

The workflow requires a second approval after tests and browser verification.
Only then does it change that one ledger row to `done`, replace its Spec link
with `—`, delete exactly the completed executable Spec, commit, push and prepare
the pull request. Do not edit another ledger row or delete another task's Spec.
The source slice, learning note, Git history and PR remain as learning material.

Do not use Git worktrees and do not reach into a sibling clone. Front and backend
share work only through merged pull requests and updated `main`.

If a change does not fit an existing task, propose a source slice with
`tasks/slices/TEMPLATE.md`, add a ledger row and create its complete executable
Spec with `tasks/TEMPLATE.md` before implementing it.

## Pull requests

A pull request should include:

- the task it completes;
- screenshots or a short browser demo description;
- test commands and results;
- API contract changes;
- security considerations;
- intentionally deferred work.

Avoid bundling formatting, renaming, dependency upgrades and product behavior into one pull request.

## Code expectations

- Prefer understandable code over framework-heavy abstractions.
- Test externally visible behavior rather than implementation details.
- Keep tenant and authorization enforcement on the server.
- Do not add a dependency without explaining what current task requires it.
- Follow `AGENTS.md` whether the code is written manually or by an AI agent.

## Documentation

Update the active Spec when its accepted contract changes; keep status and
dependencies only in `tasks/TASKS.md`. Backend slices must include a learning note under `docs/learning/` when that directory is introduced.

## Conduct

Be specific, constructive and respectful in issues, discussions and reviews.
