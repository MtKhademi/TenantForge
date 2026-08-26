---
description: Inspect one live TenantForge task and prepare its bounded implementation plan
agent: plan
---

Read `AGENTS.md`, `tasks/TASKS.md`, and the task ID or live executable Spec
supplied in `$ARGUMENTS`.

Resolve the ledger row first. A non-done row must have one valid Spec; read that
complete Spec and its `source` slice. A done row has no live Spec: report its
status and stop rather than reconstructing or re-running it.

Return:

1. visible outcome and demo steps;
2. UI states;
3. API contract;
4. owning clone, forbidden sibling paths and dependency state;
5. expected files or projects;
6. verification commands;
7. explicit out-of-scope list;
8. blockers or contract ambiguities.

Do not edit files, access sibling clones or plan later tasks.
