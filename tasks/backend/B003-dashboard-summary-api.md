---
id: B003
slice: S03
title: Implement the dashboard summary API
agent: backend-mentor
source: tasks/slices/003-dashboard-summary.md
---

# Objective

Implement the smallest authenticated summary endpoint required by the S03 UI.

## Scope

- Return only environment, API status and current administrator count fields.
- Preserve the accepted source-slice contract.
- Test authorized and unauthorized behavior and write the learning note.

## Acceptance

- The endpoint supplies honest values for F005.
- No speculative analytics, caching or persistence is introduced.
- Backend validation passes without frontend edits.
