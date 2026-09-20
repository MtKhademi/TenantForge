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

## Fast entry or recovery

The Git preflight is a short routing step, not a diagnostic task.

1. Run `git rev-parse --show-toplevel` once. Report that root as the active
   **backend clone**; never inspect or edit a sibling directory.
2. Run `git branch --show-current` once.
3. If the branch matches `backend/bxxx-<slug>`, route by evidence:
   - infer the matching B task ID from the branch;
   - run `git status --porcelain=v1` exactly once;
   - if it contains substantive staged, untracked or unstaged changes, preserve
     everything and recover this task in place. Do not fetch, switch, pull,
     restore, stash, reset or clean;
   - if it is non-empty but
     `git diff --cached --quiet && test -z "$(git ls-files --others --exclude-standard)" && git diff --ignore-cr-at-eol --quiet`
     succeeds, run `git restore --worktree -- .` once and treat it as clean;
   - when clean, run `git fetch origin main` once and inspect only the exact
     matching row from `origin/main:tasks/TASKS.md` using `git show`;
   - if that remote-main row is `done` with Spec `—`, the task is already
     delivered. Report the transition, run `git switch main`, then
     `git pull --ff-only`, verify one clean `git status --porcelain=v1`, and
     continue directly at **Select**. With no argument choose the next runnable
     task; with the completed ID explicitly supplied, report it done and stop;
   - otherwise recover the current task in place: read its local ledger row,
     live Spec when present and current diff, then rebuild the plan and visible
     todos;
   - never delete the completed local branch automatically.

4. If step 3 did not already route to **Select**, run
   `git status --porcelain=v1` exactly once.
   - If empty, continue immediately.
   - If non-empty, run exactly this one classifier:
     `git diff --cached --quiet && test -z "$(git ls-files --others --exclude-standard)" && git diff --ignore-cr-at-eol --quiet`
   - If it succeeds, the only changes are CRLF/LF noise. Run
     `git restore --worktree -- .` once and continue.
   - If it fails, show the already captured status and stop. Preserve the real
     changes; do not diagnose or modify them.
5. Run `git switch main`, then `git pull --ff-only`, then one final
   `git status --porcelain=v1`. If it is not empty, report it and stop.
6. Never create a worktree.
7. Never inspect the Git executable, aliases, PATH, config or installation.
   Never repeat an equivalent Git command. Preflight gets at most the commands
   explicitly listed above.

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
7. For a fresh task, immediately create and switch to
   `backend/<id-lowercase>-<slug>` from the updated `main`. Do this before
   deep analysis so every task starts on its own branch.
8. Read the complete Spec, its `source` slice, `AGENTS.md`, relevant
   architecture sections and load `vertical-slice-delivery`. When the Spec
   has a "Do this in order" numbered section, treat it as the primary,
   already-sequenced list of implementation steps: read every other section
   it points to (HTTP contract, required code shape, security rules, tests,
   acceptance checklist) for the exact details, but do not re-derive your own
   step order from scratch when one is already given.
9. Detect IAM ownership: the task's expected files include
   `src/modules/iam/**`, or the task changes an IAM contract living in
   `TenantForge.BuildingBlocks` or the API host. When detected, read the
   complete `docs/modules/IAM.md` handbook now, as part of discovery.
