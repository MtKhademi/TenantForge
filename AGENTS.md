# TenantForge agent rules

TenantForge is built one visible vertical slice at a time. These rules apply to every agent and every task.

## Before changing files

1. In the `front` clone start with `/front-task`; in the `backend` clone start
   with `/backend-task`. Use `/task` only for read-only coordination from the
   `main` clone. Run the owning command again to recover an interrupted task
   branch.
2. Read the active row in `tasks/TASKS.md` and its complete linked Spec. The
   ledger is authoritative for status and dependencies; the Spec is
   authoritative for detailed scope and acceptance.
3. Read the agent knowledge for your area completely:
   `docs/knowledge/AGENT-frontend.md` for `/front-task`,
   `docs/knowledge/AGENT-backend.md` for `/backend-task`. This file and those
   two are the required reading for every task.
4. Read only the product, architecture or design documents referenced by that Spec.
5. State the visible outcome, files you expect to touch and what remains out of scope.
6. Confirm the task has a browser demo. If it has no visible consumer, stop and propose a smaller visible slice.
7. After synchronization and task selection, create the owning task branch
   before deep analysis. Backend tasks then wait for explicit plan approval.
   Frontend tasks present the plan for visibility and continue automatically
   without waiting for user approval.

## Single-agent execution

- `/front-task` runs directly in the primary `ui-engineer` conversation.
- `/backend-task` runs directly in the primary `backend-mentor` conversation.
- Never call the `task` tool, start a subagent or delegate a phase.
- Backend execution has exactly two blocking user-approval gates:
  1. after reading/analyzing the task, present the summary and complete plan,
     ask `Approve`, `Change` or `Cancel`, then wait;
  2. after implementation, validation and self-review are complete, present the
     final diff/evidence, ask `Approve`, `Change` or `Cancel`, then wait
     before commit, push and pull-request creation.
- Frontend execution has zero routine user-approval gates. After reading the
  task, present the complete plan and todos as information, call `todowrite`
  immediately and continue through implementation, build, lint, browser QA,
  self-review, correction, commit, push and PR creation without asking the user
  to approve or continue.
- Keep exactly one todo item `in_progress`. Todo/progress updates are
  informational, not permission requests. Immediately continue to the next
  todo after reporting its evidence; never ask whether to start, continue, run
  normal validation, self-review, delivery or move to the next step.
- Pause only for a real blocker, a material scope/contract change, missing
  authority, a destructive action requiring approval, or an explicit user
  interruption. A normal frontend review finding is not a blocker: add the
  smallest correction and revalidation todos, fix it and repeat self-review.
- After the second backend approval, or after a clean automated frontend
  self-review, finish commit, push and PR creation without another routine
  approval.

## Task ledger and Spec lifecycle

- `tasks/TASKS.md` is the single source of task status and dependencies.
- Every non-done row has exactly one complete executable Spec under
  `tasks/front/` or `tasks/backend/`; every done row has Spec `—` and no live
  executable task file.
- For backend tasks, after plan approval change only the active row to
  `in_progress`. For frontend tasks, do so immediately after presenting the
  informational plan. After validation, change it to `review`.
- For backend tasks, after final delivery approval change only the active row
  to `done`. For frontend tasks, do so after automated self-review is clean.
  Replace its Spec link with `—` and delete exactly that tracked Spec in the
  same delivery commit.
- Preserve source slices and Git/PR history as the permanent record. Never
  delete another task's Spec or use file absence alone as proof of completion.

## Scope discipline

- Keep exactly one task active.
- Work only inside the current clone. Never inspect or modify sibling `main`,
  `front` or `backend` directories.
- Do not implement future `tasks/TASKS.md` items.
- Do not create an endpoint without a named current or immediately dependent
  front task as its consumer, except a health endpoint required to run the
  system.
- Normally add no more than two endpoints in one slice.
- Prefer direct, readable code over abstractions created for hypothetical future requirements.
- Do not combine IAM, tenancy, permissions, caching and audit work in the same slice.
- Do not alter an accepted API contract without explaining the change and updating the active task first.

## Ownership

### UI engineer

- Own `src/web/**` and browser evidence. Do not inspect, create, update or run frontend tests.
- May use mocks only when the active task explicitly allows them.
- Must not modify backend projects, database migrations or authorization policies.
- Must not create screens beyond the active task.

### Backend mentor

- Own `src/api/**`, `src/modules/**`, backend tests and backend learning notes.
- Must not redesign or restyle the frontend.
- Implements only the API contract required by the active task.
- Creates `docs/learning/<task-id>-<slug>.md` for every backend slice.

Shared files such as root configuration, Docker Compose and CI have one owner named in the active task.

## Target structure

The first tasks create this structure gradually:

```text
src/
├── api/
├── modules/
│   └── iam/
└── web/
tests/
├── integration/
└── e2e/
```

