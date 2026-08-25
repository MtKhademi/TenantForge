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
stops for plan approval, invokes the responsible agent, validates the work and
stops again before delivery.

## Parallel work

Front and backend can run simultaneously only when their task dependencies are
already `done` on `main`. They never exchange uncommitted files. Shared truth
moves between clones only after a pull request is merged and the other clone
pulls updated `main`.

Each task changes only its own status file. The static roadmap is not edited by
delivery branches, preventing routine front/backend merge conflicts.

## Recovery

If a session stops after a task branch exists, reopen OpenCode inside that same
clone and run:

```text
/task-run F001
```

or:

```text
/task-run B001
```

The recovery command preserves the current diff. It never stashes, resets,
cleans or touches either sibling clone.
