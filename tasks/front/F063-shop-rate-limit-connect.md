# F063 — Bind shared 429 handling to B046 rate-limit contract

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F063` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **HTTP binding only — UI was accepted in F053**.
- Slice: `S42`; depends on `F062, B046`.
- Backend source of truth: `B046` plus its delivered C# request/response records and integration tests.
- Persistent contract: verify the matching `docs/design/shop/http-contracts.md` section against delivered C# records and integration tests before editing.
- Visible outcome: the already-delivered mock UI behaves identically with real persisted/server data.

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

## Definition of done

- [ ] Real adapter implements the existing client interface without `any` or component-owned HTTP.
- [ ] `problems` is the only newly real slot; no silent mock fallback remains.
- [ ] Delivered UI did not require redesign; any unavoidable material contract correction is documented and explicitly resolved before delivery.
- [ ] Reload, tenant switch, abort, failure and responsive browser evidence pass.
- [ ] Stop after this task; do not connect the next capability.
