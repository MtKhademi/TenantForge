---
description: Review the active TenantForge slice against its task and visible-first rules
agent: plan
---

Review the current clone against `tasks/TASKS.md`, the active live Spec, its
source slice and `AGENTS.md`.

Check:

- whether the browser-visible outcome is complete;
- whether backend work has a current UI consumer;
- acceptance-criteria evidence;
- test and browser verification;
- authentication, tenant and authorization failures;
- speculative abstractions or future-slice work;
- learning-note clarity;
- accidental changes outside task ownership;
- changes to any unrelated ledger row or sibling queue Spec;
- final-delivery cleanup: active row `done`, Spec `—`, exact active Spec
  deleted, and all other non-done Spec links still valid;
- IAM documentation gate: if the diff touches `src/modules/iam/**` or an IAM
  contract in `TenantForge.BuildingBlocks`/the API host, require either an
  updated `docs/modules/IAM.md` in the diff or the exact declaration
  `IAM.md impact: none — <specific reason>` in self-review/the PR body.
  Reject a vague reason. Spot-check edited or existing `IAM.md` claims
  (routes, config keys, permissions, entities) against current code and
  report any stale or missing documentation as a finding.
- BuildingBlocks documentation gate: block review when the diff changed a
  public BuildingBlocks type/member, a package/framework reference, a
  project reference direction, or consumer/compatibility impact but
  `docs/building-blocks/README.md` was not updated and no exact
  `BuildingBlocks docs impact: none — <specific reason>` declaration is
  present; also block when a proposed BuildingBlocks addition lacks
  completed admission evidence or the dependency graph reverses. Cross-check
  a shared TSID/module-config change against `docs/modules/IAM.md` as well.

Return findings ordered by severity. Do not modify files or inspect sibling
clones.
