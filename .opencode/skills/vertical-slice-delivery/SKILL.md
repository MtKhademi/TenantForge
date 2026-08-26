---
name: vertical-slice-delivery
description: Plan, implement, verify and teach one TenantForge front or backend task in the three-clone workflow, preserving a visible vertical slice, fixed API contract, strict ownership, automated checks and user approval gates. Use for any executable task under tasks/front or tasks/backend.
---

# Vertical slice delivery

## Three-clone boundary

Use one normal Git clone per responsibility:

- `main`: run `/task` for read-only coordination and review;
- `front`: run `/front-task` and edit frontend-owned files;
- `backend`: run `/backend-task` and edit backend-owned files.

Never create or use Git worktrees. Never read, edit or run commands in a sibling
clone. Synchronize only the current clone from `main` before selection.

Run each executable command in one primary agent conversation:

- `/front-task` runs directly as `ui-engineer`;
- `/backend-task` runs directly as `backend-mentor`.

Never call the `task` tool or delegate to a subagent. Use the same primary agent
for discovery, planning, implementation, validation, self-review and delivery.

## Task model

Treat `tasks/front/F*.md` and `tasks/backend/B*.md` as executable queues. Read the
referenced `tasks/slices/*.md` for the complete product contract. A dependency is
complete only when its own task file says `status: done` on current `main`.

Update only the active task's status in its delivery commit. Keep
`tasks/ROADMAP.md` static so parallel front and backend PRs do not edit a shared
progress file.

## Start gate

Before editing, state:

- browser-visible consumer and demo path;
- current UI states and fixed API contract;
- current task ownership and forbidden paths;
- one primary learning goal;
- validation and explicit out-of-scope work.

Require `Approve`, `Change` or `Cancel`. Do not edit, install, branch or build
before approval.

## Visible todo protocol

After plan approval:

1. call `todowrite` with the complete approved sequence;
2. keep exactly one todo `in_progress`;
3. perform that step in the current primary agent;
4. immediately mark it `completed`, start the next todo and show its evidence;
5. never complete multiple unseen steps in one todo update;
6. on failure, keep the failed step active and add or revise the smallest
   recovery todo.

Keep build, focused tests, broader tests, browser/API demo, learning note,
self-review, status update, staging, commit, push and PR creation as distinct
todos whenever they apply. Do not start delivery todos before final approval.

## Delivery order

For front tasks:

1. implement or integrate only the specified UI phase;
2. verify idle, loading, success and relevant error/denied states;
3. run the real app on desktop and mobile;
4. stop without changing backend code.

For backend tasks:

1. restate the source slice contract;
2. implement the smallest direct request path;
3. test success and relevant unauthorized/forbidden behavior;
4. write the concise backend learning note;
5. stop without changing frontend code.

Cross-clone work proceeds through merged contracts and task dependencies, never
through uncommitted sibling files.

## Complexity guardrails

- Prefer one or two endpoints and one backend concept per task.
- Do not add future-slice infrastructure or generic abstractions.
- Treat UI hiding as presentation, never authorization.
- Keep migrations and generated output identifiable in review.
- Do not weaken tests or acceptance criteria to obtain a pass.

## Verification environment (backend)

- This machine has no Linux `dotnet` CLI; use `dotnet.exe` (Windows SDK via WSL interop, .NET 10) for build/test/run.
- Compose modules through their public seam only (`IamModule.AddIamModule` /
  `ValidateIamModuleConfiguration` / `MapIamModule`); features stay `internal`,
  and module configuration follows `IModuleConfig`. Validate configuration only
  after `builder.Build()`, never at registration.
- `dotnet.exe run` forces `Development`; run the built DLL for Production runs.
- Windows hosts are not reachable from WSL on `127.0.0.1`; bind `0.0.0.0` and
  curl through the WSL gateway IP.
- Make `WebApplicationFactory` test hosts hermetic with a temp content root so
  on-disk `appsettings.*.json` cannot leak into tests.

See `docs/architecture.md` ("Local development environment") and the
`backend-mentor` agent for the full detail.

## Review and delivery

After all checks pass, review the complete diff yourself in the same primary
conversation. Present findings, acceptance evidence and remaining risks. Require
`Approve`, `Change` or `Cancel` before delivery.

On `Change`, add visible correction and revalidation todos, execute them one at
a time and repeat self-review. On `Cancel`, preserve the branch and stop.

After final approval:

1. continue the same visible todo list;
2. set only the active task to `status: done`;
3. stage only owned implementation, tests, required docs and that task file;
4. inspect the staged diff for secrets and unrelated changes;
5. commit on `front/fxxx-slug` or `backend/bxxx-slug`;
6. push once without force and create a PR to `main`;
7. report evidence and stop.

On validation, push or PR failure, preserve the current clone and branch and
report the exact blocker. Never reset, clean, stash, amend or retry a failed push
automatically.
