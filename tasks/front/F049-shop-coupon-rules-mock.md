# F049 — Build contract-shaped advanced coupon mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F049` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S37`; depends on `F048`.
- Planned backend contract: `B041`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews coupon minimum, cap, redemption-limit and edit flows before the coupon schema changes.

## Do this in order

Before anything else, follow the fixed boilerplate: read `AGENTS.md`, read the `S37` slice file, read the paired backend Spec `B041`, and follow the branch-naming and ledger-update rules already described in the "Ownership, phase and dependencies" section above. Do not skip those.

1. Read the full backend Spec `B041` and the matching section of `docs/design/shop/http-contracts.md`. Write down every field name, type and nullability for the coupon shape and its error/reason codes.
2. Create `src/web/src/features/shop/contracts/couponRulesContract.ts`. In it, define a Zod schema (a Zod schema is a runtime validator + TypeScript type generator created by `F044` in `src/web/src/features/shop/contracts/shopContract.ts`) named `couponSchema` with these exact fields: `id` (string), `code` (string), `discountType` (enum `'Percentage' | 'FixedAmount'`), `discountValue` (number), `minimumSubtotal` (number), `maximumDiscountAmount` (number, nullable), `redemptionLimit` (integer, nullable), `redeemedCount` (integer), `isActive` (boolean), `expiresAtUtc` (string, nullable), `version` (integer).
3. Export `export type Coupon = z.infer<typeof couponSchema>`.
4. Define `export type CreateCouponRequest` as a `Pick` of `Coupon`'s fields: `code`, `discountType`, `discountValue`, `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`, `expiresAtUtc`.
5. Define `export type UpdateCouponRequest` as a `Pick` of the coupon schema's fields `discountValue`, `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`, `expiresAtUtc`, `isActive`, plus an added field `expectedVersion` (number). `expectedVersion` is used for optimistic concurrency (the server rejects the update if the coupon's current version does not match, to avoid two edits silently overwriting each other — this shows up later as the "stale version" state).
6. Define `couponListResponseSchema` as `z.object({ coupons: z.array(couponSchema), pagination: paginationSchema })` (import the shared `paginationSchema` from `src/web/src/features/shop/contracts/shopContract.ts`, created by `F044`; do not redefine it), and export `export type CouponListResponse = z.infer<typeof couponListResponseSchema>`.
7. Create `src/web/src/features/shop/clients/ShopCouponClient.ts`. In it define `export interface ShopCouponClient` with these exact methods (import `PageQuery` and `paginationSchema` from `src/web/src/features/shop/contracts/shopContract.ts`):
   - `list(tenantId: string, query: PageQuery, signal?: AbortSignal): Promise<CouponListResponse>`
   - `create(tenantId: string, body: CreateCouponRequest, signal?: AbortSignal): Promise<Coupon>`
   - `update(tenantId: string, couponId: string, body: UpdateCouponRequest, signal?: AbortSignal): Promise<Coupon>`
   - `deactivate(tenantId: string, couponId: string, signal?: AbortSignal): Promise<Coupon>`
   (`AbortSignal` makes each call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request.)
