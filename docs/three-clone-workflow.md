# Three-clone local workflow

TenantForge deliberately uses three ordinary clones instead of Git worktrees.
Each clone has its own branch, OpenCode session, dependency state and uncommitted
files.

## Local layout

```text
TenantForge-workspace/
├── main/       # coordination, review and merged truth
├── front/      # ui-engineer tasks
└── backend/    # backend-mentor tasks
```

Create the layout once:

```bash
mkdir TenantForge-workspace
cd TenantForge-workspace
git clone https://github.com/MtKhademi/TenantForge.git main
git clone https://github.com/MtKhademi/TenantForge.git front
git clone https://github.com/MtKhademi/TenantForge.git backend
```

These are three independent clones of the same repository, not three different
repositories.

## Daily use

In `main/`:

```bash
opencode
```

```text
/task
```

This only reports runnable work and where to run it.

In `front/`:

```bash
opencode
```

```text
/front-task
```

In `backend/`:

```bash
opencode
```

```text
/backend-task
```

Each task command synchronizes only its current clone, creates its own branch,
stops for plan approval and completes the work in that same primary agent
conversation. It creates a visible todo list after approval and updates it after
every implementation, validation, self-review and delivery step. No subagent is
used.

## Parallel work

Front and backend can run simultaneously only when their task dependencies are
already `done` on `main`. They never exchange uncommitted files. Shared truth
moves between clones only after a pull request is merged and the other clone
pulls updated `main`.

Each delivery changes only its own row in `tasks/TASKS.md`. Status and
dependencies live in that ledger; detailed Specs remain complete while planned
or active and are removed only when their row becomes `done`. Front and backend
branches must never edit another row or Spec.

## Recovery

If a session stops after a task branch exists, reopen OpenCode inside that same
clone and run the same owning command:

```text
/front-task F001
```

or:

```text
/backend-task B001
```

The command detects its existing branch and preserves the current diff. It can
recover `in_progress` and `review` rows, or finish delivery when the row is
`done` and its Spec has already been removed. It never stashes, resets, cleans
or touches either sibling clone.

## Permission mode

The `ui-engineer` and `backend-mentor` primary agents auto-allow normal work in
their current clone, including edits, dependencies, build/test commands, browser
verification, Docker Compose, commits, pushes and pull-request creation. This
removes repeated OpenCode tool-permission prompts.

Guardrails remain explicit `deny` rules for sibling/external directories,
subagents, reset, clean, stash, rebase, restore, force-push, recursive removal
and destructive Docker/database cleanup. Plan approval and final delivery
approval are conversation gates, not tool-permission popups, and remain enabled
so the learner sees and controls the workflow.
