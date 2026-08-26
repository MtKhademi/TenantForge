---
id: B002
slice: S02
title: Implement current account and logout APIs
agent: backend-mentor
source: tasks/slices/002-authenticated-shell.md
---

# Objective

Provide the authenticated current-account and logout behavior required by F003.

## Scope

- Implement only the S02 endpoints and token/session behavior.
- Cover valid, missing and expired authentication.
- Explain the request pipeline in the learning note.

## Acceptance

- Current identity and logout match the fixed S02 contract.
- Unauthorized behavior is enforced and integration-tested server-side.
- Backend build and focused tests pass without UI changes.
