# F063 — Bind shared 429 handling to B046 rate-limit contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F063` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F053.**
- Slice: `S42`; depends on `F062, B046`.
- Backend source of truth: `B046` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before anything else: read `AGENTS.md` (branch naming, ledger update rules and the ownership boundaries are defined there — this checklist does not repeat them), read the `S42` slice file, read the linked `B046` backend task and its delivered C# records/tests, and read the matching `docs/design/shop/http-contracts.md` section. Only then start on the steps below.

1. Open the shared `shopFetch` problem-parser file (the shared code that turns a failed HTTP response into a `ShopClientError`, used by every Shop client). This file, plus client-composition cleanup, are the only things you touch in this task, aside from deleting the developer mock-state switch described in step 5.
2. Confirm the request/response TypeScript schema still matches the actual delivered `B046` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator already used elsewhere in this codebase) to make a mismatch silently pass.
3. Implement the failure-parsing branch exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/type names already defined in this file):
   ```ts
   if (!response.ok) {
     const raw = await response.json().catch(() => ({}));
     const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10);
     throw new ShopClientError(shopProblemSchema.parse({
       ...raw,
       status: response.status,
       retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : raw.retryAfterSeconds,
     }));
   }
   ```
   Walk through exactly what this does: `response.json().catch(() => ({}))` reads the JSON error body, or falls back to an empty object if the body can't be parsed as JSON. `RFC7807` (the repo's standard JSON error body shape, sometimes called a "Problem Details" object — reuse this existing shape and the existing `shopProblemSchema`, do not invent a new error format) is what `raw` is expected to contain. `Retry-After` is a standard HTTP response header telling the caller how many seconds to wait before retrying; `Number.parseInt(...)` converts it from a header string to an integer. If the header is missing or not a valid number, fall back to whatever `retryAfterSeconds` value is already present in the RFC7807 body (`raw.retryAfterSeconds`) instead.
4. Make sure the resulting `ShopClientError` is the exact same, already-rendered error type the rest of the UI already knows how to display — do not create a second, parallel error type for rate-limit (`429`) responses.
5. Delete the developer mock-state switch: find the existing developer-only toggle that lets a developer flip individual `ShopClients` slots between mock and real HTTP, and remove it entirely, along with any UI or code path that references it.
6. Open the file that defines `createShopClients()` (the composition function that wires each capability to either its mock or real HTTP implementation). Change **only** the `problems` slot (the shared failure/error-parsing logic) so it points at this real HTTP-based implementation instead of its mock.
7. After the previous step, go through every single slot in `createShopClients()` (coupons, orders, orderOperations, payments, problems, and any others) and verify each one is now wired to its real HTTP implementation in the production composition — none should still point at a mock implementation. This task is the last "connect" task in this sequence, so this is the point where the whole `ShopClients` object should be fully real.
8. Clean up the client composition code: remove any now-unused mock-client imports, unused mock-selection branches, or dead code left over from the mock-to-real migration across `F059`–`F063`, but do not touch component or page files — the expected component/page diff for this task is zero.
9. For the failure-parsing logic:
   - Parse every successful JSON response through the Zod schema already created in `F053`.
   - Normalize every failure into `ShopClientError` exactly once, using the branch from step 3.
   - Admin routes use the existing authenticated `shopFetch`/token behavior. Storefront (customer-facing, non-admin) routes are plain anonymous requests — do not attach an auth token to them.
   - Pass the `AbortSignal` (`signal`) through on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - URL-encode every path segment and every query parameter in any code you touch.
10. Do not add any environment-based fallback that silently uses mock data if a real API call fails. If a real call fails (including with `429`), the error must propagate as a normal `ShopClientError`.
11. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

shared `shopFetch` problem parser and client composition cleanup; delete developer mock-state switch.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Parse RFC7807 and integer Retry-After from header/body into the already-rendered `ShopClientError`. Remove final mock client bindings and developer mock switch. Verify every `ShopClients` slot is HTTP in production composition.

```ts
if (!response.ok) { const raw = await response.json().catch(() => ({})); const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10); throw new ShopClientError(shopProblemSchema.parse({ ...raw, status: response.status, retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : raw.retryAfterSeconds })); }
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F053`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `problems` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F053` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F053`, now against the real backend, and confirm each one still matches.
- [ ] Reload the page after triggering a `429` and confirm the retry-after messaging still reflects the real server value.
- [ ] Trigger one real backend failure, permission, or conflict path (in addition to a real `429`) and confirm the UI shows it correctly.
- [ ] Fetch the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `npm run build` and confirm it succeeds.
- [ ] Run `npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `problems` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
