# Knowledge base

TenantForge keeps two kinds of knowledge per area, on purpose. They have
different readers, different lifecycles and different size budgets.

| File | Reader | Read before a task? |
| --- | --- | --- |
| [AGENT-backend.md](AGENT-backend.md) | the agent running `/backend-task` | **Yes, always** |
| [AGENT-frontend.md](AGENT-frontend.md) | the agent running `/front-task` | **Yes, always** |
| [HUMAN-backend.md](HUMAN-backend.md) | the repository owner | No |
| [HUMAN-frontend.md](HUMAN-frontend.md) | the repository owner | No |

## Agent knowledge

Operational memory for a lightweight coding agent. Short, structured and
scannable: paths, boundaries, conventions, reusable patterns, commands,
contract and authorization rules, known traps, and decisions later tasks must
preserve. It describes the codebase as it is now, links to detailed documents
instead of duplicating them, and carries no history or narrative.

`AGENTS.md` is the **shared** agent knowledge — rules that apply to every area
(ownership, task ledger lifecycle, Git safety, security baseline). Read it
alongside the file for your area. Update it only when a genuinely shared rule
changes.

## Human knowledge

Written for a person who wants to understand the project: what exists, how the
important features work, why the architecture is shaped this way, how to run
it, and what is deliberately missing.

It is never required reading before implementing a task, so it does not consume
a lightweight agent's context. But it **is** read before being edited, so a
task never overwrites an unrelated explanation.

## Who updates what, and when

Both `/front-task` and `/backend-task` review knowledge after verification and
**before committing**:

1. add durable facts the task learned or changed to the agent knowledge for
   that area;
2. explain the completed feature in the human knowledge for that area;
3. touch `AGENTS.md` only if a shared rule actually changed;
4. if the task produced no durable knowledge change, add nothing and say so in
   the completion report.

Knowledge describes the final verified implementation, not the original plan.
Never add debugging detail, speculation, abandoned approaches, task-specific
noise, secrets or large copied code blocks.

## Neighbouring documents

These are area handbooks, not general knowledge. Read them only when a task
touches their subject — `AGENTS.md` defines the read-first gate for each.

- `docs/modules/IAM.md` — the IAM module handbook
- `docs/building-blocks/README.md` — `TenantForge.BuildingBlocks` handbook
- `docs/contracts/iam.md` — IAM's public HTTP contract project
- `docs/architecture.md` — system-wide architecture direction
- `docs/design-system.md` — UI visual language
- `docs/learning/` — per-slice backend teaching notes, written once at delivery
