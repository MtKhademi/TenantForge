---
id: F000-or-B000
slice: S00
title: Short task title
agent: ui-engineer-or-backend-mentor
source: tasks/slices/000-example.md
---

# How to write this Spec (read this before deleting it)

The agent that will execute this Spec is assumed to be much less capable
than the agent writing it: it cannot reliably infer an unstated requirement,
cannot safely "fill in a shape" from a partial code sketch, does not know
project-specific jargon, and does poorly when many cross-referenced facts
have to be held in its head at once. It DOES follow a numbered list of
concrete, sequential steps well, and it CAN copy/adapt a complete code
sample if you give it one.

Write every section below so that agent can succeed. Concretely:

1. **Always include a "Do this in order" section**, placed right after
   Ownership, before any other detail section. Turn every implementation
   requirement into 6-25 short, literal, sequential steps: "1. Create file
   X. 2. In it, add a class named Y with fields A, B, C. 3. ...". A step
   must never ask the agent to design something from scratch — it names
   what to build, what to call it, and where it goes. This is the primary
   thing `/backend-task` and `/front-task` turn into todos; the detailed
   sections below it are where the exact values, code and edge cases live,
   not a second, disconnected plan.
2. **Write out full code, not a "shape".** If you must show a partial
   class/interface/schema, add a follow-up list ("Members you must still add
   yourself") that names every remaining member with its exact name, type
   and one-sentence behavior — never leave "complete the rest" as the only
   instruction.
3. **Explain jargon inline, once, in parentheses, the first time it's used**
   (e.g. "TSID (a sortable numeric string ID)", "RFC7807 (the repo's
   standard JSON error shape)", "Zod schema (a runtime validator + type
   generator)"). One clause is enough.
4. **Replace judgment calls with decision rules.** Never write "using your
   own judgment," "when useful," or leave a naming/placement choice open.
   Say exactly what to name it, exactly where it goes, and exactly what to
   do in each case. If a real judgment call can't be avoided, add "if
   unsure, do X" as the safe default.
5. **Make every test/acceptance line a checkbox**, one scenario per line,
   phrased as "Write a test that does X, then asserts Y" — not a dense
   semicolon-separated sentence covering ten scenarios at once.
6. **Keep sentences short. One idea per sentence.**
7. **Never remove detail to make a section shorter.** Every route, field
   name, permission key, limit, non-goal and acceptance item must be
   present, explicit and unambiguous. Simplicity comes from structure and
   plain wording, not from cutting requirements.

Delete this "How to write this Spec" section itself once the Spec below is
filled in — it is authoring guidance, not part of the task.

# <ID> — <Title>

## Ownership and dependency

- Owner: `ui-engineer` or `backend-mentor`; run with `/front-task <ID>` or
  `/backend-task <ID>` from the matching clone.
- Slice: `<Sxx>`.
- Depends on: `<task IDs, or —>`.
- Immediate consumer (backend tasks: the frontend task that will call this
  API; frontend tasks: the backend contract this UI is built against, even
  if not yet implemented): `<ID>`.
- Visible outcome: one or two sentences describing the concrete
  browser-visible result.

## Do this in order

State the standing boilerplate once (read `AGENTS.md`, the linked slice, the
paired contract task, follow the branch/ledger rules from Ownership), then
give the full numbered sequence per the authoring rules above. Every
Required implementation / Required UI item below must show up here as one
or more literal steps, in the order they should be done, ending with
running Validation and working through the Acceptance checklist.

## Files expected to change

List every file path this task creates or edits. State what must NOT be
touched (e.g. "Do not edit `src/web/**`" for a backend task).

## HTTP contract *(backend)* / Required contract shape *(frontend)*

Backend: a full method/route/auth/result table. State exactly how errors are
shaped (reuse the existing error format; name it) and what happens for a
malformed or cross-tenant ID.

Frontend: the exact TypeScript/Zod types and client interface this task
must implement, matching the paired backend Spec's field names and
nullability exactly. Show complete code, not a fragment.

## Required implementation *(backend)* / Required UI implementation *(frontend)*

Numbered list of exactly what must be built, with concrete values (limits,
counts, allowed enum values, permission keys) — never "reasonable" or
"appropriate" without naming the actual number or rule.

## Required code shape

Full code for every type/interface/schema. Anything intentionally left for
the implementer gets its own itemized bullet with exact name/type/behavior,
per rule 2 above.

## Security and transaction rules *(backend)* / Required states *(frontend)*

Backend: tenant-scoping rule, what the server must never trust from the
client, transaction/race-condition handling, cancellation/time-provider
rules, logging rules (what must never be logged).

Frontend: every UI state that must exist (idle, loading, success, empty,
every named error/status code, retry, aborted/superseded request) — one
bullet per state.

## Integration tests required *(backend)* / Browser evidence and validation *(frontend)*

Backend: one checkbox per test scenario, phrased "Write a test that X, then
asserts Y." Name the exact test fixture/file convention to use.

Frontend: exact viewports to check, exact `npm` commands to run, and the
exact one-line "Data source: ..." status the task must report.

## Validation

Exact, ordered list of commands to run (build, targeted tests, full test
suite, lint) with no ambiguity about which one runs first.

## Non-goals

Explicit list of adjacent things this task must NOT do.

## Acceptance checklist

One checkbox per completion criterion, each independently checkable with
concrete evidence — not a paragraph combining several criteria.

## Lifecycle

Add the task to `tasks/TASKS.md` with its status, dependencies and this spec
path. The ledger is the only source of truth for status and dependencies;
do not copy those fields into this file.

Keep this full specification while the task is `planned`, `in_progress` or
`review`. After final approval, change the ledger row to `done`, replace the
Spec cell with `—`, and delete this exact file in the same commit.
