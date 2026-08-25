---
id: B001
slice: S01
title: Implement the development login API
agent: backend-mentor
status: planned
depends_on: []
source: tasks/slices/001-hardcoded-admin-login.md
---

# Objective

Create the minimum .NET API for the fixed S01 development administrator login
contract while F001 provides the visible UI.

## Scope

- Scaffold only the API/IAM code required by S01.
- Make the hardcoded shortcut fail closed outside Development.
- Add integration coverage for success and invalid credentials.
- Write the required backend learning note.

## Acceptance

- The exact S01 contract works and returns a signed development token.
- Production cannot silently enable the shortcut and secrets are not logged.
- Focused tests and solution build pass without frontend edits.
