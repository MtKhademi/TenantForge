# F047 — Build contract-shaped storefront identity and policy mocks

## Ownership, phase and dependencies

- Owner: UI engineer; run `/front-task F047` from the `front` clone.
- Required skills: load `vertical-slice-delivery` and `tenantforge-ui-system` completely before planning.
- Phase: **UI mock — no network call to the paired future backend capability**.
- Slice: `S35`; depends on `F046`.
- Planned backend contract: `B039`. Read that complete backend Spec; its request/response/error names are fixed input to this task even when its implementation is not delivered yet.
- Persistent contract: read the matching section of `docs/design/shop/http-contracts.md`; it remains after the executable backend Spec is delivered and deleted.
- Visible outcome: The user reviews branded storefront header/footer, settings form and five policy pages without saving to the backend.

## Do this in order

Before step 1, follow the standing rules already described in the "Ownership,
phase and dependencies" section above: read `AGENTS.md`, read the linked
slice file (`S35`), read the paired backend contract Spec `B039`, and follow
the branch-naming and ledger-update rules from `AGENTS.md`'s Ownership
section. Do not skip those just because they are not repeated below.

1. Open `docs/design/shop/http-contracts.md` and find the section for `B039`. Note every field name, type and nullability it defines — copy these exactly, do not rename or reshape them.
2. Create `contracts/shopProfileContract.ts`. In it, write the Zod schemas and TypeScript types exactly as shown in "Required contract/code shape" below. ("Zod schema" means a runtime validator plus TypeScript type generator, already used elsewhere in `features/shop/contracts/`.)
3. Define `shopProfileSchema` exactly as shown below, with all fields: `id`, `tenantId`, `name`, `tagline`, `supportPhone`, `instagramUrl` (nullable), `aboutText`, `shippingPolicy`, `paymentPolicy`, `returnPolicy`, `privacyPolicy`, `isPublished`, `version`, `updatedAtUtc`.
4. Define `SaveShopProfileRequest` exactly as shown below: it is `ShopProfile` minus `id`, `tenantId`, `version`, `updatedAtUtc`, plus a new field `expectedVersion: number | null`. (`expectedVersion` is how the mock simulates optimistic concurrency: the caller sends the version it last saw, and a stale value should trigger the stale-version conflict state — this is the same pattern named "stale version" elsewhere in this Spec.)
5. Also define a `PublicShopProfile` type and matching schema (referenced by `getPublic` below but not fully spelled out in the shape block) — write out every field of it in full, matching whatever public-facing subset of `shopProfileSchema`'s fields the backend Spec `B039` exposes publicly (the published, customer-facing fields such as `name`, `tagline`, `supportPhone`, `instagramUrl`, `aboutText`, and the four policy fields — omit any admin-only field `B039` does not expose publicly).
6. Create `features/shop/clients/ShopProfileClient.ts`. Define the `ShopProfileClient` TypeScript interface exactly as shown below, with three methods: `getAdmin`, `save`, `getPublic`. Every method's last parameter is `signal?: AbortSignal` (abort-aware: cancel in-flight work when the caller cancels). `getAdmin` and `getPublic` both return `| null` — meaning "no profile has been created yet"; your UI must handle that null case explicitly (this is the "null-first setup" state named below), not crash or show a blank screen.
7. Create the mock client (place it beside the other mock clients under `features/shop/clients/`, named for this feature) implementing `ShopProfileClient` fully — no method left unimplemented, no `any`, no unchecked casts. Give it deterministic, seeded fixture data.
8. Simulate network latency in the mock client using the shared abort-aware delay helper already used by other mock clients in `features/shop/clients/`. Reuse that helper; do not write a new one.
9. Add named, in-memory scenarios in the mock client for: null-first setup (`getAdmin`/`getPublic` return `null`), published profile, unpublished profile, and a stale-version conflict on `save` (when `expectedVersion` does not match the mock's current stored `version`).
10. Wrap any scenario-selection UI in a check on `import.meta.env.DEV`, so it is stripped from the production build. Verify a production build (`npm run build`) contains no such toolbar and no task ID shown anywhere in production-rendered text.
11. Extend `ShopClientsProvider`'s exported `ShopClients` object with a new `profile` slot wired to the mock profile client for this task.
12. Build/update `ShopProfilePage.tsx` as a plain-text settings form covering every editable field in `SaveShopProfileRequest`. Add a live character counter next to each text field. Add a "published" toggle bound to `isPublished`. Add a preview of how the storefront header/footer will look with the current form values, updated live as the admin types.
13. Update `StorefrontLayout.tsx` (or create it if missing) so the storefront header and footer render the store's `name`, `tagline`, `supportPhone`, and `instagramUrl` (when not null) from the profile client.
14. Create/update `PolicyPage.tsx` as a single reusable component that renders one policy's text. Add routes and navigation entries for exactly five pages: About (uses `aboutText`), Shipping (uses `shippingPolicy`), Payment (uses `paymentPolicy`), Returns (uses `returnPolicy`), and Privacy (uses `privacyPolicy`).
15. When rendering any of the five policy texts or the about text, preserve line breaks (newlines) as visual line breaks, but render the text as plain text — never interpret it as HTML (no `dangerouslySetInnerHTML` or equivalent).
16. Decide what the storefront shows when `isPublished` is false: render a neutral fallback (e.g., "this store is not yet open" style message) instead of the real header/footer/policy content. Apply this same unpublished fallback consistently across the header, footer and policy pages — do not show real content on some and the fallback on others.
17. Consume the `profile` client only through `ShopClientsProvider` in every component you touch. Never import mock fixtures directly into a component, and never call `fetch` anywhere in this task.
18. Implement every state listed in "Required states" below: idle/initial, loading (without layout shift), success, empty, the named validation/409/403/404/410/429 states, unavailable-with-retry, and aborted/superseded request handling. Never show a success message the mock did not actually send.
19. Run `npm run build` and `npm run lint`; fix all errors before moving on.
20. Manually exercise the app in a real browser at 1440×900, 1024×768 and 390×844 (see "Browser evidence and validation" for exactly what to click through), including long Persian text wrapping on all three viewports, and capture evidence.
21. Report the exact line: `Data source: mock profile client; HTTP integration deferred to the matching F054–F063 task.`
22. Walk the "Definition of done" checklist at the bottom of this file item by item before marking this task's ledger row as review/done, per the ledger rules in `AGENTS.md`.

## Files expected to change

`contracts/shopProfileContract.ts`, `clients/ShopProfileClient.ts`, mock client, provider extension, ShopProfilePage, StorefrontLayout, PolicyPage, routes and navigation.

Own only `src/web/**`, this task's ledger row and browser evidence. Do not edit backend, migrations or backend tests. Do not inspect or run frontend tests.

## Contract-first rule

This mock is not throwaway UI data. Define the exact wire contract once under `features/shop/contracts/` using TypeScript types plus Zod response schemas. Define a feature client interface under `features/shop/clients/`; both mock and later HTTP implementations must satisfy that same interface. Components consume the client through `ShopClientsProvider`, never import fixtures and never call `fetch`.

JSON member casing and nullability mirror `B039` exactly. Mock IDs are canonical 13-character TSID strings (TSID = "a sortable numeric string ID — see `TenantForge.BuildingBlocks`"; treat it as an opaque 13-character string). Timestamps are ISO UTC strings; statuses/error codes are only the backend Spec values. Do not add UI-only members to wire types—derive view models separately when needed.

## Required contract/code shape

```ts
import { z } from 'zod'

export const shopProfileSchema = z.object({ id: z.string(), tenantId: z.string(), name: z.string(), tagline: z.string(), supportPhone: z.string(), instagramUrl: z.string().nullable(), aboutText: z.string(), shippingPolicy: z.string(), paymentPolicy: z.string(), returnPolicy: z.string(), privacyPolicy: z.string(), isPublished: z.boolean(), version: z.number().int(), updatedAtUtc: z.string() })
export type ShopProfile = z.infer<typeof shopProfileSchema>
export type SaveShopProfileRequest = Omit<ShopProfile,'id'|'tenantId'|'version'|'updatedAtUtc'> & { expectedVersion: number | null }
export interface ShopProfileClient { getAdmin(tenantId: string, signal?: AbortSignal): Promise<ShopProfile | null>; save(tenantId: string, body: SaveShopProfileRequest, signal?: AbortSignal): Promise<ShopProfile>; getPublic(tenantId: string, signal?: AbortSignal): Promise<PublicShopProfile | null> }
```

Complete the schemas and types for every nested member named by the backend Spec — this includes `PublicShopProfile`, referenced above but not fully spelled out here (see step 5). Do not leave `any`, unchecked casts, placeholder comments or duplicated competing types.

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
- [ ] Write/verify the null-first setup scenario — `getAdmin` and `getPublic` return `null` — assert the UI shows a setup/empty state, not a crash or blank screen.
- [ ] Write/verify editing and saving the profile form — assert every field in `SaveShopProfileRequest` is editable and character counters update live.
- [ ] Write/verify the stale-version scenario — call `save` with an `expectedVersion` that does not match the mock's stored `version` — assert a conflict state renders and the save is rejected.
- [ ] Write/verify the published-profile scenario — assert the storefront header/footer show `name`, `tagline`, `supportPhone`, and `instagramUrl` when not null.
- [ ] Write/verify the unpublished-profile scenario — assert the same neutral fallback renders across header, footer and all five policy pages.
- [ ] Write/verify each of the five policy pages (About, Shipping, Payment, Returns, Privacy) renders its correct field's text with newlines preserved as line breaks and no HTML interpreted.
- [ ] Write/verify long Persian text wrapping correctly (no overflow/clipping) at 1440×900, 1024×768 and 390×844.
