# F053 — Build contract-shaped Shop rate-limit mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F053` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S42`; depends on `F052`.
- Planned backend contract: `B046`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews consistent 429 cooldown behavior across order lookup, cart, checkout, order and payment actions before policies are enabled.

## Files expected to change

`contracts/shopProblemContract.ts`, shared Shop client error parser/mock throttle, OrderTrackingPage, cart/checkout/order/payment actions.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B046` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const shopProblemSchema = z.object({ status: z.number().int(), type: z.string().optional(), title: z.string().optional(), detail: z.string().optional(), errors: z.record(z.string(), z.array(z.string())).optional(), retryAfterSeconds: z.number().int().positive().optional() })
export const rateLimitedProblemSchema = shopProblemSchema.extend({ status: z.literal(429), type: z.literal('shop_rate_limit'), retryAfterSeconds: z.number().int().positive() })
export class ShopClientError extends Error { constructor(readonly problem: z.infer<typeof shopProblemSchema>) { super(problem.detail ?? problem.title ?? 'Shop request failed') } }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Finish the mock phase with one shared problem parser/error type used by all clients. Mock 429 per sensitive action. Disable only the triggering action for Retry-After seconds, announce countdown, preserve data and never auto-submit. Add a developer-only mock-state switch outside production to make every reviewed state reproducible.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

429 on each action; valid/invalid lookup shares presentation; countdown/unmount; preserved forms/cart; final full-store walkthrough with every client still mocked.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock problems client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B046` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