10. Detect BuildingBlocks ownership: the task's expected files include
    `src/building-blocks/**`, a consumer's `ProjectReference` to it, or a
    public BuildingBlocks contract (`IModuleConfig`, `TsidId`). When
    detected, read the complete `docs/building-blocks/README.md` handbook now,
    list the expected affected sections, and enumerate every current
    consumer (from the guide's exported-type catalog) as part of discovery.
11. Detect IAM Contract ownership: the task's expected files include
    `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a consumer's
    `ProjectReference` to it. When detected, read the complete
    `docs/contracts/iam.md` handbook now, list the expected affected
    sections, and enumerate every current consumer (from the guide's
    exported-type catalog) as part of discovery.

## Plan gate

1. Inspect only relevant `src/api/**`, `src/modules/**`, backend tests and
   nearest working examples.
2. Restate the fixed API contract and present request flow, learning goal,
   expected files, integration tests, validation, branch
   `backend/<id-lowercase>-<slug>` and explicit out-of-scope work. When IAM
   ownership was detected in Select, include the expected `IAM.md` sections
   the plan will update (or the exact reason none apply). When BuildingBlocks
   ownership was detected in Select, include the expected
   `docs/building-blocks/README.md` sections and affected consumers the plan
   will update (or the exact reason none apply). When IAM Contract ownership
   was detected in Select, include the expected `docs/contracts/iam.md`
   sections the plan will update (or the exact reason none apply).
3. This is the first routine approval gate. Present the complete todos once,
   ask `Approve`, `Change` or `Cancel`, then stop and wait.
4. Before approval, do not edit product or task files, install packages or run
   build commands. The selected task branch already exists. Do not split the
   plan into multiple permission questions.

## Execute

1. After approval, stay on the already selected or recovered task branch.
2. Add separate visible todos to change only the active ledger row from
   `planned` to `in_progress`, implement, test, teach, review and deliver.
   When the Spec has a "Do this in order" numbered section, turn each of its
   numbered steps into its own todo (or a small group of adjacent steps into
   one todo) instead of inventing a different breakdown; keep the ledger,
   test, teach, review and deliver todos this section already lists in
   addition to those.
3. Immediately call `todowrite` with the approved sequence and keep exactly one
   item `in_progress`.
4. Perform every step yourself in this same `backend-mentor` conversation.
   Never call the `task` tool or delegate any phase.
5. After each step succeeds, immediately update `todowrite`: complete that one
   todo and start the next one. Show the evidence and next action, then continue
   automatically. This progress message is informational: do not ask permission
   to begin/continue a normal approved todo and do not stop after messages such
   as “Next active step”.
6. Keep product edits under backend ownership and create the required learning
   note. Never redesign or edit frontend files.
7. Run focused integration tests, affected backend tests and solution build as
   separate visible todos.
8. Review the diff for frontend edits, secrets, generated artifacts, weakened
   tests, future work and unrelated changes.
9. Between plan approval and final review, pause only for a real blocker,
   material scope/API-contract change, missing authority, destructive action
   requiring approval, or direct user interruption.

## Review and delivery

1. When validation succeeds, change only the active ledger row from
   `in_progress` to `review`.
2. Compare the complete diff with the live Spec, source slice, API contract,
   security rules and acceptance criteria. If the diff touches
   `src/modules/iam/**` or an IAM contract in `TenantForge.BuildingBlocks`/the
   API host, classify it against `docs/modules/IAM.md`'s change-impact
   checklist as its own visible todo: update the affected `IAM.md` sections in
   this same task, or record the exact declaration
   `IAM.md impact: none — <specific reason>` in self-review and the PR body.
   A vague "docs not needed" does not satisfy this gate. If the diff touches
   `src/building-blocks/**`, a consumer's project reference to it, or a
   public BuildingBlocks contract, classify it against
   `docs/building-blocks/README.md`'s change-impact checklist the same way:
   update the affected sections in this same task, or record the exact
   declaration `BuildingBlocks docs impact: none — <specific reason>` in
   self-review and the PR body. Cross-check a shared TSID or module-config
   change against `docs/modules/IAM.md` too. If the diff touches
   `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a consumer's
   reference to it, classify it against `docs/contracts/iam.md`'s
   change-impact checklist the same way: update the affected sections in this
   same task, or record the exact declaration
   `IAM Contract docs impact: none — <specific reason>` in self-review and
   the PR body; a change to an exported contract shape also triggers
   `docs/modules/IAM.md`'s checklist.
3. This is the second and final routine approval gate. Present findings,
   complete diff/evidence, checked acceptance criteria, remaining risks and the
   final todo state. Ask the user to choose `Approve`, `Change` or `Cancel`,
   then stop and wait before commit, push and pull-request creation.
4. On `Change`, add correction and revalidation todos, execute them one at a
   time and return to this review gate.
5. On `Cancel`, preserve the branch, ledger state, Spec and current files and
   stop without delivery.
6. After final `Approve`, do not ask for another permission. Capture the
   checked acceptance criteria and evidence for the PR body, then as separate
   visible todos:
   - change only the active `tasks/TASKS.md` row from `review` to `done`;
   - replace that row's Spec link with `—`;
   - delete exactly the active tracked Spec with
     `git rm -- <exact-active-spec-path>`;
   - verify every non-done row still has one live Spec, every done row has none,
     all dependency IDs exist and the graph has no cycle.
7. Using your own judgment, update the knowledge base only if this delivery
   needs it, as its own visible todo:
   - re-index this project with codebase-memory-mcp (index_repository,
     moderate or full mode) when the delivered change is not yet reflected
     in the graph;
   - update or add the Markdown documentation this change affects (module
     conventions, "how to add a feature/endpoint in a module" guidance,
     relevant architecture.md sections, or an existing skill file describing
     project structure) only when this slice introduced or changed that
     structure;
   - skip entirely, with a one-line note why, when neither the graph nor any
     doc/skill needs a change for this delivery. Never invent speculative
     documentation.
8. Stage the backend implementation, tests, learning note, required docs,
   `tasks/TASKS.md` and the active Spec deletion. Inspect the staged diff,
   commit with the B-ID, push once without force and open a PR to `main`.
9. Report branch, commit, PR, validation and remaining risks. Stop before the
   next task.
