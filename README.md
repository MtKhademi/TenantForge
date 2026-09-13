# TenantForge

TenantForge is an open-source, production-minded starter kit for building multi-tenant SaaS products with .NET, PostgreSQL and React.

The project grows through small, visible vertical slices. Each delivered capability has a browser-visible consumer, an explicit API contract, and learning notes for backend slices.

## What works today

The current milestone includes:

- development sign-in for the seeded platform administrator;
- authenticated session recovery in the browser tab;
- a platform dashboard backed by the API health/summary contract;
- platform user listing and creation;
- tenant creation with an initial owner;
- tenant switching through a Persian RTL shell with a right sidebar and mobile drawer;
- tenant member visibility with server-enforced isolation;
- tenant roles and permission matrix rendered from the server catalog;
- permission-aware navigation for invitations and audit logs;
- invitation creation with built-in or tenant custom role names;
- active pending invitation list with expiry times;
- tenant audit log for invitation and role changes.

Live task status, dependencies and executable Specs are tracked only in [tasks/TASKS.md](tasks/TASKS.md).

## Current limitations

These are intentionally deferred to later slices:

- invitation acceptance and account registration from an invitation;
- email delivery, resend and revoke actions;
- refresh-token rotation and long-lived session management;
- collection pagination in the UI;
- a broad Persian user guide.

The invitation UI therefore shows pending invitation records only. It must not be read as proof that an email was sent or that acceptance is implemented.

## Stack

### Backend

- .NET 10 and ASP.NET Core Minimal APIs
- Modular monolith with vertical slices in the IAM module
- EF Core with PostgreSQL
- PostgreSQL 16 through Docker Compose for local infrastructure
- xUnit integration tests with Testcontainers
- `/health` for local API health checks

### Frontend

- React 19 and TypeScript
- Vite
- Tailwind CSS with project design tokens
- React Router
- React Hook Form and Zod
- lucide-react icons
- Vitest and Playwright are installed, but frontend test ownership is decided by task policy

## Running locally

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20+ and npm
- [Docker](https://www.docker.com/) for PostgreSQL

On the reference WSL setup there is no Linux `dotnet` binary; use `dotnet.exe` from the Windows SDK. See [docs/architecture.md](docs/architecture.md#local-development-environment-wsl--windows-net-sdk) for details.

### 1. Start PostgreSQL

```bash
docker compose up -d postgres
```

The Compose file runs PostgreSQL 16 with database/user/password `tenantforge` and publishes port `5432`.

### 2. Run the API

Linux/macOS shell with `dotnet` available:

```bash
dotnet run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

Reference WSL setup:

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

On startup the API applies EF Core migrations and seeds the development platform administrator when absent.

Check health:

```bash
curl http://localhost:5000/health
```

If the API runs as a Windows process from WSL, `localhost` may not reach it from Linux tools; use the WSL gateway IP as described in `docs/architecture.md`.

### 3. Run the web app

In a second terminal:

```bash
cd src/web
npm install
npm run dev
```

Open:

```text
http://localhost:5173/login
```

The Vite dev server proxies `/api` to the API host, so browser requests stay same-origin during local development.

### Development sign-in

The development seed creates this platform administrator in `Development`:

```text
Email: admin@tenantforge.local
Password: local-development-password
```

The production login screen does not display these credentials. They remain documented here for local setup only.

## Useful commands

```bash
# Frontend
cd src/web
npm run lint
npm run build

# Backend, with dotnet available
dotnet build src/api/TenantForge.Api/TenantForge.Api.csproj
dotnet test

# Backend, reference WSL setup
dotnet.exe build src/api/TenantForge.Api/TenantForge.Api.csproj
dotnet.exe test
```

Frontend tests are not part of every UI task by default; follow the active task owner policy in `AGENTS.md` and `tasks/TASKS.md`.

## Delivery principles

1. **Visible first** — every slice changes something a person can see or exercise in the browser.
2. **One active task per clone** — front and backend may overlap only where the dependency graph allows it.
3. **No speculative backend** — an endpoint needs a named current or immediately dependent front consumer.
4. **Small contracts** — a slice normally adds no more than one or two endpoints.
5. **Teach through the code** — backend slices include concise learning notes under `docs/learning/`.
6. **Secure by environment** — development shortcuts must fail closed outside development.
7. **Demoable main** — a broken or half-integrated slice is never merged into `main`.

## OpenCode workflow

TenantForge uses three ordinary clones, not Git worktrees:

```text
TenantForge-workspace/
├── main/       # coordination and merged truth
├── front/      # UI task branches
└── backend/    # backend task branches
```

Run `/task` from `main`, `/front-task` from `front`, and `/backend-task` from `backend`. Full setup and recovery details are in [docs/three-clone-workflow.md](docs/three-clone-workflow.md).

## Repository structure

```text
.
├── .opencode/              # OpenCode agents, commands and skills
├── docs/                   # Architecture, design direction and learning notes
├── src/
│   ├── api/                # ASP.NET Core API host
│   ├── modules/            # Modular monolith modules
│   └── web/                # React frontend
├── tasks/                  # Ledger, executable Specs and historical source slices
└── tests/                  # Backend integration tests
```

Historical slice files and learning notes are preserved as the project record after executable task Specs are delivered.
