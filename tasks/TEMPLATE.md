# SXX — Task title

## Status

`Planned`

Allowed values are `Planned` and `Done`. A task becomes `Done` only in its
delivery commit after validation, review and final user approval; keep the task
file in the repository.

## Owner

`ui-engineer`, `backend-mentor`, or an explicit sequence of both.

## Depends on

- Previous task or `None`.

## Visible outcome

Describe one browser-visible result in one or two sentences.

## Demo

1. Start from a known state.
2. Perform the primary action.
3. Observe success.
4. Perform the relevant failure or denied action.
5. Observe the correct feedback.

The demo should take less than three minutes.

## UI states

- Idle
- Loading or submitting
- Success
- Relevant empty, error, `401` or `403` state

## API contract

Write exact method, path, request and response shapes, or state `None`.

## Backend learning goal

Name the one primary backend concept taught by the task, or state `None`.

## Scope

- Required behavior.

## Out of scope

- Adjacent behavior explicitly postponed.

## Acceptance criteria

- [ ] Visible outcome demonstrated in a real browser.
- [ ] Relevant failure state demonstrated.
- [ ] Build, lint and tests pass.
- [ ] No new browser console errors.
- [ ] Backend security behavior has integration coverage when applicable.
- [ ] Backend learning note exists when applicable.
- [ ] No future task was started.
