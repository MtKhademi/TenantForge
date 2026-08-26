---
description: Execute the next runnable backend task in the backend clone
agent: backend-mentor
subtask: false
---

Run exactly one backend task listed in `tasks/TASKS.md` from the current clone.

Arguments: `$ARGUMENTS`

Accept no argument, an ID such as `B001` or `001`, or the exact live Spec
path from the ledger. Reject multiple, front or ambiguous arguments and show
valid examples:

```text
/backend-task
/backend-task B001
/backend-task 001
/backend-task tasks/backend/B001-development-login-api.md
```

## Enter or recover this task

1. Resolve the Git root. Report it as the active **backend clone**; never read or
   edit a sibling directory.
2. Inspect the current branch and `git status --short` before changing anything.
3. If the current branch matches `backend/bxxx-<slug>`:
   - infer or verify the matching B row in `tasks/TASKS.md`;
   - accept `in_progress` or `review` as recoverable implementation state;
   - accept `done` with Spec `—` and a deleted task file only as delivery
     recovery for this same branch;
   - never stash, reset, clean, discard, switch branches or pull;
   - read the ledger row, live task Spec when present and current diff, rebuild
     the plan and todo state, then ask for approval before resuming when the
     previous approval is unclear.
4. Otherwise require a clean worktree, switch this clone to `main`, run
   `git pull --ff-only`, and require clean current `main`.
5. Never create a worktree.

## Select

1. Read `tasks/TASKS.md`; it is the only source of status and dependencies.
2. A dependency is complete only when its ledger row is `done` on current
   `main`.
3. With no argument, select the lowest numeric `planned` Backend row whose
   dependencies are all `done`.
4. With an explicit argument, resolve exactly one Backend row and require
   `status: planned` with complete dependencies. If it is `done`, report that
   it has already been delivered and stop.
5. A selected non-done row must contain one valid Spec link and that complete
   file must exist. Stop on a missing, duplicate or mismatched Spec.
6. If blocked, report each pending dependency, its status, owning clone and
   exact command, then stop.
7. Read the complete Spec, its `source` slice, `AGENTS.md`, relevant
   architecture sections and load `vertical-slice-delivery`.

## Plan gate

1. Inspect only relevant `src/api/**`, `src/modules/**`, backend tests and
   nearest working examples.
2. Restate the fixed API contract and present request flow, learning goal,
   expected files, integration tests, validation, branch
   `backend/<id-lowercase>-<slug>` and explicit out-of-scope work.
3. Present todos and ask `Approve`, `Change` or `Cancel`; stop and wait.
4. Before approval, do not edit, install packages, create a branch or run build
   commands.

## Execute

1. After approval, create the proposed branch for a fresh task; keep the existing
   matching branch in recovery mode.
2. Add separate visible todos to change only the active ledger row from
   `planned` to `in_progress`, implement, test, teach, review and deliver.
3. Immediately call `todowrite` with the approved sequence and keep exactly one
   item `in_progress`.
4. Perform every step yourself in this same `backend-mentor` conversation.
   Never call the `task` tool or delegate any phase.
5. After each step succeeds, immediately update `todowrite`: complete that one
   todo and start the next one. Show the evidence and next step; never
   batch-complete hidden work.
6. Keep product edits under backend ownership and create the required learning
   note. Never redesign or edit frontend files.
7. Run focused integration tests, affected backend tests and solution build as
   separate visible todos.
8. Review the diff for frontend edits, secrets, generated artifacts, weakened
   tests, future work and unrelated changes.

## Review and delivery

1. When validation succeeds, change only the active ledger row from
   `in_progress` to `review`.
2. Compare the complete diff with the live Spec, source slice, API contract,
   security rules and acceptance criteria.
3. Present findings, evidence, checked acceptance criteria, remaining risks and
   the final todo state. Ask the user to choose `Approve`, `Change` or
   `Cancel`, then stop and wait.
4. On `Change`, add correction and revalidation todos, execute them one at a
   time and return to this review gate.
5. On `Cancel`, preserve the branch, ledger state, Spec and current files and
   stop without delivery.
6. After final `Approve`, capture the checked acceptance criteria and evidence
   for the PR body, then as separate visible todos:
   - change only the active `tasks/TASKS.md` row from `review` to `done`;
   - replace that row's Spec link with `—`;
   - delete exactly the active tracked Spec with
     `git rm -- <exact-active-spec-path>`;
   - verify every non-done row still has one live Spec, every done row has none,
     all dependency IDs exist and the graph has no cycle.
7. Stage the backend implementation, tests, learning note, required docs,
   `tasks/TASKS.md` and the active Spec deletion. Inspect the staged diff,
   commit with the B-ID, push once without force and open a PR to `main`.
8. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
