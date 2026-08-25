---
name: vertical-slice-delivery
description: Plan, implement, verify and teach one small TenantForge vertical slice with an immediate browser outcome, fixed API contract, minimal backend, integration tests and a backend learning note. Use whenever starting, changing, reviewing or completing any task under tasks/.
---

# Vertical slice delivery

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

Parallel UI and backend work is allowed only after the contract is explicit. Agents must use separate branches or worktrees and avoid shared files.

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
