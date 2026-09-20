# F062 — Bind payment lifecycle to B044/B045 HTTP contracts

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F062` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F052**.
- Slice: `S41`; depends on `F061, B044, B045`.
- Backend source of truth: `B044, B045` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

## Files expected to change

`clients/httpShopPaymentsClient.ts`, environment redirect allowlist and composition only.

The expected component/page diff is zero. If the delivered backend contract differs from the planned TypeScript schema, stop before editing, show the exact mismatch, and request a contract correction decision. Do not silently reshape both sides or weaken Zod parsing.

## Required implementation

Bind idempotent initiation, backend callback result/status token and Development sandbox route. Treat redirect URL as opaque after scheme/host allowlist; append no order/merchant/amount. Poll max five times. Switch only `payments`.

```ts
const initiation = paymentInitiationSchema.parse(await shopFetch(initiatePath(tenantId, orderId), { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, signal })); assertAllowedPaymentRedirect(initiation.redirectUrl); return initiation;
```

Use the existing authenticated `shopFetch`/token behavior for admin routes and plain anonymous requests for storefront routes. Encode every path segment and query. Pass AbortSignal. Parse every successful JSON response through the schema created in `F052`; normalize failures once into `ShopClientError`.

## Composition switch

Change only the `payments` slot in `createShopClients()` from its mock implementation to the HTTP implementation. All later capability slots remain mocked until their own connection task. Never use an environment condition to fall back silently from a failed real API to mock data.

## Regression and browser proof

- Replay every screenshot/state acceptance scenario from `F052` against the real backend.
- Prove persistence with reload and one relevant backend failure/permission/conflict path.
- Compare mock and real JSON through the same schema; component props/view models remain unchanged.
- Run `npm run build`, `npm run lint`, desktop/tablet/mobile browser checks and console inspection. Do not run or edit frontend tests.

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `payments` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
