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
2. Open (or create) `clients/httpShopProfileClient.ts`. This is the primary file you should need to touch for this task, along with permission constants/nav requirement and composition (see step 9).
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

8. Add a new permission key named exactly `Shop.Settings.Manage`. It must exist both as a runtime permission key (checked at request/render time) and as a compile-time permission key (a typed constant in the existing permissions constants file), following the same pattern already used for other Shop permission keys in that file.
9. Files expected to change for this task are: `clients/httpShopProfileClient.ts`, the permission constants file, the nav requirement that gates access using `Shop.Settings.Manage`, and the Shop client composition file. Do not edit any other file unless step 1 found a documented contract defect.
10. Use the existing authenticated `shopFetch`/token behavior for the admin GET/PUT routes, and plain anonymous requests for the public GET route.
11. Make sure every path segment (tenant ID, etc.) is passed through `encodeURIComponent`, and any query string is built with `URLSearchParams`.
12. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch` call, unchanged, so callers can cancel in-flight requests.
13. Parse every successful JSON response through the Zod schema (a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/`) already created in task `F047`. Do not add new fields or rename any existing schema field.
14. Normalize failures (other than the specific null/404 mappings in steps 4-6) into the existing `ShopClientError` type (the repo's shared error type for normalized HTTP failures), using the same normalization helper used elsewhere in the Shop clients.
15. Switch only the `profile` slot. Open the Shop client composition file (where `createShopClients()` is defined). Find the `profile` slot. Change it from the mock profile client to the new HTTP profile client. Do not touch any other slot.
16. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
17. Confirm the expected component/page diff is zero beyond the permission constants and nav requirement named in step 9. If you find yourself editing any other component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
18. Run the regression and browser proof steps in the "Regression and browser proof" section below.
19. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`clients/httpShopProfileClient.ts`, permission constants/nav requirement and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind admin GET/PUT and public GET. Admin null stays null; 409 preserves form; public 404 maps to null neutral fallback. Add `Shop.Settings.Manage` runtime/compile-time permission key. Switch only `profile`.

```ts
const response = shopProfileResponseSchema.parse(await shopFetch(tenantShopPath(tenantId, 'profile'), { signal })); return response.profile;
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal (the object used to cancel an in-flight `fetch`; the repo's `shopFetch` already accepts it as `{ signal }`). Parse every successful JSON response through the schema created in `F047`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `profile` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F047` against the real backend. Concretely: for each state F047 demonstrated (no profile yet, populated profile, edit form, save conflict, public storefront view, public 404, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: edit and save the profile, reload the page, and confirm the change survived; then trigger the 409 conflict path and confirm the form's values are preserved, not cleared; also confirm a missing `Shop.Settings.Manage` permission blocks admin access as expected.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `shopProfileResponseSchema` parses both the old mock data and the new real backend data without modification.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `profile` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
</content>
</invoke>
