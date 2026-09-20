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
2. Open `src/web/src/features/shop/contracts/shopContract.ts` — `F044` already created it with `shopProblemSchema` and the `ShopClientError` class. Do **not** create a second `shopProblemContract.ts`; extend the existing file. In it, define the Zod schemas ("Zod schema" = a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) so the file matches "Required contract/code shape" below. `shopProblemSchema` and `ShopClientError` already exist there from `F044` and must stay byte-identical; the only thing this task adds is `rateLimitedProblemSchema`. ("RFC7807" = the standard JSON error body shape ASP.NET Core returns for API errors — reuse it, do not invent a new error format.)
3. Add a Zod schema (or extend `shopProblemSchema`) for every other nested member or error variant that `B046` names but that is not shown in the snippet below. Do not add any field that is not in `B046`.
4. Export TypeScript types from each schema using `z.infer<typeof schemaName>`.
5. Extend the shared error parser `F044` created: the `failed(response)` function in `src/web/src/features/shop/clients/shopFetch.ts`. It already parses a failed response against `shopProblemSchema` and throws `ShopClientError`. Make it also read the `Retry-After` response header into `retryAfterSeconds`. There must be exactly one such parser, used by every Shop feature client (order lookup, cart, checkout, order, payment) — do not write a second, competing parser.
6. Implement the mock throttle: for each sensitive action (order lookup, cart, checkout, order, payment), the mock client must be able to return a 429 response matching `rateLimitedProblemSchema`, including a positive integer `retryAfterSeconds`.
7. When a 429 is returned for an action, disable only that triggering action (not the whole page) for `retryAfterSeconds` seconds. Show a visible countdown while it is disabled. Never auto-submit the action when the countdown ends — the user must click again.
8. While an action is disabled by cooldown, preserve any data the user already entered (e.g. cart contents, checkout form fields, entered order lookup values) — do not clear or reset it.
9. Add a developer-only mock-state switch (a way to force any of the "Required states" below on demand) so a reviewer can reproduce every state. Do not show task IDs (like `F053`) anywhere in production UI text.
10. Gate the mock-state switch and any scenario toolbar behind `import.meta.env.DEV` (a build-time flag that is `true` only in the local dev server, `false` in a production build). Verify a production build (`cd src/web && npm run build`) does not render this toolbar.
11. Make every mock method deterministic (same inputs always produce the same result) and route every simulated delay through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` ("abort-aware" = it takes an `AbortSignal`, the standard browser signal used to cancel an in-flight request, and rejects immediately if already aborted).
12. Update `src/web/src/pages/shop/storefront/OrderTrackingPage.tsx`, `CartPage.tsx`, `CheckoutPage.tsx`, `OrderReviewPage.tsx` and `PaymentRedirectPage.tsx` so they all go through the shared parser from step 5 and all render the 429 cooldown behaviour from steps 6–8 consistently.
13. Make sure a valid order lookup and an invalid order lookup share the same visual presentation shape (same layout/structure, different content), so no extra information leaks through layout differences.
14. Implement every other state listed under "Required states" below: idle/initial, loading (with no destructive layout shift — the page must not jump/reflow when loading finishes), success, the relevant empty state, the exact validation/409/403/404/410 states that `B046` names (in addition to 429 handled above), unavailable-with-retry, and aborted/superseded request handling (an older in-flight request's result must never overwrite a newer one).
15. Re-read the "Required contract/code shape" section below and confirm every named member compiles with no `any`, no unchecked casts, and no placeholder comments. Remove any duplicated or competing type/error-parser definitions.
16. Run the browser evidence and validation steps in the "Browser evidence and validation" section below, at all three viewports, including one final full-store walkthrough with every client still mocked, and fix anything that fails before moving on.
17. Walk through every line of the "Definition of done" checklist below and check it off only once you have verified it, not assumed it.

## Files expected to change

Edited by this task (this task creates no new contract file):

- `src/web/src/features/shop/contracts/shopContract.ts` (add `rateLimitedProblemSchema`)
- `src/web/src/features/shop/clients/shopFetch.ts` (read `Retry-After` in the shared parser)
- every `mockShop*Client.ts` under `src/web/src/features/shop/clients/` (add the 429 scenario)
- `src/web/src/pages/shop/storefront/OrderTrackingPage.tsx`, `CartPage.tsx`, `CheckoutPage.tsx`, `OrderReviewPage.tsx`, `PaymentRedirectPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

JSON member casing and nullability mirror `B046` exactly. Mock IDs are canonical 13-character TSID strings ("TSID" = a sortable, numeric-looking string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types — derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

// ALREADY IN THE FILE from F044 — leave both of these exactly as they are:
export const shopProblemSchema = z.object({ status: z.number().int(), type: z.string().optional(), title: z.string().optional(), detail: z.string().optional(), errors: z.record(z.string(), z.array(z.string())).optional(), retryAfterSeconds: z.number().int().positive().optional() })
export class ShopClientError extends Error {
  constructor(readonly problem: z.infer<typeof shopProblemSchema>) {
    super(problem.detail ?? problem.title ?? 'Shop request failed')
    this.name = 'ShopClientError'
  }
}

// THE ONLY NEW DEFINITION THIS TASK ADDS:
export const rateLimitedProblemSchema = shopProblemSchema.extend({ status: z.literal(429), type: z.literal('shop_rate_limit'), retryAfterSeconds: z.number().int().positive() })
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

This means, concretely: for every field `B046` mentions that is not already in `shopProblemSchema` or `rateLimitedProblemSchema` above, add it as its own line inside the matching `z.object({...})` (or `.extend({...})`), with the exact Zod type that matches `B046`'s type for that field. Export a TypeScript type from each schema via `z.infer`. Keep `ShopClientError` as the single error type thrown by the shared parser (step 5 above) — do not create a second error class.

## Required UI implementation

Finish the mock phase with one shared problem parser/error type used by all clients. Mock 429 per sensitive action. Disable only the triggering action for Retry-After seconds, announce countdown, preserve data and never auto-submit. Add a developer-only mock-state switch outside production to make every reviewed state reproducible.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 5–11 above for the mechanical breakdown of this section.)

