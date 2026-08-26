---
id: F000-or-B000
slice: S00
title: Short task title
agent: ui-engineer-or-backend-mentor
source: tasks/slices/000-example.md
---

# Objective

State the concrete outcome this task must deliver.

## Context

Explain the relevant product, design, architecture and contract context. Keep this
spec complete enough for the assigned agent to implement without reconstructing
requirements from the task ledger.

## Scope

- List the implementation responsibilities owned by this task.
- List important boundaries and non-goals.
- Link to exact contracts or source material when useful.

## Acceptance

- Write observable, testable completion criteria.
- Include UX, accessibility, security or isolation criteria when relevant.
- Include the expected review evidence.

## Verification

- List the exact automated checks.
- List required manual or browser checks.
- Record any environment-specific prerequisite without weakening verification.

## Lifecycle

Add the task to `tasks/TASKS.md` with its status, dependencies and this spec path.
The ledger is the only source of truth for status and dependencies; do not copy
those fields into this file.

Keep this full specification while the task is `planned`, `in_progress` or
`review`. After final approval, change the ledger row to `done`, replace the
Spec cell with `—`, and delete this exact file in the same commit.
