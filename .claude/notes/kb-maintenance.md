# Codebase-memory KB maintenance

Maintenance note for the `codebase-memory-mcp` knowledge base used in this
clone. `/front-task` and `/backend-task` search this MCP before scanning the
repository, so a stale index costs every task. Update the verification section
after each `index_repository` run.

## Projects (codebase-memory-mcp)

Run `list_projects` and match on `root_path` — never on a remembered name.

| Project | Root | Notes |
|---|---|---|
| `tenantforge-backend` | `/mnt/c/me/source/TenantForge/backend` | This clone. Verified 2026-09-20: `ready`, 3011 nodes / 6132 edges, head `2b591c3`. |

Sibling `front` and `main` clones are indexed as their own projects when they
exist. A project whose `root_path` no longer exists is dead — delete it with
`delete_project` rather than querying it.

## Argument names differ between tools

- `index_status`, `detect_changes`, `search_graph`, `search_code`,
  `get_code_snippet`, `trace_path`, `get_architecture`, `query_graph` → take
  **`project`**.
- `index_repository` → takes **`name`** (plus `path` and `mode`).

Passing the wrong one returns `missing required argument`, not a silent
failure. Read the error before assuming the project is missing.

## Maintenance rule of thumb

1. Before trusting graph results, call `index_status` and compare `head_sha`
   to `git rev-parse HEAD`.
2. A matching SHA is not enough. Call `detect_changes` — a long
   `changed_files` list means the graph predates real edits.
3. When the graph is behind, run `index_repository` with `mode: "full"`.
   `fast` misses symbols in some files.
4. After any committed work, a background reindex may cover small changes.
   Verify with one `search_graph` on a newly added symbol; if it is missing,
   force a full reindex.
5. Always verify an MCP hit against the actual file before editing. The graph
   summarizes code, it does not replace it.
6. Non-code files (CSS, Markdown, JSON) index as `parse_partial` or not at all.
   Use `rg` for those.

## Known failure (verified 2026-09-20)

`index_repository` on `tenantforge-backend` fails in every mode (`fast`,
`moderate`, `full`) with:

```text
{"status":"error","outcome":"exit_nonzero",
 "hint":"Indexing worker crashed on a file. The crash was contained
 (the server survived). Re-run to retry..."}
```

Re-running does not help. The existing index stays `ready` at head `2b591c3`
and remains usable for queries — it just cannot be advanced. Until this is
fixed, report the refresh as attempted-and-failed rather than skipped, and
verify any MCP hit against the current file (which is required anyway).
