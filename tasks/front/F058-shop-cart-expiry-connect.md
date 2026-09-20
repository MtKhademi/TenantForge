# F058 — Bind cart lease UI to B040 contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F058` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F048**.
- Slice: `S36`; depends on `F057, B040`.
- Backend source of truth: `B040` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before starting, follow the boilerplate in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `S36` slice file, read the paired backend task `B040` and its delivered contract, and follow the existing branch-naming/ledger-update rules. Do not skip those — this checklist only covers the coding steps.

1. Open `docs/design/shop/http-contracts.md` and find the B040 section. Compare every route, field name, and status code listed there against the delivered C# request/response records and B040's integration tests. If anything differs from what this Spec below describes, stop, write down the exact mismatch, and ask for a contract correction decision before writing any code.
2. Create `src/web/src/features/shop/clients/httpShopCartLeaseClient.ts`. Move the request logic out of the existing `src/web/src/features/shop/cartAdapter.ts` into it — that adapter already talks to the real cart routes (`/api/shop/{tenantId}/carts...`), so you are porting working code behind the `ShopCartLeaseClient` interface, not writing new request logic from scratch. Leave `cartAdapter.ts` in place if anything else still imports it; delete it only once nothing does.
3. Make `httpShopCartLeaseClient` implement `ShopCartLeaseClient` fully — the same interface `mockShopCartLeaseClient` from `F048` implements, method for method.
4. In every method that creates, reads, or mutates the cart (create, read, and each mutation), parse the `expiresAtUtc` field from the server response using the existing cart Zod schema (a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`), `cartResponseSchema`. Never compute or synthesize `expiresAtUtc` from `Date.now()` on the client — it must always come from the server response.
5. Reference implementation for a read call (copy/adapt this exactly):

```ts
const cart = cartResponseSchema.parse(await shopFetch(cartPath(tenantId, cartId), { signal }))
// Never synthesize expiresAtUtc from Date.now().
```

6. In the shared problem parser (the code that turns a failed HTTP response into a client-facing error), add a specific mapping: when the response status is exactly 410 Gone AND the parsed error code is exactly `shop_cart_expired`, map it to the existing reviewed cart-expiry recovery flow (the UI path already built for handling an expired cart, from F048).
7. Do not treat an ordinary 404 Not Found as cart expiry. Do not treat a network error (fetch throwing, no response at all) as cart expiry. Only the exact combination of 410 status + `shop_cart_expired` error code triggers the expiry recovery flow; every other failure goes through the normal `ShopClientError` normalization path.
8. Do not change any UI timer logic. The existing on-screen countdown/expiry timers must keep working exactly as before — this task only changes where `expiresAtUtc` comes from (server truth instead of mock data), not how the timer displays or counts down.
9. Cart routes (`/api/shop/{tenantId}/carts...`) are public storefront routes, so use `shopFetchPublic` (no token) for every one of them. Do not attach a bearer token to a cart request.
10. Make sure every path segment (tenant ID, cart ID, etc.) is passed through `encodeURIComponent`, and any query string is built with `URLSearchParams`.
11. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch` call, unchanged, so callers can cancel in-flight requests.
12. Parse every successful JSON response through the Zod schema already created in task `F048`. Do not add new fields or rename any existing schema field.
13. Switch only the `cartLease` slot. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` is defined. Find the `cartLease` slot. Change it from the mock cart lease client to the upgraded HTTP cart lease adapter. Do not touch any other slot.
14. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
15. Confirm the expected component/page diff is zero. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
16. Run the regression and browser proof steps in the "Regression and browser proof" section below.
17. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopCartLeaseClient.ts` (new), `src/web/src/features/shop/cartAdapter.ts` (logic moved out), `src/web/src/features/shop/clients/shopFetch.ts` (the 410 mapping) and `src/web/src/features/shop/clients/ShopClientsProvider.tsx`.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Parse `expiresAtUtc` on create/read/mutations. Map only 410 + `shop_cart_expired` to the reviewed recovery; do not treat ordinary 404/network as expiry. Switch only `cartLease`; UI timers remain unchanged.

```ts
const cart = cartResponseSchema.parse(await shopFetch(cartPath(tenantId, cartId), { signal })); // Never synthesize expiresAtUtc from Date.now().
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `cartLease` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F048` against the real backend. Concretely: for each state F048 demonstrated (active cart, near-expiry countdown, expired-cart recovery, mutation success, mutation failure, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: create/mutate a cart, reload the page, and confirm the cart and its real `expiresAtUtc` survived; then trigger the 410 `shop_cart_expired` case and confirm the reviewed recovery flow runs, and separately confirm an ordinary 404 or network error does NOT trigger that recovery flow.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `cartResponseSchema` parses both the old mock data and the new real backend data without modification, and that timer components receive `expiresAtUtc` in the same shape as before.
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
- [ ] `cartLease` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
