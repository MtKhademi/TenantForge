# F052 — Build contract-shaped gateway-neutral payment mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F052` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S40`; depends on `F051`.
- Planned backend contract: `B044`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews redirecting, verifying, paid, declined, pending and unavailable payment states without assuming the fake bank is production.

## Do this in order

Before starting, follow the boilerplate already described in "Ownership, phase and dependencies" above: read `AGENTS.md`, read the `B044` backend Spec, read the `docs/design/shop/http-contracts.md` section for payments, and follow the branch-naming and ledger-update rules from `AGENTS.md`. Then do the following steps in order:

1. Read the full `B044` backend Spec end to end. Write down every field name, type, status value, and error code it defines for payment initiation and payment result — you will need every one of them in step 2.
2. Create `src/web/src/features/shop/contracts/paymentLifecycleContract.ts`. In it, define Zod schemas ("Zod schema" = a runtime validator plus TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) for every payload named in `B044`, matching JSON member casing and nullability exactly as `B044` specifies. Start from the two schemas given in "Required contract/code shape" below (`paymentInitiationSchema`, `paymentResultSchema`) and add a Zod schema for every other nested member or payload that `B044` names but that is not shown in that snippet. Do not add any field that is not in `B044`.
3. From each schema, export a matching TypeScript type using `z.infer<typeof schemaName>` (e.g. `export type PaymentInitiation = z.infer<typeof paymentInitiationSchema>`).
4. Create `src/web/src/features/shop/clients/ShopPaymentsClient.ts`. In it, define the `ShopPaymentsClient` interface exactly as shown in "Required contract/code shape" below: `initiate`, `getStatus`, and the optional `resolveSandbox`. Every method takes an `AbortSignal` ("AbortSignal" = the standard browser signal used to cancel an in-flight request; a method that accepts one is "abort-aware") as its last parameter, called `signal`, and is optional to pass.
5. Create `src/web/src/features/shop/clients/mockShopPaymentsClient.ts` implementing `ShopPaymentsClient`. Do not import fixtures into a component and do not call `fetch` anywhere in this task.
6. Make every method on the mock deterministic (same inputs always produce the same result) and route every simulated delay through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts` (it takes a `signal` and rejects immediately if the signal is already aborted, instead of a bare `setTimeout`).
7. Give the mock a way to select named scenarios (for example an in-memory scenario map keyed by order ID or by a query parameter), so a reviewer can force each of the states listed in "Required states" below. Do not show task IDs (like `F052`) anywhere in production UI text.
8. Gate any scenario-selection UI (a "scenario toolbar") behind `import.meta.env.DEV` (a build-time flag that is `true` only in the local dev server, `false` in a production build). Verify a production build (`cd src/web && npm run build`) does not render this toolbar.
9. Add a `payments` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to your new mock payments client. Components get it through `useShopClients()`, never by constructing or importing it directly. Do not touch any other slot.
10. Create `src/web/src/pages/shop/storefront/PaymentRedirectPage.tsx` and add a `payment-redirect` child route for it in `src/web/src/App.tsx`, inside the existing `<Route path="/shop/:tenantId" element={<StorefrontLayout />}>` block beside the current `payment-result` route. It calls `initiate` with `tenantId`, `orderId`, and an idempotency key ("idempotent" = calling it more than once with the same key produces the same result instead of creating duplicates), then navigates the user only after checking that `redirectUrl` is safe.

    Your mock must return both of the shapes the real backend returns, because
    `F062` has to handle both:

    - for `provider: 'Sandbox'`, a **relative same-origin path**
      `/shop/{tenantId}/bank?authority={authority}` (this is what `B044`
      specifies, and it resolves to the existing `bank` route);
    - for `provider: 'ZarinPal'`, an **absolute `https://` URL** on a gateway
      host.

    Add scenarios for an unsafe redirect too: an absolute `http://` URL, an
    absolute URL on an unexpected host, and a protocol-relative `//host/path`
    (which looks like a path but is actually another origin). Each must show an
    error instead of navigating. `F062` replaces your check with a shared
    `assertAllowedPaymentRedirect` helper, so keep the check in one small
    function rather than inline in the component.
