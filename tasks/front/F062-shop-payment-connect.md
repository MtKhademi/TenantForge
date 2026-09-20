# F062 — Bind payment lifecycle to B044/B045 HTTP contracts

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F062` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F052.**
- Slice: `S41`; depends on `F061, B044, B045`.
- Backend source of truth: `B044, B045` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Do this in order

Before anything else: read `AGENTS.md` (branch naming, ledger update rules and the ownership boundaries are defined there — this checklist does not repeat them), read the `S41` slice file, read the linked `B044` and `B045` backend tasks and their delivered C# records/tests, and read the matching `docs/design/shop/http-contracts.md` section. Only then start on the steps below.

1. Create `src/web/src/features/shop/clients/httpShopPaymentsClient.ts` and `src/web/src/features/shop/clients/paymentRedirectAllowlist.ts` (the scheme/host allowlist from step 4). Those two files plus `src/web/src/features/shop/clients/ShopClientsProvider.tsx` are the only things you touch in this task.
2. Confirm the request/response TypeScript schema in this file still matches the actual delivered `B044`/`B045` C# records and integration tests. If it does not match, **stop before editing anything else**, write down the exact mismatch, and request a contract-correction decision. Do not edit both sides to force them to agree, and do not weaken (loosen) the Zod schema (Zod schema = a runtime validator plus TypeScript type generator, created by the paired mock task under `src/web/src/features/shop/contracts/`) to make a mismatch silently pass.
3. Implement **initiation** (starting a payment) exactly like this (this is the full, complete implementation — copy it, adjusting only the schema/path names already defined in this file):
   ```ts
   const initiation = paymentInitiationSchema.parse(await shopFetch(initiatePath(tenantId, orderId), { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, signal }));
   assertAllowedPaymentRedirect(initiation.redirectUrl);
   return initiation;
   ```
   `shopFetch` is the authenticated fetch helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` — reuse it, do not write a new fetch wrapper. (Its anonymous twin for public storefront routes is `shopFetchPublic`.) `Idempotency-Key` (a header value that lets the server recognize a retried request as "the same request", so repeating it has no extra effect — this is what "idempotent" means) must be sent on every initiation call, so a retried initiation never starts a second, duplicate payment.
4. Create `assertAllowedPaymentRedirect(url: string): void` in `src/web/src/features/shop/clients/paymentRedirectAllowlist.ts`. It parses the URL, throws if the scheme is not `https:` or the host is not in the configured allowlist, and returns nothing on success. It does not exist yet. Call it on every redirect URL you receive, before using that URL anywhere. Read the allowed hosts from a Vite env var (`import.meta.env.VITE_SHOP_PAYMENT_REDIRECT_HOSTS`, a comma-separated list); if it is unset, allow no host at all and throw — fail closed, never fail open.
5. Treat the redirect URL as **opaque** after the scheme/host allowlist check passes: do not parse it further, do not read query parameters out of it, and — critically — **never append** order ID, merchant ID, or amount onto it yourself. Use exactly the URL string the backend gave you.
6. Implement the **callback result/status** handling: after the backend redirects the customer back, read the backend's callback result and status token exactly as the contract defines them, and parse them through the existing schema from `F052`. Do not derive payment status from the redirect URL's own query string — only from the backend's callback response.
7. Implement the **Development sandbox route**: in the Development environment only, bind to the sandbox payment route defined in the `B044`/`B045` contract (this lets the payment flow be exercised locally without a real payment provider). Do not enable the sandbox route outside the Development environment.
8. Implement **polling** for payment status: poll the status endpoint up to a maximum of **five times**, then stop. Do not poll indefinitely and do not exceed five attempts.
9. For every operation in this file (initiate, callback/status read, sandbox route, poll):
   - URL-encode every path segment and every query parameter.
   - Pass the `AbortSignal` (`signal`) through to `shopFetch` on every call. (`AbortSignal` = the mechanism that lets an in-flight request be cancelled, for example when the component using it unmounts.)
   - Parse every successful JSON response through the Zod schema already created in `F052`.
   - Normalize any failure into `ShopClientError` exactly once — do not invent a second error shape.
   - Admin routes (`/api/tenants/{tenantId}/shop/...`) use `shopFetch`, which attaches the bearer token. Public storefront routes (`/api/shop/{tenantId}/...`) use `shopFetchPublic`, which sends no token. Both are in `src/web/src/features/shop/clients/shopFetch.ts`.
10. Open `src/web/src/features/shop/clients/ShopClientsProvider.tsx`, where `createShopClients()` wires each capability slot to either its mock or its real HTTP implementation. Change **only** the `payments` slot so it points at your new `httpShopPaymentsClient.ts` implementation. Leave every other slot pointing at its mock; those get switched in their own later tasks.
11. Do not add any environment-based fallback that silently uses mock data if the real API call fails. If the real call fails, the error must propagate as a normal `ShopClientError`. (The one and only environment-conditioned behavior allowed in this task is the Development sandbox route from step 7 — that is a documented, explicit route switch, not a silent mock fallback.)
12. Run the regression and browser proof steps below, then check every item in "Definition of done" before stopping.

## Files expected to change

`src/web/src/features/shop/clients/httpShopPaymentsClient.ts`, `src/web/src/features/shop/clients/paymentRedirectAllowlist.ts` and `src/web/src/features/shop/clients/ShopClientsProvider.tsx` only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind idempotent initiation, backend callback result/status token and Development sandbox route. Treat redirect URL as opaque after scheme/host allowlist; append no order/merchant/amount. Poll max five times. Switch only `payments`.

```ts
const initiation = paymentInitiationSchema.parse(await shopFetch(initiatePath(tenantId, orderId), { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, signal })); assertAllowedPaymentRedirect(initiation.redirectUrl); return initiation;
```

Use `shopFetch` (authenticated, sends the bearer token) for every admin route under `/api/tenants/{tenantId}/shop/...`, and `shopFetchPublic` (anonymous, sends no token) for every public storefront route under `/api/shop/{tenantId}/...`. Both live in `src/web/src/features/shop/clients/shopFetch.ts`, created by `F044` — reuse them, never write another `fetch` wrapper and never call `fetch` from a component. Encode every path segment with `encodeURIComponent` and build every query string with `URLSearchParams`. Pass the caller's `AbortSignal` straight through as `{ signal }`; never create an `AbortController` inside a client. Parse every successful JSON response through the Zod schema the paired mock task already created; normalize every failure into the single shared `ShopClientError`.

## Composition switch

Change only the `payments` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F052` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Then do the desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests — `npm test` and `npm run test:e2e` are out of bounds for the UI engineer (see `AGENTS.md`, "Ownership").

### Checklist form of the regression and browser proof (do each one and check it off)

- [ ] Replay every screenshot/state acceptance scenario listed in `F052`, now against the real backend, and confirm each one still matches.
- [ ] Initiate a payment, reload the page, and confirm the payment state persisted on the server.
- [ ] Trigger one real backend failure or permission/conflict path in the payment flow and confirm the UI shows it correctly.
- [ ] Confirm the redirect URL is only ever checked against the allowlist and passed through unmodified — never has order/merchant/amount appended by the client.
- [ ] Confirm polling stops after at most five attempts.
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
- [ ] `payments` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
