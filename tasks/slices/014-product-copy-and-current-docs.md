# S14 — Clear Persian UI and accurate current documentation

## Executable task and owner
- F021: ui-engineer.
- Shared files owned here: README.md, docs/design-system.md and
  docs/three-clone-workflow.md. No concurrent changes to these files in this batch.

## Visible outcome and demo
Visit login, platform and tenant screens on desktop/mobile. Product copy
explains user actions in Persian without internal task IDs or implementation
instructions. Built-in role/status labels are Persian; custom names stay intact.
Production login does not display development credentials. The invitation UI
accurately describes pending records without claiming delivery or acceptance.

## Scope and acceptance
Update current README status/setup, actual /health route, configured database
version and implemented technology choices from the repository. State clearly
which invitation capabilities are deferred. Point to TASKS.md for status instead
of hardcoding a second live queue. Update design-system guidance to the existing
Persian RTL, right-sidebar and supported collapsed/mobile behavior.
Verify documented run commands against current files and a real local startup.
Keep historical slices/learning notes as history.

This is a copy/documentation pass, not a restyle or task-workflow redesign.
Do not add dependencies, refactor adapters broadly or change agent permissions.
The ui-engineer prohibition on inspecting/editing/running frontend tests remains
in force. Existing stale frontend test expectations are a deferred QA concern:
repairing them needs a separately assigned owner/policy decision and is not
silently included in this batch. Browser verification, build and lint remain
required for these frontend tasks.