Do not create empty projects or folders for future modules.

## Verification

A task is complete only when:

- the visible outcome works in a real browser;
- the happy path and the relevant failure path are demonstrated;
- frontend changed code builds and lints successfully; backend changed code builds and tests successfully;
- no browser console error is introduced;
- security-sensitive API behavior is covered by an integration test;
- generated files are separated from authored changes in the review summary;
- the task acceptance criteria are checked with evidence;
- the agent stops instead of beginning the next task.

## Backend learning note

Write concise notes that explain:

1. files changed and why;
2. request flow from endpoint to response;
3. the backend concepts introduced;
4. important security decisions;
5. alternatives deliberately postponed;
6. commands and manual steps to verify the slice;
7. three review questions for the learner.

Do not turn the learning note into framework documentation. Explain only the code introduced in the slice.

## Knowledge base

TenantForge keeps two kinds of knowledge per area. `docs/knowledge/README.md`
is their index.

- **Agent knowledge** — `docs/knowledge/AGENT-backend.md` and
  `docs/knowledge/AGENT-frontend.md`. Short operational memory for a
  lightweight agent: paths, boundaries, conventions, reusable patterns,
  commands, contract and authorization rules, known traps, and decisions later
  tasks must preserve. Read the file for your area completely before every
  task. This `AGENTS.md` is the shared agent knowledge; change it only when a
  genuinely shared rule changes.
- **Human knowledge** — `docs/knowledge/HUMAN-backend.md` and
  `docs/knowledge/HUMAN-frontend.md`. Written for the repository owner. Never
  required reading before implementing a task, so it does not consume agent
  context. Always read it before editing it, so a task does not overwrite an
  unrelated explanation.

After verification and **before committing**, every `/front-task` and
`/backend-task` run updates the agent knowledge with durable facts it learned or
changed, and the human knowledge with a clear explanation of the completed work.
Backend tasks additionally write `docs/learning/<task-id>-<slug>.md`. Knowledge
describes the final verified implementation, not the plan. Never record
debugging detail, speculation, abandoned approaches, task-specific noise,
secrets or large copied code blocks. A task that produced no durable knowledge
change adds nothing and says so in its completion report.

## Code search

Use the Codebase Memory MCP (`codebase-memory-mcp`, also shown as
`MCP-CodeBaseServer`) first when looking for any file, class, endpoint,
component, contract, symbol, existing implementation or usage. Verify every MCP
result against the actual current file before editing — the graph can be stale.
When the MCP returns nothing, a partial result or a stale path, fall back to
repository search such as `rg`; a missing MCP result is never a reason to stop.
Never guess a file path, type name, endpoint or contract member. Refresh the
index with `index_repository` after delivery when the change is not yet in the
graph, and report the exact outcome — a refresh the server does not expose, or
one that failed, is reported as such and never as a success.

## Living module knowledge

- `docs/modules/IAM.md` is the current, searchable handbook for the IAM
  module. Any task that reads or changes `src/modules/iam/**`, or changes an
  IAM contract living in `TenantForge.BuildingBlocks`/the API host, reads
  `docs/modules/IAM.md` first during discovery, then verifies the relevant
  facts against current code — the handbook summarizes code, it does not
  replace it.
- Before moving to review, classify the actual diff against `IAM.md`'s
  change-impact checklist (its final section). When a documented fact
  changed (routes, request/response shapes, configuration, composition, IDs,
  domain invariants, persistence, auth, permissions, tenancy, startup, tests
  or limitations), update `IAM.md` in the same task and validate it against
  code.
- Otherwise state the exact declaration in self-review and the PR body:
  `IAM.md impact: none — <specific reason>`. A vague "docs not needed" is not
  accepted; review blocks a production IAM diff that has neither an `IAM.md`
  edit nor a defensible no-impact statement.
- Historical learning notes and source slices explain why a past change
  happened; they never override the current handbook or current code when
  the two disagree.
- Future modules that gain the same kind of living handbook follow this same
  read-first/impact-gate shape.

## Living Shop knowledge

- `docs/modules/SHOP.md` is the current, searchable handbook for the Shop
  module. Any task that reads or changes `src/modules/shop/**`, or changes a
  Shop contract living in `TenantForge.BuildingBlocks`/the API host, reads
  `docs/modules/SHOP.md` first during discovery, then verifies the relevant
  facts against current code — the handbook summarizes code, it does not
  replace it.
- Before moving to review, classify the actual diff against `SHOP.md`'s
  change-impact checklist (its final section). When a documented fact
  changed (routes, request/response shapes, configuration, composition,
  domain invariants, persistence, tenant/auth rules, product media,
  inventory reservation, sandbox payment, tests or limitations), update
  `SHOP.md` in the same task and validate it against code.
