# F057 — Bind storefront profile client to B039 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F057` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F047**.
- Slice: `S35`; depends on `F056, B039`.
- Backend source of truth: `B039` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before starting, follow the boilerplate in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `S35` slice file, read the paired backend task `B039` and its delivered contract, and follow the existing branch-naming/ledger-update rules. Do not skip those — this checklist only covers the coding steps.

1. Open `docs/design/shop/http-contracts.md` and find the B039 section. Compare every route, field name, and status code listed there against the delivered C# request/response records and B039's integration tests. If anything differs from what this Spec below describes, stop, write down the exact mismatch, and ask for a contract correction decision before writing any code.
2. Create `src/web/src/features/shop/clients/httpShopProfileClient.ts`. This is the primary file you should need to touch for this task, along with permission constants/nav requirement and composition (see step 9).
3. Bind the admin `GET` profile route and admin `PUT` (update) profile route, plus the public `GET` profile route, to real HTTP calls via `shopFetch`.
4. For the admin `GET` route: if the backend returns a null profile (no profile configured yet), the client must return `null` — do not substitute an empty object or default values.
5. For the admin `PUT` route: on a 409 Conflict response, the client must preserve the form's current in-progress values (do not clear or reset the form on conflict) so the existing UI's conflict-handling behavior keeps working unchanged.
6. For the public `GET` route: if the backend returns 404 Not Found, the client must map that to `null` as a neutral fallback (this represents "no public profile to show"), not to a thrown error.
7. In the admin `GET` implementation, call `shopFetch` against the shop profile path, parse the JSON with `shopProfileResponseSchema.parse(...)`, and return `response.profile`. Reference implementation (copy/adapt this exactly):

```ts
const response = shopProfileResponseSchema.parse(
  await shopFetch(tenantShopPath(tenantId, 'profile'), { signal }),
)
return response.profile
```

8. Add the permission key `Shop.Settings.Manage` on the frontend side, in `src/web/src/features/roles/roleTypes.ts`: add `export const SHOP_SETTINGS_MANAGE_KEY = 'Shop.Settings.Manage'` beside the existing `SHOP_CATALOG_MANAGE_KEY` and `SHOP_SHIPPING_MANAGE_KEY`, and add `'Shop.Settings.Manage'` to the `TenantPermissionKey` union in the same file and to the key list in `src/web/src/features/roles/permissionCatalog.ts`. The **server-side** key is added by the backend task `B039`, not here — do not touch any backend file.
9. Gate the profile nav entry in `src/web/src/components/shell/ShellNav.tsx` on `SHOP_SETTINGS_MANAGE_KEY`, the same way the existing Shop nav items are gated on `SHOP_CATALOG_MANAGE_KEY` (delivered by `F043`). The complete file list for this task is: `src/web/src/features/shop/clients/httpShopProfileClient.ts`, `src/web/src/features/roles/roleTypes.ts`, `src/web/src/features/roles/permissionCatalog.ts`, `src/web/src/components/shell/ShellNav.tsx` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx`. Do not edit any other file unless step 1 found a documented contract defect.
10. The admin routes are `GET` and `PUT /api/tenants/{tenantId}/shop/profile` and use `shopFetch` (bearer token). The public route is `GET /api/shop/{tenantId}/profile` and uses `shopFetchPublic` (no token).
11. Make sure every path segment (tenant ID, etc.) is passed through `encodeURIComponent`, and any query string is built with `URLSearchParams`.
12. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch` call, unchanged, so callers can cancel in-flight requests.
13. Parse every successful JSON response through the Zod schema (a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) already created in task `F047`. Do not add new fields or rename any existing schema field.
14. Normalize failures (other than the specific null/404 mappings in steps 4-6) into the shared `ShopClientError` type from `src/web/src/features/shop/contracts/shopContract.ts` (created by `F044`), using the same normalization helper used elsewhere in the Shop clients.
15. Switch only the `profile` slot. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` is defined. Find the `profile` slot. Change it from the mock profile client to the new HTTP profile client. Do not touch any other slot.
16. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
17. Confirm the expected component/page diff is zero beyond the permission constants and nav requirement named in step 9. If you find yourself editing any other component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
18. Run the regression and browser proof steps in the "Regression and browser proof" section below.
19. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopProfileClient.ts`, `src/web/src/features/roles/roleTypes.ts`, `src/web/src/features/roles/permissionCatalog.ts`, `src/web/src/components/shell/ShellNav.tsx` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind admin GET/PUT and public GET. Admin null stays null; 409 preserves form; public 404 maps to null neutral fallback. Add `Shop.Settings.Manage` runtime/compile-time permission key. Switch only `profile`.

```ts
const response = shopProfileResponseSchema.parse(await shopFetch(tenantShopPath(tenantId, 'profile'), { signal })); return response.profile;
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `profile` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F047` against the real backend. Concretely: for each state F047 demonstrated (no profile yet, populated profile, edit form, save conflict, public storefront view, public 404, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: edit and save the profile, reload the page, and confirm the change survived; then trigger the 409 conflict path and confirm the form's values are preserved, not cleared; also confirm a missing `Shop.Settings.Manage` permission blocks admin access as expected.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `shopProfileResponseSchema` parses both the old mock data and the new real backend data without modification.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted.
   The component/page diff is expected to be zero — if it is not, say exactly
   which component you had to touch and which contract mismatch forced it.
2. **Implementation decisions.** Every decision this Spec left to you, with the
   option you picked and one sentence of why.
3. **Contract comparison.** The result of step 1's check: whether the delivered
   backend C# records, routes and status codes matched
   `docs/design/shop/http-contracts.md` and this Spec. List every difference
   you found, even ones you decided were harmless. If you found none, say so
   explicitly.
4. **Commands executed.** `cd src/web && npm run build` and
   `cd src/web && npm run lint`, copied verbatim, in the order you ran them.
   State explicitly that you did not run `npm test` or `npm run test:e2e`.
5. **Results of those checks.** For each command: pass or fail, plus the error
   text if it failed. Then: which of the paired mock task's scenarios you
   replayed against the real backend, which slot is now real, whether reload
   and tenant-switch persisted, which failure path you triggered and how it
   surfaced, and whether the browser console stayed clean at all three
   viewports. Never report a check as passing if you did not run it.
6. **Risks, blockers and follow-up.** Anything you could not verify, any
   acceptance item you could not check off and why, and anything the next
   connection task needs to know. Write "None." if there is genuinely nothing.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `profile` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
