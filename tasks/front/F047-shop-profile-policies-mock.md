# F047 — Build contract-shaped storefront identity and policy mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F047` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S35`; depends on `F046`.
- Planned backend contract: `B039`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews branded storefront header/footer, settings form and five policy pages without saving to the backend.

## Files expected to change

`contracts/shopProfileContract.ts`, `clients/ShopProfileClient.ts`, mock client, provider extension, ShopProfilePage, StorefrontLayout, PolicyPage, routes and navigation.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B039` exactly. Mock IDs are canonical 13-character TSID strings; timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const shopProfileSchema = z.object({ id: z.string(), tenantId: z.string(), name: z.string(), tagline: z.string(), supportPhone: z.string(), instagramUrl: z.string().nullable(), aboutText: z.string(), shippingPolicy: z.string(), paymentPolicy: z.string(), returnPolicy: z.string(), privacyPolicy: z.string(), isPublished: z.boolean(), version: z.number().int(), updatedAtUtc: z.string() })
export type ShopProfile = z.infer<typeof shopProfileSchema>
export type SaveShopProfileRequest = Omit<ShopProfile,'id'|'tenantId'|'version'|'updatedAtUtc'> & { expectedVersion: number | null }
export interface ShopProfileClient { getAdmin(tenantId: string, signal?: AbortSignal): Promise<ShopProfile | null>; save(tenantId: string, body: SaveShopProfileRequest, signal?: AbortSignal): Promise<ShopProfile>; getPublic(tenantId: string, signal?: AbortSignal): Promise<PublicShopProfile | null> }
```

Complete the schemas and types for every nested member named by the backend Spec. Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

## Required UI implementation

Extend provider with `profile`. Plain-text settings with character counters, published toggle and preview. Mock null-first setup, published/unpublished and stale version. Header/footer show store name, tagline, phone and Instagram. Add About, shipping, payment, returns and privacy routes; preserve newlines without HTML rendering.

The mock client must be deterministic, simulate latency through an abort-aware helper, and expose named scenarios without production UI showing task IDs. Keep mock switching behind `import.meta.env.DEV`; production build must not expose a scenario toolbar.

## Required states

- idle/initial, loading without destructive layout shift, success and relevant empty state;
- exact validation/409/403/404/410/429 states named by this capability;
- unavailable-with-retry and aborted/superseded request behavior;
- success feedback without inventing server authority.

## Browser evidence and validation

Initial setup/edit/stale states; all policy pages; unpublished neutral fallback; long Persian wrapping on three viewports.

Run `npm run build` and `npm run lint`. Use the real app at 1440×900, 1024×768 and 390×844; inspect keyboard focus, RTL overflow, contrast, layout shift and browser console. Report explicitly: `Data source: mock profile client; HTTP integration deferred to the matching F054–F063 task.`

## Definition of done

- [ ] The whole named flow is reviewable without the backend capability.
- [ ] Contract schemas/types match `B039` and the mock implements the same client port reserved for HTTP.
- [ ] No component imports fixtures or uses `fetch`.
- [ ] Desktop/tablet/mobile, accessibility, build, lint and console checks pass.
- [ ] Only this row becomes review/done; stop before the next mock task.
