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

1. Open `src/web/src/features/shop/clients/shopFetch.ts` and find the `failed(response)` function `F044` created — the shared code that turns a failed HTTP response into a `ShopClientError`, used by every Shop client. That file, plus `ShopClientsProvider.tsx` cleanup and deleting the developer mock-state switch from step 5, are the only things you touch in this task.

   **There is no `problems` slot in `ShopClients` and you must not add one.** Error parsing is a shared function, not a capability slot. An earlier draft of this Spec described it as a slot; that was wrong.
2. Confirm the request/response TypeScript schema still matches the actual delivered `B046` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator, created by the paired mock task under `src/web/src/features/shop/contracts/`) to make a mismatch silently pass.
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
6. There is no composition change for error parsing. `failed()` in `shopFetch.ts` is already the single path every HTTP client's failures go through, so fixing it in step 3 fixes every client at once.
7. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx` and verify every slot in `createShopClients()` is wired to its real HTTP implementation. The complete list of slots, and the task that made each one real, is:

   | Slot | Real implementation | Made real by |
   |---|---|---|
   | `media` | `httpShopMediaClient` | `F054` |
   | `discovery` | `httpShopDiscoveryClient` | `F055` |
   | `categories` | `httpShopCategoryClient` | `F056` |
   | `profile` | `httpShopProfileClient` | `F057` |
   | `cartLease` | `httpShopCartLeaseClient` | `F058` |
   | `coupons` | `httpShopCouponClient` | `F059` |
   | `orders` | `httpShopOrdersClient` | `F060` |
   | `orderOperations` | `httpShopOrderOperationsClient` | `F061` |
   | `payments` | `httpShopPaymentsClient` | `F062` |

   All nine must be real. None may still point at a mock. This is the last "connect" task, so this is the point where the whole `ShopClients` object becomes fully real.
8. Delete every `mockShop*Client.ts` file under `src/web/src/features/shop/clients/`, every import of one, and any mock-selection branch left over from the mock-to-real migration across `F054`–`F063`. After this task no mock Shop client exists in the app at all. Do not touch component or page files — the expected component/page diff for this task is zero.
9. For the failure-parsing logic:
   - Keep parsing every successful JSON response through the Zod schema its own capability's mock task created (`F044`–`F052`); this task adds no new success schema.
   - Normalize every failure into `ShopClientError` exactly once, using the branch from step 3.
   - Admin routes (`/api/tenants/{tenantId}/shop/...`) use `shopFetch`, which attaches the bearer token. Public storefront routes (`/api/shop/{tenantId}/...`) use `shopFetchPublic`, which sends no token. Both are in `src/web/src/features/shop/clients/shopFetch.ts`.
   - Pass the `AbortSignal` (`signal`) through on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - URL-encode every path segment and every query parameter in any code you touch.
10. Do not add any environment-based fallback that silently uses mock data if a real API call fails. If a real call fails (including with `429`), the error must propagate as a normal `ShopClientError`.
11. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`src/web/src/features/shop/clients/shopFetch.ts` (the shared `failed()` parser) and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (composition cleanup); delete the developer mock-state switch and every `mockShop*Client.ts` file.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Parse RFC7807 and the integer `Retry-After` from header/body into the already-rendered `ShopClientError`. Remove every mock client binding and the developer mock switch. Verify all nine `ShopClients` slots are HTTP in the production composition (see the table in "Do this in order" step 7).

```ts
if (!response.ok) { const raw = await response.json().catch(() => ({})); const retryAfter = Number.parseInt(response.headers.get('Retry-After') ?? '', 10); throw new ShopClientError(shopProblemSchema.parse({ ...raw, status: response.status, retryAfterSeconds: Number.isFinite(retryAfter) ? retryAfter : raw.retryAfterSeconds })); }
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

This task switches no individual slot — `F054`–`F062` already made all nine real. What it does instead is verify all nine are real, delete the mock clients and the developer mock switch, and fix the one shared `failed()` parser so every client reports `429` identically. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F053` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F053`, now against the real backend, and confirm each one still matches.
- [ ] Reload the page after triggering a `429` and confirm the retry-after messaging still reflects the real server value.
- [ ] Trigger one real backend failure, permission, or conflict path (in addition to a real `429`) and confirm the UI shows it correctly.
- [ ] Fetch the same data from mock and from the real backend, parse both through the same schema, and confirm component props/view models are identical either way.
- [ ] Run `cd src/web && npm run build` and confirm it succeeds.
- [ ] Run `cd src/web && npm run lint` and confirm it succeeds.
- [ ] Check the UI at desktop, tablet, and mobile viewport sizes in a real browser.
- [ ] Inspect the browser console and confirm no new errors were introduced.
- [ ] Do not run or edit frontend tests as part of this task.
- [ ] Report which data source (mock vs. real) each browser-evidence screenshot uses, using the existing "Data source: mock ..." reporting line convention.

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
- [ ] All nine `ShopClients` slots are real; no `mockShop*Client.ts` file remains under `src/web/src/features/shop/clients/`; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] This is the final Shop connection task. Nothing further in the `F044`–`F063` sequence remains; stop here rather than starting unrelated work.
