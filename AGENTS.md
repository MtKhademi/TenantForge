# TenantForge agent rules

TenantForge is built one visible vertical slice at a time. These rules apply to every agent and every task.

## Before changing files

1. In the `front` clone start with `/front-task`; in the `backend` clone start
   with `/backend-task`. Use `/task` only for read-only coordination from the
   `main` clone. Run the owning command again to recover an interrupted task
   branch.
2. Read the active task completely.
3. Read only the product, architecture or design documents referenced by that task.
4. State the visible outcome, files you expect to touch and what remains out of scope.
5. Confirm the task has a browser demo. If it has no visible consumer, stop and propose a smaller visible slice.
6. Wait for explicit plan approval before editing, installing packages, creating
   a branch or running implementation commands.

## Single-agent execution

- `/front-task` runs directly in the primary `ui-engineer` conversation.
- `/backend-task` runs directly in the primary `backend-mentor` conversation.
- Never call the `task` tool, start a subagent or delegate a phase.
- After plan approval, create the todo list with `todowrite` and keep exactly one
  item `in_progress`.
- Update the todo list immediately after every completed step and show the
  evidence and next step. Never hide several implementation steps inside one
  todo update.
- Perform final diff review in the same agent and require final user approval
  before delivery.

## Scope discipline

- Keep exactly one task active.
- Work only inside the current clone. Never inspect or modify sibling `main`,
  `front` or `backend` directories.
- Do not implement future roadmap items.
- Do not create an endpoint without a named current or immediately dependent
  front task as its consumer, except a health endpoint required to run the
  system.
- Normally add no more than two endpoints in one slice.
- Prefer direct, readable code over abstractions created for hypothetical future requirements.
- Do not combine IAM, tenancy, permissions, caching and audit work in the same slice.
- Do not alter an accepted API contract without explaining the change and updating the active task first.

## Ownership

### UI engineer

- Own `src/web/**`, UI tests and browser evidence.
- May use mocks only when the active task explicitly allows them.
- Must not modify backend projects, database migrations or authorization policies.
- Must not create screens beyond the active task.

### Backend mentor

- Own `src/api/**`, `src/modules/**`, backend tests and backend learning notes.
- Must not redesign or restyle the frontend.
- Implements only the API contract required by the active task.
- Creates `docs/learning/<task-id>-<slug>.md` for every backend slice.

Shared files such as root configuration, Docker Compose and CI have one owner named in the active task.

## Target structure

The first tasks create this structure gradually:

```text
src/
├── api/
├── modules/
│   └── iam/
└── web/
tests/
├── integration/
└── e2e/
```

Do not create empty projects or folders for future modules.

## Verification

A task is complete only when:

- the visible outcome works in a real browser;
- the happy path and the relevant failure path are demonstrated;
- changed code builds, lints and tests successfully;
- no browser console error is introduced;
- security-sensitive API behavior is covered by an integration test;
- generated files are separated from authored changes in the review summary;
- the task acceptance criteria are checked with evidence;
- the agent stops instead of beginning the next task.

## Backend learning note

Write concise notes that explain:

1. files changed and why;
2. request flow from endpoint to response;
3. the backend concepts introduced;
4. important security decisions;
5. alternatives deliberately postponed;
6. commands and manual steps to verify the slice;
7. three review questions for the learner.

Do not turn the learning note into framework documentation. Explain only the code introduced in the slice.

## Git safety

- Use three ordinary clones named `main`, `front` and `backend`. Never create or
  use a Git worktree.
- Synchronize clean `main` in the current clone, then create
  `front/fxxx-<slug>` or `backend/bxxx-<slug>` after plan approval.
- Show `git status` and `git diff` before proposing a commit.
- Never force-push, rewrite shared history or push directly to `main`.
- Do not commit secrets, local credentials, database data or generated browser artifacts.
- Keep `main` runnable and demoable.
- Do not delete completed task files. Change only the active executable task to
  `status: done`; never edit the static roadmap merely to record progress.
- Require a second user approval after validation and review before commit,
  push and pull-request creation.

## Security baseline

- Development-only authentication shortcuts must be impossible to enable silently in production.
- Never log passwords, tokens or secrets.
- Treat tenant isolation and authorization as server-side responsibilities; hiding UI is not authorization.
- Default to deny when tenant or permission context is missing.
- Add complexity such as refresh-token rotation, caching or impersonation only in the task that demonstrates it.
