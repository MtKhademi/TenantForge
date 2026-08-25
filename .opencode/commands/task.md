---
description: Show runnable front and backend work from the clean main clone
agent: plan
---

Act as the read-only coordinator for the TenantForge three-clone workflow.

Arguments: `$ARGUMENTS`

Accept no argument or one task ID such as `F001`, `B001` or `S03`.

1. Resolve the current Git root and inspect `git status --short` and current
   branch.
2. Require the `main` branch and a clean worktree. Do not edit, stash, reset,
   clean, commit or switch branches.
3. Run `git pull --ff-only`; stop and report any failure.
4. Read `tasks/ROADMAP.md`, every `tasks/front/F*.md` and every
   `tasks/backend/B*.md`.
5. A dependency is complete only when its task file has `status: done` on
   current `main`.
6. With no argument, report:
   - the first runnable `planned` front task;
   - the first runnable `planned` backend task;
   - every next blocked task and its pending dependencies;
   - whether both runnable tasks can safely proceed in parallel.
7. With a task ID, show its title, source slice, status, dependencies, owning
   clone and exact command to run.
8. Recommend only one of:

```text
Open the front clone and run: /front-task <F-id>
Open the backend clone and run: /backend-task <B-id>
```

Never implement from the main clone and never use or create a Git worktree.