11. Update the existing `src/web/src/pages/shop/storefront/PaymentResultPage.tsx` (delivered by an earlier task — do not create a second one). It must resolve its state purely from the opaque `resultToken` in the URL (not from session/draft state or a query-string "outcome" flag), so refreshing the page after a redirect still shows the correct state. It calls `getStatus` with that token.
12. In `PaymentResultPage.tsx`, implement the `Pending` status as bounded fake polling (poll `getStatus` a fixed number of times with a delay between each, then stop) plus a manual "Retry"/"Check again" control the user can click at any time.
13. Update the existing `src/web/src/pages/shop/storefront/SandboxBankPage.tsx` (the fake bank UI used only for the `Sandbox` provider — it already exists, do not create a second one) and mark it explicitly Development-only. Gate its `bank` route in `src/web/src/App.tsx` on `import.meta.env.DEV`, so the route is not registered at all in a production build.
14. Implement every state listed under "Required states" below across these pages: idle/initial, loading (with no destructive layout shift — the page must not jump/reflow when loading finishes), success, the relevant empty state, the exact validation/409/403/404/410/429 states that `B044` names for this capability, unavailable-with-retry, and aborted/superseded request handling (an older in-flight request's result must never overwrite a newer one).
15. Re-read the "Required contract/code shape" section below and confirm every named member compiles with no `any`, no unchecked casts, and no placeholder comments. Remove any duplicated or competing type definitions.
16. Run the browser evidence and validation steps in the "Browser evidence and validation" section below, at all three viewports, and fix anything that fails before moving on.
17. Walk through every line of the "Definition of done" checklist below and check it off only once you have verified it, not assumed it.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/paymentLifecycleContract.ts`
- `src/web/src/features/shop/clients/ShopPaymentsClient.ts` (interface only)
- `src/web/src/features/shop/clients/mockShopPaymentsClient.ts`
- `src/web/src/pages/shop/storefront/PaymentRedirectPage.tsx`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `payments` slot)
- `src/web/src/pages/shop/storefront/PaymentResultPage.tsx` (already exists)
- `src/web/src/pages/shop/storefront/SandboxBankPage.tsx` (already exists)
- `src/web/src/App.tsx` (add the redirect route; gate the `bank` route on `import.meta.env.DEV`)

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

JSON member casing and nullability mirror `B044` exactly. Mock IDs are canonical 13-character TSID strings ("TSID" = a sortable, numeric-looking string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types — derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const paymentInitiationSchema = z.object({ provider: z.enum(['Sandbox','ZarinPal']), redirectUrl: z.string(), resultToken: z.string() })
export const paymentResultSchema = z.object({ orderNumber: z.string(), status: z.enum(['PendingPayment','Paid','Cancelled','Fulfilled']), providerReference: z.string().nullable() })
export interface ShopPaymentsClient { initiate(tenantId: string, orderId: string, idempotencyKey: string, signal?: AbortSignal): Promise<PaymentInitiation>; getStatus(tenantId: string, orderId: string, token: string, signal?: AbortSignal): Promise<PaymentResult>; resolveSandbox?(tenantId: string, orderId: string, authority: string, approved: boolean, signal?: AbortSignal): Promise<PaymentResult> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

This means, concretely: for every field `B044` mentions that is not already in `paymentInitiationSchema` or `paymentResultSchema` above, add it as its own line inside the matching `z.object({...})`, with the exact Zod type that matches `B044`'s type for that field (e.g. a required string is `z.string()`, a nullable string is `z.string().nullable()`, an enum is `z.enum([...])` listing every value `B044` names). Export a `PaymentInitiation` and `PaymentResult` TypeScript type from each schema via `z.infer`.

## Required UI implementation

Extend provider with payments. Mock idempotent initiation and every result state. Result route works from opaque token after refresh, not session draft/query outcome. Validate redirect scheme/host before navigation. Pending uses bounded fake polling plus manual retry. Keep Sandbox Bank explicitly Development-only.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 9–13 above for the mechanical breakdown of this section.)

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
  `400` (a malformed initiation request), `403`, `404` (unknown order, or a `resultToken` that does not match — both look identical) and `409` (the order is no longer `PendingPayment`, so a payment cannot be started). `PendingPayment` is a **status**, not an error — it is the bounded-polling state from step 12. Each needs its own message — do not collapse them into one generic
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

- [ ] All result states (`PendingPayment`, `Paid`, `Cancelled`, `Fulfilled`) render correctly on `PaymentResultPage`.
- [ ] Refreshing `PaymentResultPage` after landing on it still shows the correct state (state comes from the opaque token, not session/draft/query state).
- [ ] A malformed/unsafe redirect URL is rejected before navigation (scheme/host check from step 10 fires and shows an error).
- [ ] Repeated initiate calls with the same idempotency key do not create duplicate payment attempts.
- [ ] Pending state polling stops after its bounded number of attempts, and the manual retry control works.
- [ ] Sandbox Bank page still works after any earlier changes (regression check) and is unreachable in a production build.
- [ ] All of the above are also demonstrated on mobile (390×844).

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock payments client; HTTP integration deferred to the matching F054–F063 task.`

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
- [ ] Contract schemas/types match `B044` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify each result status (`PendingPayment`, `Paid`, `Cancelled`, `Fulfilled`) — assert each renders its own distinct state on `PaymentResultPage`.
- [ ] Write/verify refreshing `PaymentResultPage` — assert the state still resolves from the `resultToken` in the URL, not from session or draft state.
- [ ] Write/verify a `Sandbox` redirect (`/shop/{tenantId}/bank?authority=...`) — assert it is accepted and navigates to the bank page.
- [ ] Write/verify a `ZarinPal` redirect (absolute `https://` URL) — assert it is accepted.
- [ ] Write/verify a redirect URL with a disallowed scheme, an unexpected host, or a protocol-relative `//host/path` — assert each shows an error and does not navigate.
- [ ] Write/verify repeated `initiate` calls with the same idempotency key — assert no duplicate payment attempt is created and the same result comes back.
- [ ] Write/verify the `PendingPayment` polling — assert it stops after its fixed number of attempts and does not poll forever.
- [ ] Write/verify the manual "check again" control — assert it re-queries even after bounded polling has stopped.
- [ ] Write/verify an unknown or mismatched `resultToken` — assert the same generic not-found state renders as for an unknown order.
- [ ] Write/verify a production build — assert the `bank` route is not registered and `SandboxBankPage` is unreachable.
- [ ] Write/verify the sandbox flow still works end to end in development.
- [ ] Write/verify every state above also renders correctly at 390×844.
