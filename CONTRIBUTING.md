# Contributing to TenantForge

TenantForge welcomes focused contributions that preserve its visible-first learning model.

## Start with a task

Every product change should map to one executable task under `tasks/front/` or
`tasks/backend/` and one source specification under `tasks/slices/`.

1. use the `main` clone and `/task` to see runnable work;
2. run `/front-task Fxxx` in the `front` clone or `/backend-task Bxxx` in the
   `backend` clone;
3. review and approve the generated plan before implementation;
4. keep the source slice contract and out-of-scope list intact;
5. work only on `front/fxxx-<slug>` or `backend/bxxx-<slug>` in that clone.

The workflow requires a second approval after tests and browser verification.
Only then does it mark that one task `done`, commit, push and prepare the pull
request. Do not edit the static roadmap or another queue's task to record
progress. Completed tasks remain as learning material.

Do not use Git worktrees and do not reach into a sibling clone. Front and backend
share work only through merged pull requests and updated `main`.

If a change does not fit an existing task, propose a source slice with
`tasks/slices/TEMPLATE.md` and an executable task with `tasks/TEMPLATE.md` before
implementing it.

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

Update the active task when its accepted contract changes. Backend slices must include a learning note under `docs/learning/` when that directory is introduced.

## Conduct

Be specific, constructive and respectful in issues, discussions and reviews.
