---
description: Select, plan, implement, review and deliver one TenantForge slice
agent: build
---

Run exactly one TenantForge task from the public roadmap.

Arguments: `$ARGUMENTS`

Accept:

- no argument: select the first runnable non-`Done` task in `tasks/ROADMAP.md`;
- a slice ID such as `S00` or `00`;
- a task filename or path such as `000-ui-foundation.md` or
  `tasks/000-ui-foundation.md`.

Reject multiple, ambiguous or invalid arguments. Show these valid forms and stop:

```text
/task
/task S00
/task 00
/task tasks/000-ui-foundation.md
```

## 1. Synchronize main

1. Resolve the Git root and inspect `git status --short`.
2. Stop and report changed paths if the worktree is dirty. Never stash, reset,
   clean or discard them.
3. Switch to `main` and run `git pull --ff-only`.
4. Stop on conflicts, non-fast-forward state or network failure.
5. Require a clean, current `main` before task selection.

## 2. Select one task

1. Read `tasks/ROADMAP.md` and task files matching `tasks/[0-9][0-9][0-9]-*.md`.
2. Exclude `tasks/TEMPLATE.md`.
3. Parse the slice ID, `Status` and `Depends on` sections from each task.
4. Treat a dependency as complete only when its task and roadmap row are both
   `Done` on `main`.
5. With no argument, choose the first roadmap task whose status is not `Done`
   and whose dependencies are complete.
6. With an explicit task, require an exact unique match and complete
   dependencies.
7. If the task is blocked, report each pending dependency and stop.
8. If no task is runnable, report `No runnable TenantForge task.` and stop.
9. Read the complete selected task before inspecting implementation files.

## 3. Plan and approval gate

1. Read `AGENTS.md` and load `vertical-slice-delivery`.
2. Read only the product, architecture and design sections referenced by the
   selected task.
3. Inspect the nearest existing implementation and tests, if any.
4. Treat the task's API contract as fixed. Report ambiguity instead of silently
   inventing a contract.
5. Derive the implementation phases from `Owner`:
   - `ui-engineer` owns UI phases;
   - `backend-mentor` owns API, persistence and security phases;
   - for multi-agent slices, follow the written order and hand off only through
     the accepted API contract;
   - default to sequential phases on the task branch. Parallel work requires an
     explicitly approved plan with separate worktrees, disjoint ownership and a
     named integration owner.
6. Present:
   - selected task and visible outcome;
   - a demo under three minutes;
   - implementation phases and responsible agent;
   - expected files;
   - verification commands and browser checks;
   - branch name `slice/<slice-id-lowercase>-<slug>`;
   - risks, ambiguities and explicit out-of-scope work;
   - a todo preview with exactly one item intended to be in progress at a time.
7. Ask the user to choose `Approve`, `Change`, or `Cancel`, then stop and wait.
8. Before `Approve`, do not edit files, install packages, create a branch or run
   build/test commands.

## 4. Execute after approval

1. Create and switch to the proposed task branch from current `main`.
2. Create the todo list and keep exactly one item `in_progress`.
3. Invoke the implementation agent required for each phase. Give it the complete
   task, accepted plan, fixed contract, current branch and ownership boundary.
4. Follow `vertical-slice-delivery`: UI first when the task says UI defines the
   experience, minimum backend second, real integration third.
5. Run the task's explicit validation plus affected build, lint, tests and real
   browser demo.
6. Keep the task and roadmap unchanged while validation is failing.
7. Review `git status` and the full diff for unrelated files, secrets, generated
   artifacts, weakened tests and future-slice work.

## 5. Review gate

After every required check passes:

1. Present the implementation report, demo evidence, validation results and diff
   summary.
2. Ask the user to choose `Approve`, `Change`, or `Auto-review`, then stop.
3. On `Change`, apply only the requested changes, revalidate and return here.
4. On `Auto-review`, invoke `task-reviewer` with the complete task, accepted plan,
   diff and validation evidence. Present its severity-ordered findings. Fix only
   findings confirmed by the user, revalidate and return here.
5. On `Approve`, continue to delivery.

## 6. Delivery

After final approval:

1. Change the selected task's `Status` to `Done` and its roadmap row to `Done`.
   Do not delete public task files.
2. Stage only the implementation, tests, required documentation and these status
   updates.
3. Show the staged diff summary and ensure no secret or generated evidence is
   included.
4. Commit with a concise message referencing the slice ID.
5. Push the task branch once. Never force-push and never retry a failed push
   automatically.
6. Create a pull request against `main` containing the visible outcome,
   screenshots/demo evidence, validation results, security notes and postponed
   work.

## 7. Final report

Return the task, delivered behavior, changed files, validation and browser
evidence, review result, branch, commit SHA, push status, pull-request URL and
remaining risks. Never begin the next roadmap task.
