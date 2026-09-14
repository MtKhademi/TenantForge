# S21 — Give agents one living IAM knowledge source

## Outcome

`docs/modules/IAM.md` becomes the current, searchable handbook for the IAM
module. An agent answering an IAM question reads this file first and can quickly
find the owning code path, current contract, security rule and verification
command without reconstructing the module from old task history.

The handbook is living documentation, not a task diary. It describes current
merged behavior only and contains no stale `planned`, task-status or temporary
implementation language.

## Questions it must answer

The document is organized for lookup and must answer at least:

- What does IAM own, and what is explicitly outside it?
- How does the API register and activate the module?
- Which configuration keys exist, which are secrets and how do they fail closed?
- Which domain entities and database tables exist?
- How are TSIDs represented in PostgreSQL, .NET, JSON, routes and JWTs?
- Which endpoint exists, which policy/permission protects it and what shape does
  it read/write?
- How are platform admin, tenant membership, tenant roles and permission keys
  resolved?
- What tenant-isolation rules must never be weakened?
- How do migration, startup seeding and invitation/audit behavior work?
- Which errors and pagination rules are stable?
- Where are the relevant tests and how are focused/full checks run?
- What is implemented, deliberately postponed or operationally important?

## Required structure

Use stable headings, compact tables and exact repository paths:

1. Purpose and non-goals
2. Fast facts
3. Dependency and composition boundary
4. Source-code map
5. Configuration and secrets
6. Identity/TSID contract
7. Domain model and invariants
8. Persistence schema and migrations
9. Authentication and JWT claims
10. Authorization and tenant isolation
11. Endpoint catalog
12. Pagination and error semantics
13. Seeding and startup order
14. Test map and verification commands
15. Current limitations and postponed work
16. Change-impact/update checklist

The endpoint catalog covers all current routes: login, current account, current
account tenants, dashboard summary, platform users, platform tenants, tenant
members, permission catalog, tenant roles, member-role assignment/removal,
resolved permissions, invitations and audit.

## Source-of-truth rules

`IAM.md` summarizes code; it does not replace code, migrations or tests.
When they disagree, the agent must report the mismatch and verify behavior
before correcting the handbook. Do not copy secrets, full migration SQL, full
DTO definitions or long learning-note prose into it. Link exact paths instead.

Historical slices and `docs/learning/**` explain why a change happened.
`IAM.md` answers what is true now. Architecture documentation explains
system-wide decisions. Avoid duplicating large blocks across all three.

## Automatic maintenance gate

After S21 is delivered, every backend task that reads or changes
`src/modules/iam/**` must read `docs/modules/IAM.md` during discovery and run
an IAM documentation impact check before review.

The task updates `AGENTS.md`, `backend-task`, `review-slice`,
`backend-mentor` and `vertical-slice-delivery` so the agent must:

1. classify the IAM diff against the handbook sections;
2. update `IAM.md` in the same task when routes, request/response contracts,
   configuration, composition, IDs, domain invariants, persistence, auth,
   permissions, tenancy, startup, tests or limitations change; or
3. state `IAM.md impact: none — <specific reason>` in self-review and the PR
   body when no handbook fact changed.

A vague “docs not needed” is not accepted. Review must stop when production IAM
behavior changed but neither an `IAM.md` edit nor a defensible no-impact
statement exists.

## Verification

- Every documented path resolves on current main.
- Every route mapped by `IamModule` appears exactly once in the endpoint table.
- Configuration and permission keys are derived from current code.
- A fixed question drill can be answered from the handbook with a section and
  owning path for each answer.
- Existing API/browser smoke behavior still matches the handbook.
- No runtime code, database schema or frontend file changes in this docs/workflow
  task.

## Acceptance

- One concise, navigable `docs/modules/IAM.md` describes current IAM behavior.
- The handbook contains no credentials, secret values or fabricated behavior.
- Agent/task/review workflows enforce the impact gate automatically.
- The file is linked from root architecture/README navigation.
- Route, config, permission, entity, table and test inventories match code.
- The question drill and link/coverage checks pass.
