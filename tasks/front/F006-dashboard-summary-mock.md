---
id: F006
slice: S03
title: Define and mock the dashboard summary
agent: ui-engineer
source: tasks/slices/003-dashboard-summary.md
---

# Objective

Build the small dashboard summary using task-approved mock data and lock the
request/response contract that B003 will implement.

## Scope

- Implement summary cards, loading skeleton and failure state.
- Use only the exact fields required by the S03 visible outcome.
- If the source contract is ambiguous, stop and request a separate contract
  correction merged to `main` before implementation.

## Acceptance

- Mock summary is responsive, accessible and visually complete.
- The API contract is explicit enough for independent backend work.
- No real endpoint or speculative analytics are added.
