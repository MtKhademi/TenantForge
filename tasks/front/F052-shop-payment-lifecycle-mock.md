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
2. Create `contracts/paymentLifecycleContract.ts` under `features/shop/contracts/`. In it, define Zod schemas ("Zod schema" = a runtime validator plus TypeScript type generator already used elsewhere in `features/shop/contracts/`) for every payload named in `B044`, matching JSON member casing and nullability exactly as `B044` specifies. Start from the two schemas given in "Required contract/code shape" below (`paymentInitiationSchema`, `paymentResultSchema`) and add a Zod schema for every other nested member or payload that `B044` names but that is not shown in that snippet. Do not add any field that is not in `B044`.
3. From each schema, export a matching TypeScript type using `z.infer<typeof schemaName>` (e.g. `export type PaymentInitiation = z.infer<typeof paymentInitiationSchema>`).
4. Create `clients/ShopPaymentsClient.ts` under `features/shop/clients/`. In it, define the `ShopPaymentsClient` interface exactly as shown in "Required contract/code shape" below: `initiate`, `getStatus`, and the optional `resolveSandbox`. Every method takes an `AbortSignal` ("AbortSignal" = the standard browser signal used to cancel an in-flight request; a method that accepts one is "abort-aware") as its last parameter, called `signal`, and is optional to pass.
5. Implement a mock class that implements `ShopPaymentsClient`. Put it in the mock client location already used by this feature area (the same folder pattern as other Shop feature mocks). Do not import fixtures and do not call `fetch` anywhere in this class or in any component.
6. Make every method on the mock deterministic (same inputs always produce the same result) and route every simulated delay through one shared abort-aware latency helper (a helper function that takes a `signal` and rejects immediately if the signal is already aborted, instead of a bare `setTimeout`).
7. Give the mock a way to select named scenarios (for example an in-memory scenario map keyed by order ID or by a query parameter), so a reviewer can force each of the states listed in "Required states" below. Do not show task IDs (like `F052`) anywhere in production UI text.
8. Gate any scenario-selection UI (a "scenario toolbar") behind `import.meta.env.DEV` (a build-time flag that is `true` only in the local dev server, `false` in a production build). Verify a production build does not render this toolbar.
9. Extend `ShopClientsProvider` (the existing React context/provider that hands feature clients to components) to also provide the payments client, so any component can get it via the provider instead of constructing or importing it directly.
10. Build `PaymentRedirectPage`: it calls `initiate` with `tenantId`, `orderId`, and an idempotency key ("idempotent" = calling it more than once with the same key produces the same result instead of creating duplicates), then navigates the user only after validating that `redirectUrl`'s scheme and host are safe (allow only the expected scheme, e.g. `https`, and the expected host(s); reject anything else and show an error instead of navigating).
11. Build `PaymentResultPage`: it must resolve its state purely from the opaque `resultToken` in the URL (not from session/draft state or a query-string "outcome" flag), so refreshing the page after a redirect still shows the correct state. It calls `getStatus` with that token.
12. In `PaymentResultPage`, implement the `Pending` status as bounded fake polling (poll `getStatus` a fixed number of times with a delay between each, then stop) plus a manual "Retry"/"Check again" control the user can click at any time.
13. Build `SandboxBankPage` (the fake bank UI used only for the `Sandbox` provider) and mark it explicitly Development-only — gate its route/entry point the same way as the scenario toolbar (`import.meta.env.DEV`), so it is unreachable in a production build.
14. Implement every state listed under "Required states" below across these pages: idle/initial, loading (with no destructive layout shift — the page must not jump/reflow when loading finishes), success, the relevant empty state, the exact validation/409/403/404/410/429 states that `B044` names for this capability, unavailable-with-retry, and aborted/superseded request handling (an older in-flight request's result must never overwrite a newer one).
15. Re-read the "Required contract/code shape" section below and confirm every named member compiles with no `any`, no unchecked casts, and no placeholder comments. Remove any duplicated or competing type definitions.
16. Run the browser evidence and validation steps in the "Browser evidence and validation" section below, at all three viewports, and fix anything that fails before moving on.
17. Walk through every line of the "Definition of done" checklist below and check it off only once you have verified it, not assumed it.

## Files expected to change

`contracts/paymentLifecycleContract.ts`, `clients/ShopPaymentsClient.ts`, mock client, provider extension, PaymentRedirectPage, PaymentResultPage and Development-only SandboxBankPage.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

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

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority (do not show or imply a confirmation the backend has not actually returned).

## Browser evidence and validation

Capture evidence for all of the following. Treat each bullet as its own checklist item:

- [ ] All result states (`PendingPayment`, `Paid`, `Cancelled`, `Fulfilled`) render correctly on `PaymentResultPage`.
- [ ] Refreshing `PaymentResultPage` after landing on it still shows the correct state (state comes from the opaque token, not session/draft/query state).
- [ ] A malformed/unsafe redirect URL is rejected before navigation (scheme/host check from step 10 fires and shows an error).
- [ ] Repeated initiate calls with the same idempotency key do not create duplicate payment attempts.
- [ ] Pending state polling stops after its bounded number of attempts, and the manual retry control works.
- [ ] Sandbox Bank page still works after any earlier changes (regression check) and is unreachable in a production build.
- [ ] All of the above are also demonstrated on mobile (390×844).

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock payments client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B044` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
