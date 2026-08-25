# TenantForge web

S00 creates the frontend-only UI foundation.

## Start

```bash
npm install
npm run dev
```

Open `http://localhost:5173/login`.

## Development mock administrator

The S00 login uses an isolated, temporary mock auth adapter. S01 replaces this adapter with the real HTTP login endpoint.

```text
Email: admin@tenantforge.local
Password: local-development-password
```

The mock adapter stores a session in `sessionStorage`, so the dashboard survives a browser refresh in the same tab. Signing out or closing the tab clears the mock session.
