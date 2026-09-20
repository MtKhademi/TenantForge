# F049 — Build contract-shaped advanced coupon mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F049` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S37`; depends on `F048`.
- Planned backend contract: `B041`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews coupon minimum, cap, redemption-limit and edit flows before the coupon schema changes.

## Files expected to change

`contracts/couponRulesContract.ts`, `clients/ShopCouponClient.ts`, mock client, provider extension, CouponsPage and checkout coupon messages.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B041` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

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

## Required UI implementation

Extend provider with coupons. Mock create/edit/deactivate, null-as-unlimited, usage display, expired/inactive and stale version. Inputs label the existing money unit explicitly and normalize Persian digits. Lock code/type after redemption. Map all B041 reason codes in checkout but never decrease usage on preview.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

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
