# F056 — Bind category hierarchy client to B038 HTTP contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F056` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F046**.
- Slice: `S34`; depends on `F055, B038`.
- Backend source of truth: `B038` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before starting, follow the boilerplate in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `S34` slice file, read the paired backend task `B038` and its delivered contract, and follow the existing branch-naming/ledger-update rules. Do not skip those — this checklist only covers the coding steps.

1. Open `docs/design/shop/http-contracts.md` and find the B038 section. Compare every route, field name, and status code listed there against the delivered C# request/response records and B038's integration tests. If anything differs from what this Spec below describes, stop, write down the exact mismatch, and ask for a contract correction decision before writing any code.
2. Create `src/web/src/features/shop/clients/httpShopCategoryClient.ts`. This is the only client file you should need to touch for this task.
3. Bind the existing admin create route, admin update route, admin list route, and the public list route to real HTTP calls via `shopFetch`, using the extended parent/children fields that B038 adds to the category shape. Do not invent new routes; use exactly the routes already planned for these four operations.
4. In the code that builds the request body for create/update (the "save category" form submit handler), construct an object typed `SaveCategoryRequest` with exactly these fields: `name` set to `values.name.trim()`, `slug` set to `values.slug.trim().toLowerCase()`, `displayOrder` set to `values.displayOrder`, `isActive` set to `values.isActive`, and `parentCategoryId` set to `values.parentCategoryId`. Reference implementation (copy/adapt this exactly):

```ts
const body: SaveCategoryRequest = {
  name: values.name.trim(),
  slug: values.slug.trim().toLowerCase(),
  displayOrder: values.displayOrder,
  isActive: values.isActive,
  parentCategoryId: values.parentCategoryId,
}
```

5. Parse every response (create, update, admin list, public list) through the matching Zod schema (a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) already created in task `F046`. Do not add new fields or rename any existing schema field.
6. Map parent-category validation errors and 409 Conflict responses (for example: a category referencing an invalid or circular parent, or a stale version conflict) into the shared `ShopClientError` type from `src/web/src/features/shop/contracts/shopContract.ts` (created by `F044`), using the same normalization helper used elsewhere in the Shop clients. Do not reshape this data into a new UI-facing shape — pass through the same error information the component already expects.
7. The admin create/update/list routes are `POST /api/tenants/{tenantId}/shop/categories`, `PUT /api/tenants/{tenantId}/shop/categories/{categoryId}` and `GET /api/tenants/{tenantId}/shop/categories` — all three use `shopFetch` (bearer token). The public list route is `GET /api/shop/{tenantId}/categories` and uses `shopFetchPublic` (no token).
8. Make sure every path segment (tenant ID, category ID, etc.) is passed through `encodeURIComponent`, and any query string is built with `URLSearchParams`.
9. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch` call, unchanged, so callers can cancel in-flight requests.
10. Switch only the `categories` slot. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` is defined. Find the `categories` slot. Change it from the mock category client to the new HTTP category client. Do not touch any other slot.
11. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
12. Confirm the expected diff is limited to `clients/httpShopCategoryClient.ts` and the Shop client composition file. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
13. Run the regression and browser proof steps in the "Regression and browser proof" section below.
14. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopCategoryClient.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind existing admin create/update/list and public list routes using extended parent/children fields. Parse all responses and map parent validation/409 without reshaping UI data. Switch only `categories`.

```ts
const body: SaveCategoryRequest = { name: values.name.trim(), slug: values.slug.trim().toLowerCase(), displayOrder: values.displayOrder, isActive: values.isActive, parentCategoryId: values.parentCategoryId };
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `categories` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F046` against the real backend. Concretely: for each state F046 demonstrated (flat list, nested/parent-child tree, create form, edit form, validation error, conflict error, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: create or edit a category with a parent assignment, reload the page, and confirm the change and hierarchy survived; then trigger one 409/validation failure case and confirm it surfaces as a normal error via `ShopClientError`, not a crash or silent fallback.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm the same Zod schema(s) parse both the old mock data and the new real backend data without modification.
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
- [ ] `categories` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
