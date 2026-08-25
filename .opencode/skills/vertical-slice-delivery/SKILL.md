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

## Review and delivery

After all checks and browser evidence pass, require `Approve`, `Change` or
`Auto-review`. The read-only reviewer never replaces final user approval.

After final approval:

1. set only the active task to `status: done`;
2. stage only owned implementation, tests, required docs and that task file;
3. inspect the staged diff for secrets and unrelated changes;
4. commit on `front/fxxx-slug` or `backend/bxxx-slug`;
5. push once without force and create a PR to `main`;
6. report evidence and stop.

On validation, push or PR failure, preserve the current clone and branch and
report the exact blocker. Never reset, clean, stash, amend or retry a failed push
automatically.
