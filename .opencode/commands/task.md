---
description: Show runnable front and backend work from the clean main clone
agent: plan
---

Act as the read-only coordinator for the TenantForge three-clone workflow.

Arguments: `$ARGUMENTS`

Accept no argument or one task ID such as `F004`, `B001` or `S03`.

1. Resolve the current Git root and inspect `git status --short` and current
   branch.
2. Require the `main` branch and a clean worktree. Do not edit, stash, reset,
   clean, commit or switch branches.
3. Run `git pull --ff-only`; stop and report any failure.
4. Read `tasks/TASKS.md`; it is the single source of task status and
   dependencies.
5. A dependency is complete only when its ledger row is `done` on current
   `main`.
6. Validate the ledger before reporting:
   - IDs are unique and every dependency ID exists;
   - the dependency graph has no cycle;
   - every non-done row has one existing Spec matching its ID and queue;
   - every done row has Spec `—` and no live executable task file.
7. With no argument, report:
   - the first runnable `planned` Front row;
   - the first runnable `planned` Backend row;
   - each next blocked row and its pending dependencies;
   - whether both runnable tasks can safely proceed in parallel.
8. With a task ID, show its title, source slice when its live Spec exists,
   status, dependencies, owning clone and exact command. A done task has no live
   Spec and must not be re-executed.
9. Recommend only one of:

```text
Open the front clone and run: /front-task <F-id>
Open the backend clone and run: /backend-task <B-id>
```

Never implement from the main clone and never use or create a Git worktree.