- Otherwise state the exact declaration in self-review and the PR body:
  `SHOP.md impact: none — <specific reason>`. A vague "docs not needed" is
  not accepted; review blocks a production Shop diff that has neither a
  `SHOP.md` edit nor a defensible no-impact statement.
- Historical learning notes and source slices explain why a past change
  happened; they never override the current handbook or current code when
  the two disagree.

## Living BuildingBlocks knowledge

- `docs/building-blocks/README.md` is the current, searchable handbook for
  `TenantForge.BuildingBlocks`. Any task that touches
  `src/building-blocks/**`, a consumer's project reference to it, or a
  public BuildingBlocks contract (`IModuleConfig`, `TsidId`) reads
  `docs/building-blocks/README.md` first during discovery, then verifies the
  relevant facts against current code.
- Before moving to review, classify the actual diff against the guide's
  change-impact checklist (its final section). When a documented fact
  changed (exported type, dependency direction, package reference,
  consumer, contract member, exclusion), update the guide in the same task.
- Otherwise state the exact declaration in self-review and the PR body:
  `BuildingBlocks docs impact: none — <specific reason>`. A vague "docs not
  needed" is not accepted; review blocks a production BuildingBlocks diff
  that has neither a guide edit nor a defensible no-impact statement.
- A shared TSID or module-config contract change also triggers
  `docs/modules/IAM.md`'s own change-impact checklist, since IAM is the
  current consumer of both contracts.
- This guide's admission checklist governs every proposed addition to
  BuildingBlocks: incomplete admission evidence means the type stays in its
  owning module.

## Living IAM Contract knowledge

- `docs/contracts/iam.md` is the current, searchable handbook for
  `TenantForge.Modules.Iam.Contract`. Any task that touches
  `src/modules/iam/TenantForge.Modules.Iam.Contract/**` or a consumer's
  project reference to it reads `docs/contracts/iam.md` first during
  discovery, then verifies the relevant facts against current code.
- Before moving to review, classify the actual diff against the guide's
  change-impact checklist (its final section). When a documented fact
  changed (exported type, member, dependency direction, incoming reference,
  exclusion, or the hand-enumerated roster in `IamContractArchitectureTests`),
  update the guide in the same task.
- Otherwise state the exact declaration in self-review and the PR body:
  `IAM Contract docs impact: none — <specific reason>`. A vague "docs not
  needed" is not accepted; review blocks a production IAM Contract diff that
  has neither a guide edit nor a defensible no-impact statement.
- A change to an exported contract shape also triggers
  `docs/modules/IAM.md`'s own change-impact checklist (its endpoint catalog
  documents the same shapes); a TSID-format change additionally triggers
  `docs/building-blocks/README.md`'s checklist, since the identifier seam the
  contract strings are formatted through lives there.
- This guide's admission checklist governs every proposed addition to the
  Contract project: incomplete admission evidence means the type stays in its
  owning module.

## Git safety

- Use three ordinary clones named `main`, `front` and `backend`. Never create or
  use a Git worktree.
- For a fresh task, use the bounded command preflight: synchronize `main`,
  select the first runnable owning task, create `front/fxxx-<slug>` or
  `backend/bxxx-<slug>`, then analyze it. Request plan approval only for a
  backend task; a frontend task continues automatically after displaying its
  plan.
- If tracked changes are proven to be CRLF/LF-only with
  `git diff --ignore-cr-at-eol --quiet`, the owning command may run exactly
  `git restore --worktree -- .` once. Never use that exception for staged,
  untracked or substantive changes.
- Git preflight is not an investigation: do not inspect binaries, aliases, PATH
  or config, and do not repeat equivalent status commands.
- On a clean owning task branch, the owning command fetches `origin/main` once.
  If that exact task row is already `done` with Spec `—` on remote main, it
  automatically switches to local `main`, pulls and continues to the next
  runnable task. This delivery-exit transition is not recovery and requires no
  manual Git command.
- Show `git status` and `git diff` before proposing a commit.
- Never force-push, rewrite shared history or push directly to `main`.
- Do not commit secrets, local credentials, database data or generated browser artifacts.
- Keep `main` runnable and demoable.
- During delivery, update only the active `tasks/TASKS.md` row and delete only
  its exact linked Spec after final backend approval or clean automated
  frontend self-review. Verify all other live Spec links and dependencies
  before committing.
- Require a second user approval after backend validation and review before
  commit, push and pull-request creation. Frontend validation and review are
  automatic and must continue directly to delivery when clean.

## Security baseline

- Development-only authentication shortcuts must be impossible to enable silently in production.
- Never log passwords, tokens or secrets.
- Treat tenant isolation and authorization as server-side responsibilities; hiding UI is not authorization.
- Default to deny when tenant or permission context is missing.
- Add complexity such as refresh-token rotation, caching or impersonation only in the task that demonstrates it.
