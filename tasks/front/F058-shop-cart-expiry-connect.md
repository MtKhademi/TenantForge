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
2. Find the existing HTTP cart adapter (already exists — you are upgrading it, not creating a new file from scratch). This task's changes go in that existing file, plus the shared problem parser and composition.
3. Upgrade the existing HTTP cart adapter so it fully implements the `ShopCartLeaseClient` interface (the same interface the mock cart lease implementation already implements).
4. In every method that creates, reads, or mutates the cart (create, read, and each mutation), parse the `expiresAtUtc` field from the server response using the existing cart Zod schema (a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/`), `cartResponseSchema`. Never compute or synthesize `expiresAtUtc` from `Date.now()` on the client — it must always come from the server response.
5. Reference implementation for a read call (copy/adapt this exactly):

```ts
const cart = cartResponseSchema.parse(await shopFetch(cartPath(tenantId, cartId), { signal }))
// Never synthesize expiresAtUtc from Date.now().
```

6. In the shared problem parser (the code that turns a failed HTTP response into a client-facing error), add a specific mapping: when the response status is exactly 410 Gone AND the parsed error code is exactly `shop_cart_expired`, map it to the existing reviewed cart-expiry recovery flow (the UI path already built for handling an expired cart, from F048).
7. Do not treat an ordinary 404 Not Found as cart expiry. Do not treat a network error (fetch throwing, no response at all) as cart expiry. Only the exact combination of 410 status + `shop_cart_expired` error code triggers the expiry recovery flow; every other failure goes through the normal `ShopClientError` normalization path.
8. Do not change any UI timer logic. The existing on-screen countdown/expiry timers must keep working exactly as before — this task only changes where `expiresAtUtc` comes from (server truth instead of mock data), not how the timer displays or counts down.
9. Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes (cart routes are storefront/customer-facing, so confirm which behavior the existing adapter already uses and keep it unchanged).
10. Make sure every path segment (tenant ID, cart ID, etc.) is passed through `encodeURIComponent`, and any query string is built with `URLSearchParams`.
11. Pass the `AbortSignal` (`signal`) parameter through to every `shopFetch` call, unchanged, so callers can cancel in-flight requests.
12. Parse every successful JSON response through the Zod schema already created in task `F048`. Do not add new fields or rename any existing schema field.
13. Switch only the `cartLease` slot. Open the Shop client composition file (where `createShopClients()` is defined). Find the `cartLease` slot. Change it from the mock cart lease client to the upgraded HTTP cart lease adapter. Do not touch any other slot.
14. Confirm you have not added an environment condition that silently falls back from a failed real API call to mock data. If a real call fails, it must surface as a normal error, never fall back to mock data.
15. Confirm the expected component/page diff is zero. If you find yourself editing a component or page file, stop — that means the backend contract differs from plan; follow step 1's instructions instead of editing components.
16. Run the regression and browser proof steps in the "Regression and browser proof" section below.
17. Check every box in "Definition of done" below with real evidence before stopping.

## Files expected to change

existing HTTP cart adapter upgraded to implement `ShopCartLeaseClient`, shared problem parser and composition.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Parse `expiresAtUtc` on create/read/mutations. Map only 410 + `shop_cart_expired` to the reviewed recovery; do not treat ordinary 404/network as expiry. Switch only `cartLease`; UI timers remain unchanged.

```ts
const cart = cartResponseSchema.parse(await shopFetch(cartPath(tenantId, cartId), { signal })); // Never synthesize expiresAtUtc from Date.now().
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal (the object used to cancel an in-flight `fetch`; the repo's `shopFetch` already accepts it as `{ signal }`). Parse every successful JSON response through the schema created in `F048`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `cartLease` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F048` against the real backend. Concretely: for each state F048 demonstrated (active cart, near-expiry countdown, expired-cart recovery, mutation success, mutation failure, etc.), open that same screen against the real backend and confirm it still looks and behaves the same.
- Prove persistence with reload and one relevant backend failure/permission/conflict path. Concretely: create/mutate a cart, reload the page, and confirm the cart and its real `expiresAtUtc` survived; then trigger the 410 `shop_cart_expired` case and confirm the reviewed recovery flow runs, and separately confirm an ordinary 404 or network error does NOT trigger that recovery flow.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged. Concretely: confirm `cartResponseSchema` parses both the old mock data and the new real backend data without modification, and that timer components receive `expiresAtUtc` in the same shape as before.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `cartLease` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
</content>
</invoke>
