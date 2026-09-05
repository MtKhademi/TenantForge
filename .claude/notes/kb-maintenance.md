# Codebase-memory KB maintenance

Maintenance note for the codebase-memory MCP knowledge base used in this clone. Update the verification section after each `index_repository` run.

## Projects (codebase-memory-mcp)

| Project | Root | Notes |
|---|---|---|
| tenant-forge-front | /mnt/d/sources/tenant-forge/front | Watched; auto-refresh can update branch/head_sha but leave graph content stale (seen: head said f700690 but graph was a 2026-08-26 `fast` snapshot). After F009 delivery (2026-09-05) ran `index_repository` with `name: "tenant-forge-front"` → full mode, 1191 nodes / 2446 edges, head f700690. |
| mnt-d-sources-tenant-forge-backend | /mnt/d/sources/tenant-forge/backend | Read-only reference (B006 wire contract). Refresh after backend commits if used for contract checks. |
| match-app-agent-a | /mnt/d/sources/match-app/agent-a | Unrelated to TenantForge. |
| opencode-home | ~/.opencode | Tiny config index. |

## Maintenance rule of thumb

1. Before trusting graph results on the front clone, call `index_status` (project `tenant-forge-front`) and compare `head_sha`/`base_sha` to `git rev-parse HEAD`.
2. SHA match is NOT enough: `check_index_coverage` on the files you cite. `freshness: "not_tracked"` + old `indexed_at` → force `index_repository` (mode `full` for accurate symbol coverage; `fast` misses symbols in some files).
3. After any committed work, a background reindex may be enough for small changes; verify with one `search_graph` on a newly added symbol. If missing, force reindex.
4. Frontend coverage caveat: `src/web/src/index.css` is `parse_partial` (CSS, not code) — ignore.

## Verified F009 symbols (2026-09-05)

- `src/web/src/features/users/userAdapter.ts`: `httpUserAdapter` (object, line 152), `UserValidationError` (class), `UserForbiddenError` (class), `normalizeUtcTimestamp` (function).
- `src/web/src/features/users/userTypes.ts`: `UserConflictError` (class).
