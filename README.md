# TenantForge

TenantForge is an open-source, production-minded starter kit for building multi-tenant SaaS products with .NET and React.

The project is built in very small, visible vertical slices. Every backend capability must have an immediate UI consumer, a short browser demo, and a learning note that explains the request flow and design decisions.

## Why TenantForge exists

Most starter kits arrive as a large finished codebase. They are quick to clone but difficult to understand, modify, or trust.

TenantForge takes the opposite approach:

- build from an empty repository;
- keep `main` runnable and demonstrable;
- introduce one backend concept at a time;
- show every capability in the browser as soon as it exists;
- document why the code was written, not only what it does;
- grow from a development-only admin login into a real multi-tenant IAM system.

The repository is useful both as a reusable SaaS foundation and as a guided, real-world .NET learning project.

## Target product

The first public milestone will support this complete journey:

```text
Run the project
→ Sign in as the platform administrator
→ View the dashboard
→ Create and manage users
→ Create tenants
→ Switch between tenants
→ Assign roles and permissions
→ See permission-aware navigation and 403 states
→ Inspect sensitive changes in the audit log
```

## Target stack

### Backend

- .NET 10 and ASP.NET Core
- Modular monolith with vertical slices
- Minimal APIs and OpenAPI
- EF Core with PostgreSQL
- FluentValidation
- xUnit integration tests with Testcontainers
- Docker Compose for local infrastructure

### Frontend

- React and TypeScript
- Vite
- Tailwind CSS and shadcn/ui
- React Router
- TanStack Query
- React Hook Form and Zod
- Vitest and Playwright

Versions are introduced and locked by the task that first needs them. The bootstrap repository intentionally contains no application code yet.

## Delivery principles

1. **Visible first** — every slice changes something a person can see or exercise in the browser.
2. **One active task per clone** — front and backend may overlap only where the
   explicit dependency graph allows it.
3. **No speculative backend** — an endpoint needs a named current or immediately
   dependent front consumer in the roadmap.
4. **Small contracts** — a slice normally adds no more than one or two endpoints.
5. **Teach through the code** — backend slices include a concise learning note under `docs/learning/`.
6. **Secure by environment** — temporary shortcuts such as the hardcoded admin are development-only and must fail closed in production.
7. **Demoable main** — a broken or half-integrated slice is never merged into `main`.

## Roadmap

| Slice | Visible outcome | Backend evolution |
|---|---|---|
| S00 | Professional login and dashboard shell using mock data | None |
| S01 | Full admin can sign in and open the dashboard | Development-only hardcoded admin and signed token |
| S02 | Protected route, current admin and logout | `GET /auth/me` and authentication guard |
| S03 | Dashboard displays real summary data | Dashboard summary query |
| S04 | Login still works while persistence is introduced | Account model, EF Core and migration |
| S05 | Admin survives database recreation and application restarts | Idempotent platform-admin seed and database authentication |
| S06 | Users can be listed and created from the UI | User query, command and validation |
| S07 | Default tenant appears in a tenant switcher | Tenant and membership model |
| S08 | Admin switches between two isolated tenants | Tenant context and isolation enforcement |
| S09 | Roles and permissions are editable in a matrix | Tenant RBAC and authorization policies |
| S10 | Navigation reacts to permissions; audit and invitations are visible | Permission-aware API, audit log and invitation flow |

See [tasks/ROADMAP.md](tasks/ROADMAP.md) for dependencies and completion rules.

## OpenCode workflow

TenantForge includes two implementation agents and one read-only reviewer:

- `ui-engineer` owns the frontend, browser states, responsiveness and visual verification.
- `backend-mentor` implements only the backend required by the active slice, tests it, and explains it.
- `task-reviewer` checks the finished diff against visible behavior, scope,
  security, tests and the learning goal without editing files.

OpenCode loads their definitions from `.opencode/agents/`. Project-specific skills live in `.opencode/skills/`.

Use three independent clones of the same repository:

```text
TenantForge-workspace/
├── main/       # status, coordination and merged truth
├── front/      # UI task branches
└── backend/    # backend task branches
```

These are normal Git clones, not worktrees. Full setup and daily usage are in
[the three-clone workflow](docs/three-clone-workflow.md).

Open OpenCode separately in each folder. From `main/`, inspect the two queues:

```text
/task
```

From `front/`, execute only frontend work:

```text
/front-task
/front-task F001
```

From `backend/`, execute only backend work:

```text
/backend-task
/backend-task B001
```

Each command selects the first runnable task from its own folder, checks
cross-queue dependencies, prepares a bounded plan, waits for approval, invokes
the responsible agent, validates the result and stops again before delivery.
If a session is interrupted on an existing task branch, run `/task-run F001` or
`/task-run B001` in that same clone.

Focused read-only commands also remain available:

- `/start-slice tasks/front/F001-ui-foundation.md` inspects one task;
- `/review-slice tasks/backend/B001-development-login-api.md` reviews one diff;
- `@ui-engineer` and `@backend-mentor` can still be invoked manually for a
  narrowly-scoped phase.

Install the optional upstream UI skills locally in this repository:

```bash
npx skills add anthropics/skills \
  --skill frontend-design \
  --skill webapp-testing \
  -a opencode --copy

npx skills add shadcn/ui \
  --skill shadcn \
  -a opencode --copy

npx skills add vercel-labs/agent-skills \
  --skill vercel-react-best-practices \
  -a opencode --copy
```

Review third-party skill contents and licenses before committing copied files. TenantForge's own skills are already included.

## Repository structure

```text
.
├── .opencode/
│   ├── agents/          # UI engineer, backend mentor and reviewer
│   ├── commands/        # /task, /front-task and /backend-task
│   └── skills/          # TenantForge-specific workflows
├── docs/                # Product, architecture and UI direction
├── tasks/
│   ├── front/           # Executable F tasks
│   ├── backend/         # Executable B tasks
│   └── slices/          # Complete product/API specifications
├── AGENTS.md            # Shared rules for every coding agent
├── CONTRIBUTING.md
└── opencode.jsonc       # Safe project-level OpenCode configuration
```

Application directories are created by the first tasks rather than hidden in a prebuilt template.

## Current status

**Bootstrap / documentation-first.** The product code begins with S00.

## License

TenantForge is released under the [MIT License](LICENSE).
