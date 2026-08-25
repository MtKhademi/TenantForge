# Contributing to TenantForge

TenantForge welcomes focused contributions that preserve its visible-first learning model.

## Start with a task

Every product change should map to one vertical-slice task under `tasks/`. Before writing code:

1. run `/task` or explicitly select a slice such as `/task S01`;
2. review the generated plan and approve it before any implementation begins;
3. confirm its visible outcome and demo steps;
4. keep its explicit out-of-scope list intact;
5. work on the generated focused branch such as
   `slice/s01-hardcoded-admin-login`.

The workflow requires a second approval after tests and browser verification.
Only then does it mark the task and roadmap row `Done`, commit, push and prepare
the pull request. Completed task files remain in the repository as learning
material.

If a change does not fit an existing task, propose a task using `tasks/TEMPLATE.md` before implementing it.

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
