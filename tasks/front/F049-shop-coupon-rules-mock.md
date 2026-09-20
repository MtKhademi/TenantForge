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
2. Create (or open) `features/shop/contracts/couponRulesContract.ts`. In it, define a Zod schema (a Zod schema is a runtime validator + TypeScript type generator already used elsewhere in `features/shop/contracts/`) named `couponSchema` with these exact fields: `id` (string), `code` (string), `discountType` (enum `'Percentage' | 'FixedAmount'`), `discountValue` (number), `minimumSubtotal` (number), `maximumDiscountAmount` (number, nullable), `redemptionLimit` (integer, nullable), `redeemedCount` (integer), `isActive` (boolean), `expiresAtUtc` (string, nullable), `version` (integer).
3. Export `export type Coupon = z.infer<typeof couponSchema>`.
4. Define `export type CreateCouponRequest` as a `Pick` of `Coupon`'s fields: `code`, `discountType`, `discountValue`, `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`, `expiresAtUtc`.
5. Define `export type UpdateCouponRequest` as a `Pick` of the coupon schema's fields `discountValue`, `minimumSubtotal`, `maximumDiscountAmount`, `redemptionLimit`, `expiresAtUtc`, `isActive`, plus an added field `expectedVersion` (number). `expectedVersion` is used for optimistic concurrency (the server rejects the update if the coupon's current version does not match, to avoid two edits silently overwriting each other — this shows up later as the "stale version" state).
6. Define `couponListResponseSchema` as `z.object({ coupons: z.array(couponSchema), pagination: paginationSchema })` (reuse the existing shared `paginationSchema`), and export `export type CouponListResponse = z.infer<typeof couponListResponseSchema>`.
7. In the same file, define `export interface ShopCouponClient` with these exact methods:
   - `list(tenantId: string, query: PageQuery, signal?: AbortSignal): Promise<CouponListResponse>`
   - `create(tenantId: string, body: CreateCouponRequest, signal?: AbortSignal): Promise<Coupon>`
   - `update(tenantId: string, couponId: string, body: UpdateCouponRequest, signal?: AbortSignal): Promise<Coupon>`
   - `deactivate(tenantId: string, couponId: string, signal?: AbortSignal): Promise<Coupon>`
   (`AbortSignal` makes each call abort-aware, meaning an in-flight request can be cancelled if the user navigates away or issues a newer request.)
8. Go through the full backend Spec `B041` again and add every nested member it names (including every coupon reason/error code) to these schemas/types. Do not leave any field as `any`, do not add unchecked type casts, do not leave placeholder comments, and do not create a second competing type for something already defined here.
9. Create the file `clients/ShopCouponClient.ts` implementing the mock version of `ShopCouponClient`. Make it deterministic (same input always produces the same output) and route all simulated delays through an existing abort-aware latency helper already used by other mock clients in this codebase.
10. Give the mock client named scenarios (e.g. "create success", "stale version conflict", "exhausted redemption limit") that a developer can switch between. Gate this scenario switching behind `import.meta.env.DEV` (a build-time flag that is `true` only in local development) so a production build never shows a scenario toolbar and never exposes task IDs in the UI.
11. Register the new coupon client on `ShopClientsProvider` (the existing shared provider component).
12. In CouponsPage, implement mock create, edit and deactivate flows. Treat `null` in `maximumDiscountAmount` or `redemptionLimit` as "unlimited" and display it that way. Show current usage (`redeemedCount` versus `redemptionLimit`). Show distinct visual states for expired coupons and inactive coupons.
13. Label every money input with the existing money unit explicitly (do not leave a bare number with no currency/unit label). Normalize Persian digits typed into these inputs to standard digits before validating/submitting.
14. Once a coupon has any redemptions (`redeemedCount > 0`), lock its `code` and `discountType` fields so they cannot be edited anymore (only the other fields from `UpdateCouponRequest` remain editable).
15. In the checkout coupon-message UI, map every reason code that `B041` defines to a distinct user-facing message. Never decrease the displayed usage count during a checkout preview — usage only changes on a real redemption, which this mock does not simulate as a decrement.
16. Implement every state listed under "Required states" below: idle/initial, loading (without shifting the layout in a jarring way), success, the relevant empty state, the exact validation/409/403/404/410/429 states this capability names, an unavailable-with-retry state, and an aborted/superseded request state. Never show success feedback that implies the (non-existent) real backend approved something.
17. Make sure CouponsPage and the checkout coupon UI consume the new client only through the provider (never by importing fixtures directly and never by calling `fetch` directly).
18. Run the validation and browser evidence steps described below, then finish the ledger/PR steps from `AGENTS.md`.

## Files expected to change

`contracts/couponRulesContract.ts`, `clients/ShopCouponClient.ts`, mock client, provider extension, CouponsPage and checkout coupon messages.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

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

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Create/edit/stale; unlimited and exhausted; validation; checkout minimum/cap/limit messages; responsive table/cards.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock coupons client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B041` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
