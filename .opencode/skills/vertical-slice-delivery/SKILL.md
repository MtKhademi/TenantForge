---
name: vertical-slice-delivery
description: Plan, implement, verify and teach one small TenantForge vertical slice with an immediate browser outcome, fixed API contract, minimal backend, integration tests and a backend learning note. Use whenever starting, changing, reviewing or completing any task under tasks/.
---

# Vertical slice delivery

## Task entry

Use `/task` for normal work and `/task-run` only to recover an interrupted,
already-approved slice branch. The roadmap is the single ordered queue. Keep
completed task files as public learning artifacts; mark their `Status` and
roadmap row `Done` only in the delivery commit.

Before implementation, require a clean, current `main`, deterministic task
selection and explicit user approval of the plan. Create the slice branch only
after that approval. Do not edit, install dependencies or run implementation
commands during the planning gate.

## Start gate

Read `AGENTS.md` and the active task. Do not edit until you can state:

- the browser-visible outcome;
- demo steps under three minutes;
- current UI states;
- accepted API contract;
- backend learning goal;
- explicit out-of-scope work.

If any item is missing, improve the task before implementation.

## Delivery order

1. Build or confirm the UI with task-approved mocks.
2. Lock the smallest request/response contract required by that UI.
3. Implement only that backend path.
4. Replace the mock with the real API.
5. Test the successful and relevant denied/error behavior.
6. Run the browser demo.
7. Write the backend learning note.
8. Review the diff against the out-of-scope list.
9. Stop.

Parallel UI and backend work is allowed only after the contract is explicit.
Agents must use separate branches or worktrees and avoid shared files.

When `/task` orchestrates multiple agents on one slice branch, invoke phases
sequentially in the task's written owner order. Parallel invocation requires an
explicitly approved separate-worktree plan, disjoint ownership and a named
integration owner. Record handoff decisions in the main task context.

## Complexity guardrails

- Prefer one or two endpoints.
- Prefer one backend concept per slice.
- Do not introduce a generic framework for a single known use case.
- Do not implement later tasks because they seem adjacent.
- Do not add persistence, caching, events, background jobs or authorization layers before the active UI demonstrates their need.
- Keep generated migrations separate from authored code in review summaries.

## Verification gate

Require:

- build and relevant tests pass;
- browser demo works from a clean start;
- relevant loading/error/denied state is visible;
- security enforcement exists on the server;
- no new console error appears;
- acceptance criteria have evidence;
- the learning note explains the actual request path.

Reject completion statements based only on edited files or compilation.

## Review and delivery gates

After validation, show the user the implementation report and diff summary.
Require `Approve`, `Change`, or `Auto-review`. `Auto-review` invokes the
read-only `task-reviewer`; it never replaces user approval.

After final approval:

1. mark the active task and roadmap row `Done` without deleting the task;
2. stage only slice code, tests, required docs and status updates;
3. verify the staged diff contains no secrets or generated evidence;
4. commit on `slice/<id>-<slug>`, push without force, and open a PR to `main`;
5. report delivery evidence and stop before the next slice.

If validation, push or PR creation fails, preserve the branch and task state and
report the exact blocker. Never reset, clean, stash, amend or retry a failed push
automatically.
