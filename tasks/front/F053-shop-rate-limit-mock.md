# F053 — Build contract-shaped Shop rate-limit mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F053` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S42`; depends on `F052`.
- Planned backend contract: `B046`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews consistent 429 cooldown behavior across order lookup, cart, checkout, order and payment actions before policies are enabled.

## Do this in order

Before starting, follow the boilerplate already described in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `B046` backend Spec, read the `docs/design/shop/http-contracts.md` section for rate limiting, and follow the branch-naming and ledger-update rules from `AGENTS.md`. Then do the following steps in order:

1. Read the full `B046` backend Spec end to end. Write down every field name, type, status value, and error code it defines for the shared problem/error body and for rate limiting — you will need every one of them in step 2.
2. Create `contracts/shopProblemContract.ts` under `features/shop/contracts/`. In it, define Zod schemas ("Zod schema" = a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/`) starting from the three definitions given in "Required contract/code shape" below: `shopProblemSchema`, `rateLimitedProblemSchema`, and the `ShopClientError` class. `shopProblemSchema` is the repo's RFC7807 error body shape ("RFC7807" = the repo's standard JSON error body shape for API errors — reuse this existing helper shape, do not invent a new error format).
3. Add a Zod schema (or extend `shopProblemSchema`) for every other nested member or error variant that `B046` names but that is not shown in the snippet below. Do not add any field that is not in `B046`.
4. Export TypeScript types from each schema using `z.infer<typeof schemaName>`.
5. Create or extend the shared Shop client error parser: one function, used by every Shop feature client (order lookup, cart, checkout, order, payment), that takes a failed HTTP response, parses it against `shopProblemSchema`, and throws a `ShopClientError` (defined in "Required contract/code shape" below) wrapping the parsed problem. There must be exactly one such parser reused everywhere — do not write a second, competing parser.
6. Implement the mock throttle: for each sensitive action (order lookup, cart, checkout, order, payment), the mock client must be able to return a 429 response matching `rateLimitedProblemSchema`, including a positive integer `retryAfterSeconds`.
7. When a 429 is returned for an action, disable only that triggering action (not the whole page) for `retryAfterSeconds` seconds. Show a visible countdown while it is disabled. Never auto-submit the action when the countdown ends — the user must click again.
8. While an action is disabled by cooldown, preserve any data the user already entered (e.g. cart contents, checkout form fields, entered order lookup values) — do not clear or reset it.
9. Add a developer-only mock-state switch (a way to force any of the "Required states" below on demand) so a reviewer can reproduce every state. Do not show task IDs (like `F053`) anywhere in production UI text.
10. Gate the mock-state switch and any scenario toolbar behind `import.meta.env.DEV` (a build-time flag that is `true` only in the local dev server, `false` in a production build). Verify a production build does not render this toolbar.
11. Make every mock method deterministic (same inputs always produce the same result) and route every simulated delay through one shared abort-aware ("abort-aware" = it takes an `AbortSignal`, the standard browser signal used to cancel an in-flight request, and rejects immediately if already aborted) latency helper, reused across all Shop mock clients.
12. Update `OrderTrackingPage` and the cart/checkout/order/payment actions so they all go through the shared error parser from step 5 and all render the 429 cooldown behavior from steps 6–8 consistently.
13. Make sure a valid order lookup and an invalid order lookup share the same visual presentation shape (same layout/structure, different content), so no extra information leaks through layout differences.
14. Implement every other state listed under "Required states" below: idle/initial, loading (with no destructive layout shift — the page must not jump/reflow when loading finishes), success, the relevant empty state, the exact validation/409/403/404/410 states that `B046` names (in addition to 429 handled above), unavailable-with-retry, and aborted/superseded request handling (an older in-flight request's result must never overwrite a newer one).
15. Re-read the "Required contract/code shape" section below and confirm every named member compiles with no `any`, no unchecked casts, and no placeholder comments. Remove any duplicated or competing type/error-parser definitions.
16. Run the browser evidence and validation steps in the "Browser evidence and validation" section below, at all three viewports, including one final full-store walkthrough with every client still mocked, and fix anything that fails before moving on.
17. Walk through every line of the "Definition of done" checklist below and check it off only once you have verified it, not assumed it.

## Files expected to change

`contracts/shopProblemContract.ts`, shared Shop client error parser/mock throttle, OrderTrackingPage, cart/checkout/order/payment actions.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B046` exactly. Mock IDs are canonical 13-character TSID strings ("TSID" = a sortable, numeric-looking string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types — derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const shopProblemSchema = z.object({ status: z.number().int(), type: z.string().optional(), title: z.string().optional(), detail: z.string().optional(), errors: z.record(z.string(), z.array(z.string())).optional(), retryAfterSeconds: z.number().int().positive().optional() })
export const rateLimitedProblemSchema = shopProblemSchema.extend({ status: z.literal(429), type: z.literal('shop_rate_limit'), retryAfterSeconds: z.number().int().positive() })
export class ShopClientError extends Error { constructor(readonly problem: z.infer<typeof shopProblemSchema>) { super(problem.detail ?? problem.title ?? 'Shop request failed') } }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

This means, concretely: for every field `B046` mentions that is not already in `shopProblemSchema` or `rateLimitedProblemSchema` above, add it as its own line inside the matching `z.object({...})` (or `.extend({...})`), with the exact Zod type that matches `B046`'s type for that field. Export a TypeScript type from each schema via `z.infer`. Keep `ShopClientError` as the single error type thrown by the shared parser (step 5 above) — do not create a second error class.

## Required UI implementation

Finish the mock phase with one shared problem parser/error type used by all clients. Mock 429 per sensitive action. Disable only the triggering action for Retry-After seconds, announce countdown, preserve data and never auto-submit. Add a developer-only mock-state switch outside production to make every reviewed state reproducible.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 5–11 above for the mechanical breakdown of this section.)

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority (do not show or imply a confirmation the backend has not actually returned).

## Browser evidence and validation

Capture evidence for all of the following. Treat each bullet as its own checklist item:

- [ ] Trigger a 429 on each sensitive action (order lookup, cart, checkout, order, payment) and confirm only that action is disabled for `retryAfterSeconds`, with a visible countdown.
- [ ] Confirm a valid order lookup and an invalid order lookup render with the same presentation shape.
- [ ] Confirm the countdown behaves correctly if the component unmounts mid-countdown (no error, no leaked timer).
- [ ] Confirm entered form/cart data survives a 429 cooldown without being cleared.
- [ ] Do a final full-store walkthrough (browse, cart, checkout, order, payment) with every client still mocked, end to end.
- [ ] All of the above are also demonstrated on mobile (390×844).

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock problems client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B046` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