8. Go through the full backend Spec `B041` again and add every nested member it names (including every coupon reason/error code) to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
9. Create `src/web/src/features/shop/clients/mockShopCouponClient.ts` implementing `ShopCouponClient`. (The interface lives in `ShopCouponClient.ts` from step 7; the mock is a separate file — do not put both in one file.) Make it deterministic (same input always produces the same output) and route all simulated delays through the `delay(ms, signal)` helper `F044` created in `src/web/src/features/shop/clients/shopFetch.ts`.
10. Give the mock client named scenarios (e.g. "create success", "stale version conflict", "exhausted redemption limit") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
11. Add a `coupons` slot to the `ShopClients` type and to `createShopClients()` in `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (created by `F044`), wired to your new mock coupon client. Do not touch any other slot.
12. In `src/web/src/pages/shop/admin/CouponsPage.tsx`, implement mock create, edit and deactivate flows. Treat `null` in `maximumDiscountAmount` or `redemptionLimit` as "unlimited" and display it that way. Show current usage (`redeemedCount` versus `redemptionLimit`). Show distinct visual states for expired coupons and inactive coupons.
13. Label every money input with the existing money unit explicitly (do not leave a bare number with no currency/unit label). Normalize Persian digits typed into these inputs to standard digits before validating/submitting.
14. Once a coupon has any redemptions (`redeemedCount > 0`), lock its `code` and `discountType` fields so they cannot be edited anymore (only the other fields from `UpdateCouponRequest` remain editable).
15. In the checkout coupon-message UI (`src/web/src/pages/shop/storefront/CheckoutPage.tsx`), map every reason code that `B041` defines to a distinct user-facing message. Never decrease the displayed usage count during a checkout preview — usage only changes on a real redemption, which this mock does not simulate as a decrement.
16. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
17. Make sure `CouponsPage.tsx` and the checkout coupon UI get the client only from `useShopClients()` (never by importing fixtures directly and never by calling `fetch` directly).
18. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

Created by this task:

- `src/web/src/features/shop/contracts/couponRulesContract.ts`
- `src/web/src/features/shop/clients/ShopCouponClient.ts` (interface only)
- `src/web/src/features/shop/clients/mockShopCouponClient.ts`

Edited by this task:

- `src/web/src/features/shop/clients/ShopClientsProvider.tsx` (add the `coupons` slot)
- `src/web/src/pages/shop/admin/CouponsPage.tsx`
- `src/web/src/pages/shop/storefront/CheckoutPage.tsx`

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `src/web/src/features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `src/web/src/features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `useShopClients()`, never import fixtures and never call `fetch`. All four of those things were created by `F044` — see that Spec's "Build the shared Shop client seam first" section for their exact contents.

JSON member casing and nullability mirror `B041` exactly. Mock IDs are canonical 13-character TSID strings (TSID: a sortable numeric string ID — see `TenantForge.BuildingBlocks`); timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const couponSchema = z.object({ id: z.string(), code: z.string(), discountType: z.enum(['Percentage','FixedAmount']), discountValue: z.number(), minimumSubtotal: z.number(), maximumDiscountAmount: z.number().nullable(), redemptionLimit: z.number().int().nullable(), redeemedCount: z.number().int(), isActive: z.boolean(), expiresAtUtc: z.string().nullable(), version: z.number().int() })
export type Coupon = z.infer<typeof couponSchema>
export type CreateCouponRequest = Pick<Coupon,'code'|'discountType'|'discountValue'|'minimumSubtotal'|'maximumDiscountAmount'|'redemptionLimit'|'expiresAtUtc'>
export type UpdateCouponRequest = Pick<z.infer<typeof couponSchema>,'discountValue'|'minimumSubtotal'|'maximumDiscountAmount'|'redemptionLimit'|'expiresAtUtc'|'isActive'> & { expectedVersion: number }
export const couponListResponseSchema = z.object({ coupons: z.array(couponSchema), pagination: paginationSchema })
export type CouponListResponse = z.infer<typeof couponListResponseSchema>
export interface ShopCouponClient { list(tenantId: string, query: PageQuery, signal?: AbortSignal): Promise<CouponListResponse>; create(tenantId: string, body: CreateCouponRequest, signal?: AbortSignal): Promise<Coupon>; update(tenantId: string, couponId: string, body: UpdateCouponRequest, signal?: AbortSignal): Promise<Coupon>; deactivate(tenantId: string, couponId: string, signal?: AbortSignal): Promise<Coupon> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

Concretely: this means (see step 8 above) adding every field and every reason/error code `B041` defines, until nothing from that backend Spec is missing from `couponRulesContract.ts`.

## Required UI implementation

Extend provider with coupons. Mock create/edit/deactivate, null-as-unlimited, usage display, expired/inactive and stale version. Inputs label the existing money unit explicitly and normalize Persian digits. Lock code/type after redemption. Map all B041 reason codes in checkout but never decrease usage on preview.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

(See "Do this in order" steps 9–17 for the mechanical breakdown of this section.)

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
  `400` (a coupon field failed validation — negative money, a `redemptionLimit` below `redeemedCount`, a percentage outside 1..100), `403` (the user lacks `Shop.Shipping.Manage`), `404` (coupon not found) and `409` (stale `expectedVersion`). At checkout, also render one distinct message per reason code `B041` defines: `coupon_not_found`, `coupon_inactive`, `coupon_expired`, `coupon_minimum_not_met`, `coupon_limit_reached`. Each needs its own message — do not collapse them into one generic
  "something went wrong". Do **not** invent a state for a status code not listed
  here.
- **unavailable, with retry** — the request could not be made at all (network
  failure). Show a retry control.
- **aborted / superseded** — when a newer request starts, the older one's
  result must never overwrite the newer one's, and an aborted request must not
  surface as an error to the user.
- **honest success feedback** — never show or imply a confirmation the mock did
  not actually return.

## Browser evidence and validation

Create/edit/stale; unlimited and exhausted; validation; checkout minimum/cap/limit messages; responsive table/cards.

Run `cd src/web && npm run build`, then `cd src/web && npm run lint`. Both must succeed with no errors. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock coupons client; HTTP integration deferred to the matching F054–F063 task.`

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
- [ ] Contract schemas/types match `B041` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
- [ ] Write/verify creating a coupon — assert the new coupon appears in the list with the values that were entered.
- [ ] Write/verify editing a coupon — assert the edited values persist in the mock and render back.
- [ ] Write/verify deactivating a coupon — assert its state changes to inactive and it is styled distinctly.
- [ ] Write/verify a coupon with `maximumDiscountAmount: null` and `redemptionLimit: null` — assert both render as "unlimited", not as an empty cell or `null`.
- [ ] Write/verify a coupon at its redemption limit — assert the usage display shows it as exhausted.
- [ ] Write/verify a coupon whose `expiresAtUtc` is in the past — assert the expired visual state renders, distinct from the inactive state.
- [ ] Write/verify a stale `expectedVersion` on update — assert the conflict state renders and the change is rejected.
- [ ] Write/verify a coupon with `redeemedCount > 0` — assert its `code` and `discountType` fields are locked and every other editable field still works.
- [ ] Write/verify typing Persian digits into a money input — assert they are normalized to standard digits before validation and submission.
- [ ] Write/verify every money input carries a visible currency/unit label.
- [ ] Write/verify each of the five `B041` reason codes at checkout — assert each renders its own distinct message.
- [ ] Write/verify a checkout coupon preview — assert the displayed usage count never decreases.
- [ ] Write/verify the coupons table at 1440×900 and its card layout at 390×844.