## Required states

Build every one of these. "State" means something the user can actually see on
screen, not a code path.

- **idle / initial** — before anything is requested.
- **loading** — visible progress, with no destructive layout shift (the page
  must not jump or reflow when loading finishes).
- **success** — the normal populated result.
- **empty** — a successful response that contains no items. This is not an
  error; it must not look like one.
- **each error this capability actually defines.** For this task those are:
  `429` with `type` exactly `shop_rate_limit` and a positive integer `retryAfterSeconds` — the focus of this task. Every other code (`400`, `403`, `404`, `409`, `410`) keeps the behaviour the earlier task that introduced it already built; this task only routes them all through the one shared parser. Each needs its own message — do not collapse them into one generic
  "something went wrong". Do **not** invent a state for a status code not listed
  here.
- **unavailable, with retry** — the request could not be made at all (network
  failure). Show a retry control.
- **aborted / superseded** — when a newer request starts, the older one's
  result must never overwrite the newer one's, and an aborted request must not
  surface as an error to the user.
- **honest success feedback** — never show or imply a confirmation the mock did
  not actually return (do not show or imply a confirmation the backend has not actually returned).

## Browser evidence and validation

Capture evidence for all of the following. Treat each bullet as its own checklist item:

- [ ] Trigger a 429 on each sensitive action (order lookup, cart, checkout, order, payment) and confirm only that action is disabled for `retryAfterSeconds`, with a visible countdown.
- [ ] Confirm a valid order lookup and an invalid order lookup render with the same presentation shape.
- [ ] Confirm the countdown behaves correctly if the component unmounts mid-countdown (no error, no leaked timer).
- [ ] Confirm entered form/cart data survives a 429 cooldown without being cleared.
- [ ] Do a final full-store walkthrough (browse, cart, checkout, order, payment) with every client still mocked, end to end.
- [ ] All of the above are also demonstrated on mobile (390×844).

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock problems client; HTTP integration deferred to the matching F054–F063 task.`

## Completion report

When the task is finished, report exactly these six things — no more, no less.
Do not skip a heading because you think it is obvious.

1. **Files changed.** The full list of paths you created, edited or deleted,
   split into "created" and "edited". Compare it against "Files expected to
   change" above and call out every difference, in either direction.
2. **Implementation decisions.** Every decision this Spec left to you, with the
   option you picked and one sentence of why. Name every place you had to add
   a field, schema or type that the Spec referenced but did not spell out.
3. **Commands executed.** `cd src/web && npm run build` and
   `cd src/web && npm run lint`, copied verbatim, in the order you ran them.
   State explicitly that you did not run `npm test` or `npm run test:e2e`
   (the UI engineer does not touch frontend tests — see `AGENTS.md`).
4. **Results of those checks.** For each command: pass or fail, plus the error
   text if it failed and what you changed to fix it. Then the browser evidence:
   which scenarios you exercised at 1440×900, 1024×768 and 390×844, and whether
   the browser console stayed clean. Never report a check as passing if you did
   not run it.
5. **Contract fidelity.** State that every schema field name, type and
   nullability matches the paired backend Spec, and list any field where you
   were unsure. If the paired Spec and `docs/design/shop/http-contracts.md`
   disagreed, say which one you followed and why.
6. **Risks, blockers and follow-up.** Anything you could not verify, any
   acceptance item you could not check off and why, and anything the paired
   connection task needs to know. Finish with the exact `Data source:` line
   this Spec names. Write "None." for the risk list if there is genuinely
   nothing.

## Definition of done

"Write/verify a scenario" below means: add that scenario to the mock client
and exercise it by hand in a real browser, then record what you saw. It does
**not** mean writing an automated test file — the UI engineer does not create,
edit or run frontend tests (see `AGENTS.md`, "Ownership"). Check a box only
after you have actually seen the described behaviour in the browser.

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B046` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify a 429 on order lookup — assert only the lookup action is disabled for `retryAfterSeconds` with a visible countdown, and the rest of the page stays usable.
- [ ] Write/verify a 429 on a cart mutation — assert the same per-action cooldown behaviour.
- [ ] Write/verify a 429 on checkout — assert the same per-action cooldown behaviour.
- [ ] Write/verify a 429 on order creation — assert the same per-action cooldown behaviour.
- [ ] Write/verify a 429 on payment initiation — assert the same per-action cooldown behaviour.
- [ ] Write/verify that no action auto-submits when its countdown reaches zero — assert the user has to click again.
- [ ] Write/verify that entered data (cart contents, checkout form fields, lookup input) survives a cooldown — assert nothing is cleared.
- [ ] Write/verify a successful and an unsuccessful order lookup — assert both render with the same layout/structure so no information leaks through layout differences.
- [ ] Write/verify unmounting a component mid-countdown — assert no console error and no leaked timer.
- [ ] Write/verify every Shop client throws the same single `ShopClientError` type — assert no second error class exists anywhere under `src/web/src/features/shop/`.
- [ ] Write/verify one full store walkthrough (browse, cart, checkout, order, payment) with every client still mocked, end to end.
- [ ] Write/verify every cooldown state above also renders correctly at 390×844.
