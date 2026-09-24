# TenantForge task ledger

This file is the single source of truth for task order, status and dependencies.
Run frontend work from the `front` clone, backend work from the `backend`
clone and use the `main` clone only for coordination and merged truth.

## Status lifecycle

- `planned`: not started; runnable only when every dependency is `done`.
- `in_progress`: implementation is active on its owning task branch.
- `review`: validation is complete. Backend awaits final user approval;
  frontend performs automated self-review and proceeds when clean.
- `done`: delivered through its pull request; dependencies may rely on it.

Readiness and blocking are derived from the dependency column. Never mark a
blocked task runnable by changing its status.

## Spec lifecycle

Every non-done task has one complete executable spec under `tasks/front/` or
`tasks/backend/`. After final backend approval or clean automated frontend
self-review, the delivery change must:

1. change only the active row to `done`;
2. replace its Spec link with `—`;
3. delete that active executable spec file;
4. preserve the source slice and Git/PR history as the permanent record.

A `done` row must not have a live task spec. A non-done row must have exactly
one valid Spec link.

## Front queue

| ID | Slice | Task | Status | Depends on | Spec |
|---|---|---|---|---|---|
| F001 | S00 | UI foundation and mock login | done | — | — |
| F002 | S01 | Connect development login | done | F001, B001 | — |
| F003 | S02 | Authenticated shell | done | F002, B002 | — |
| F004 | S02 | Refactor Persian RTL interface | done | F003 | — |
| F005 | S02 | Refactor collapsible RTL sidebar | done | F004 | — |
| F006 | S03 | Dashboard summary mock | done | F005 | — |
| F007 | S03 | Connect dashboard summary | done | F006, B003 | — |
| F008 | S06 | User management mock | done | F007 | — |
| F009 | S06 | Connect user management | done | F008, B006 | — |
| F010 | S07 | Tenant membership mock | done | F009 | — |
| F011 | S07 | Connect tenant membership | done | F010, B007 | — |
| F012 | S08 | Tenant isolation UI | done | F011, B008 | — |
| F013 | S09 | Permission matrix mock | done | F012 | — |
| F014 | S09 | Connect permission matrix | done | F013, B009 | — |
| F015 | S10 | Audit and invitations mock | done | F014 | — |
| F016 | S10 | Connect audit and invitations | done | F015, B010 | — |
| F017 | S08 | Verify non-admin tenant isolation in browser | done | F012, B011 | — |
| F018 | S11 | Navigate platform and tenant scopes correctly | done | F017, B012 | — |
| F023 | S16 | Keep member navigation inside the selected tenant | done | F018 | — |
| F019 | S12 | Render permissions from the server catalog | done | F023, B013 | — |
| F020 | S13 | Handle custom invitation roles and honest pending states | done | F019, B014 | — |
| F021 | S14 | Polish Persian product copy and current documentation | done | F020 | — |
| F022 | S15 | Connect server pagination to tables and selectors | done | F021, B015 | — |
| F024 | S17 | Write a practical Persian user-guide README | done | F022 | — |
| F025 | S25 | Fix duplicated branding and inconsistent header controls in the shared shell | done | F024 | — |
| F026 | S25 | Extract a shared state panel and give dashboard cards semantic weight | done | F025 | — |
| F027 | S25 | Put the sign-in form before the hero on mobile and de-emphasize the dev-credentials box | done | F025 | — |
| F028 | S26 | Admin catalog management mock | done | F025 | — |
| F029 | S26 | Connect admin catalog management to the real API | done | F028, B026 | — |
| F030 | S26 | Storefront catalog browsing and product detail mock | done | F025 | — |
| F031 | S26 | Connect storefront browsing and detail to the real API | done | F030, B027 | — |
| F032 | S27 | Cart page mock | done | F030 | — |
| F033 | S27 | Connect cart page to the real API | done | F032, B028 | — |
| F034 | S28 | Admin shipping-rate and coupon management mock | done | F029 | — |
| F035 | S28 | Connect admin shipping-rate and coupon management to the real API | done | F034, B029 | — |
| F036 | S28 | Checkout page mock | done | F033 | — |
| F037 | S28 | Connect checkout page to the real API | done | F036, B030 | — |
| F038 | S29 | Order review and sandbox payment mock | done | F037 | — |
| F039 | S29 | Connect order review and payment to the real API | done | F038, B031, B032 | — |
| F040 | S30 | Order tracking page mock | done | F039 | — |
| F041 | S30 | Connect order tracking page to the real API | done | F040, B033 | — |
| F042 | S31 | Group ShellNav into labelled module sections | done | F029 | — |
| F043 | S31 | Gate Shop nav items on the new Shop permission keys | done | F042, B035 | — |
| F044 | S32 | Build contract-shaped product gallery and storefront image mocks | done | F043 | — |
| F045 | S33 | Build contract-shaped storefront discovery mocks | done | F044 | — |
| F046 | S34 | Build contract-shaped nested category mocks | done | F045 | — |
| F047 | S35 | Build contract-shaped storefront identity and policy mocks | done | F046 | — |
| F048 | S36 | Build contract-shaped cart expiry mocks | done | F047 | — |
| F049 | S37 | Build contract-shaped advanced coupon mocks | done | F048 | — |
| F050 | S38 | Build contract-shaped admin order list and detail mocks | planned | F049 | [F050 Spec](front/F050-shop-admin-orders-mock.md) |
| F051 | S39 | Build contract-shaped fulfil and cancel mocks | planned | F050 | [F051 Spec](front/F051-shop-order-operations-mock.md) |
| F052 | S40 | Build contract-shaped gateway-neutral payment mocks | planned | F051 | [F052 Spec](front/F052-shop-payment-lifecycle-mock.md) |
| F053 | S42 | Build contract-shaped Shop rate-limit mocks | planned | F052 | [F053 Spec](front/F053-shop-rate-limit-mock.md) |
| F054 | S32 | Bind product media client to B036 HTTP contract | planned | F053, B036 | [F054 Spec](front/F054-shop-product-media-connect.md) |
| F055 | S33 | Bind discovery client to B037 HTTP contract | planned | F054, B037 | [F055 Spec](front/F055-shop-discovery-connect.md) |
| F056 | S34 | Bind category hierarchy client to B038 HTTP contract | planned | F055, B038 | [F056 Spec](front/F056-shop-category-hierarchy-connect.md) |
| F057 | S35 | Bind storefront profile client to B039 HTTP contract | planned | F056, B039 | [F057 Spec](front/F057-shop-profile-policies-connect.md) |
| F058 | S36 | Bind cart lease UI to B040 contract | planned | F057, B040 | [F058 Spec](front/F058-shop-cart-expiry-connect.md) |
| F059 | S37 | Bind coupon rules client to B041 HTTP contract | planned | F058, B041 | [F059 Spec](front/F059-shop-coupon-rules-connect.md) |
| F060 | S38 | Bind admin orders client to B042 HTTP contract | planned | F059, B042 | [F060 Spec](front/F060-shop-admin-orders-connect.md) |
| F061 | S39 | Bind order operation client to B043 HTTP contract | planned | F060, B043 | [F061 Spec](front/F061-shop-order-operations-connect.md) |
| F062 | S41 | Bind payment lifecycle to B044/B045 HTTP contracts | planned | F061, B044, B045 | [F062 Spec](front/F062-shop-payment-connect.md) |
| F063 | S42 | Bind shared 429 handling to B046 rate-limit contract | planned | F062, B046 | [F063 Spec](front/F063-shop-rate-limit-connect.md) |

## Backend queue

| ID | Slice | Task | Status | Depends on | Spec |
|---|---|---|---|---|---|
| B001 | S01 | Development login API | done | — | — |
| B002 | S02 | Current account API | done | B001, F001 | — |
| B003 | S03 | Dashboard summary API | done | B002, F003 | — |
| B004 | S04 | IAM persistence | done | B003, F007 | — |
| B005 | S05 | Seeded platform admin | done | B004 | — |
| B006 | S06 | User management API | done | B005, F008 | — |
| B007 | S07 | Tenant membership API | done | B006, F010 | — |
| B008 | S08 | Tenant isolation | done | B007, F011 | — |
| B009 | S09 | Role permission API | done | B008, F013 | — |
| B010 | S10 | Invitations and audit API | done | B009, F015 | — |
| B011 | S02 | Current account for all authenticated users | done | B008 | — |
| B012 | S11 | Close platform user access and expose my tenants | done | B009, B011, F017 | — |
| B013 | S12 | Unify tenant permission semantics and protect administrators | done | B012, F018 | — |
| B014 | S13 | Make invitation creation atomic and tenant scoped | done | B013, F019 | — |
| B015 | S15 | Add consistent pagination to collection APIs and query filters | done | B014, F021 | — |
| B016 | S18 | Collapse IAM startup behind one registration and one activation seam | done | B015 | — |
| B017 | S19 | Replace persisted IAM GUID identifiers with TSIDs | done | B016 | — |
| B018 | S20 | Extract stable cross-module building blocks | done | B017 | — |
| B019 | S21 | Create a living IAM knowledge base and update gate | done | B018 | — |
| B020 | S22 | Create a living BuildingBlocks knowledge and admission guide | done | B019 | — |
| B021 | S23 | Create the IAM Contract project and move its pagination, login, account and dashboard types | done | B020 | — |
| B022 | S23 | Move users, tenants and tenant-member contract types into the IAM Contract project | done | B021 | — |
| B023 | S23 | Move roles, invitations and audit contract types, then lock the IAM Contract project's exported surface | done | B022 | — |
| B024 | S24 | Create a living IAM Contract knowledge and admission guide | done | B023 | — |
| B025 | S26 | Shop module persistence and skeleton | done | — | — |
| B026 | S26 | Category and product admin API | done | B025 | — |
| B027 | S26 | Public storefront catalog read API | done | B026 | — |
| B028 | S27 | Cart persistence and API | done | B027 | — |
| B029 | S28 | Shipping-rate and coupon admin API | done | B026 | — |
| B030 | S28 | Checkout API | done | B028, B029 | — |
| B031 | S29 | Order creation API | done | B030 | — |
| B032 | S29 | Sandbox payment API | done | B031 | — |
| B033 | S30 | Order lookup API | done | B032 | — |
| B034 | S31 | Shared permission catalog contract (BuildingBlocks) and IAM migration | done | — | — |
| B035 | S31 | Shop permission enforcement (Shop.Catalog.Manage / Shop.Shipping.Manage) | done | B034 | — |
| B036 | S32 | Persist and serve safe product galleries | done | B035 | — |
| B037 | S33 | Add storefront search, sorting and sale discovery | done | B036 | — |
| B038 | S34 | Support one level of storefront subcategories | done | B037 | — |
| B039 | S35 | Add tenant storefront identity and policy content | done | B036, B035 | — |
| B040 | S36 | Expire abandoned carts and release reserved stock | done | B031 | — |
| B041 | S37 | Add enforceable coupon limits and atomic redemption | done | B040, B029 | — |
| B042 | S38 | Expose tenant order list and detail for operators | done | B035, B033 | — |
| B043 | S39 | Fulfil or cancel orders with inventory-safe transitions | done | B042, B040 | — |
| B044 | S40 | Make payment initiation and verification gateway-neutral and idempotent | done | B032, B043 | — |
| B045 | S41 | Integrate ZarinPal request and server-side verification | done | B044 | — |
| B046 | S42 | Rate-limit sensitive anonymous Shop flows | done | B045, B043 | — |

## Shop continuation: S32–S42

The executable plan below continues the already delivered S26–S31 Shop implementation. Shared decisions and current-gap evidence live in [the Shop continuation design](../docs/design/shop/README.md).

Frontend execution is deliberately two-phase: finish all contract-shaped UI
mocks (`F044`–`F053`) first, then bind those accepted clients to backend HTTP
contracts (`F054`–`F063`). The [frontend contract boundary](../docs/design/shop/frontend-contract-boundary.md)
is mandatory for both phases; connection tasks must not redesign accepted UI.
The persistent [Shop HTTP contracts](../docs/design/shop/http-contracts.md)
survive backend Spec deletion and are updated by every B036–B046 delivery.

- [S32 — Product galleries](slices/032-shop-product-media.md)
- [S33 — Storefront discovery](slices/033-shop-storefront-discovery.md)
- [S34 — Category hierarchy](slices/034-shop-category-hierarchy.md)
- [S35 — Store identity and policies](slices/035-shop-profile-policies.md)
- [S36 — Cart reservation expiry](slices/036-shop-cart-reservation-expiry.md)
- [S37 — Coupon rules](slices/037-shop-coupon-rules.md)
- [S38 — Admin order reading](slices/038-shop-admin-orders.md)
- [S39 — Order operations](slices/039-shop-order-operations.md)
- [S40 — Gateway-neutral payment lifecycle](slices/040-shop-payment-lifecycle.md)
- [S41 — ZarinPal payment](slices/041-shop-zarinpal-payment.md)
- [S42 — Public abuse controls](slices/042-shop-public-abuse-controls.md)

## Cleanup batch: S11–S14

Sources preserve the decisions and demos after executable Specs are removed:

- [S11 — Platform and tenant access boundaries](slices/011-platform-tenant-boundaries.md)
- [S12 — Consistent tenant permissions](slices/012-permission-consistency.md)
- [S13 — Reliable existing invitations](slices/013-invitation-consistency.md)
- [S14 — Clear Persian UI and accurate current documentation](slices/014-product-copy-and-current-docs.md)

Execution order, including the S16 navigation fix, is
B012 → F018 → F023 → B013 → F019 → B014 → F020 → F021.
The dependencies deliberately require each contract change and its UI consumer
to be delivered before the next cleanup pair begins. Do not run these tasks in
parallel or start implementation as part of registering this batch. B012 is the
first candidate once these planned rows and Specs are delivered to main; derive
subsequent readiness from the table, not this explanatory text.

Invitation acceptance/email delivery, broad architecture refactoring and
frontend test repair are outside this batch. S14 records the existing frontend
test ownership restriction; these Specs do not change agent permissions.

When a dependency is pending, report its ID, current status, owning clone and
exact command. Never bypass a dependency merely to keep an agent busy.

## Pagination: S15

- [S15 — Server pagination for lists and their UI consumers](slices/015-list-pagination.md)
- Continue after the cleanup batch: F021 → B015 → F022.
- B015 adds pagination query parameters and response metadata to the seven
  business collection reads; F022 connects tables, role lists and selectors.
- Both tasks remain planned. Registering these Specs does not implement them
  or change the status, dependencies or scope of existing tasks.

## Tenant member navigation: S16

- [S16 — Tenant members and honest navigation scope](slices/016-tenant-member-navigation.md)
- F023 follows F018 and precedes F019 despite its later numeric ID. It completes
  the tenant member destination after F018 removes the ambiguous global Users
  link. B013 retains its backend dependencies; do not run overlapping work in
  parallel. Read execution readiness from the ledger.
- F019 now requires F023 as well as B013. All other existing dependencies and
  task statuses are unchanged; pagination remains downstream of this fix.

## Practical user guide: S17

- [S17 — Learn to use TenantForge through one working example](slices/017-persian-user-guide.md)
- F024 follows F022 so the guide documents the delivered navigation, Persian
  labels, permission behavior and pagination. F023 and F021 are transitive
  prerequisites; existing task dependencies and statuses remain unchanged.
- The deliverable is docs/user-guide/README.md in Persian, linked prominently
  from the root README. This row registers the writing task, not a completed guide.

## IAM module composition seam: S18

- [S18 — Keep IAM composition inside the IAM module](slices/018-iam-module-composition-seam.md)
- B016 is the next backend architecture task after B015. It preserves every
  existing HTTP contract and browser flow while reducing the API host to one
  IAM registration call before `Build` and one asynchronous IAM activation
  call after `Build`.
- B016 and F022 have no file ownership overlap and may proceed in parallel.
  Neither task may absorb the other's scope.

## TSID identifiers: S19

- [S19 — Use one safe identifier across database, domain and HTTP boundaries](slices/019-tsid-identifiers.md)
- B017 starts only after B016 is delivered because it changes IAM domain types,
  persistence mappings, startup verification and every IAM HTTP identifier.
- The migration must preserve existing rows and relationships while replacing
  persisted UUID identity columns with PostgreSQL `bigint`. Existing JWTs and
  bookmarked GUID URLs are intentionally invalid after deployment; users must
  sign in again. Registering this task does not implement the migration.


## Cross-module building blocks: S20

- [S20 — Extract stable cross-module building blocks](slices/020-building-blocks.md)
- B018 follows the completed TSID migration because it relocates the proven
  identifier seam as well as `IModuleConfig` into
  `TenantForge.BuildingBlocks`.
- The task preserves all runtime contracts. It introduces an explicit
  API → module → BuildingBlocks dependency rule and a strict admission test so
  the new project cannot become an ownerless `Common` utility bucket.
- Pagination, EF converters and IAM business concerns remain IAM-owned until a
  second real consumer proves a narrower shared abstraction.


## Living IAM knowledge base: S21

- [S21 — Give agents one living IAM knowledge source](slices/021-iam-knowledge-base.md)
- B019 follows B018 so the handbook records the final BuildingBlocks namespaces
  and dependency direction rather than immediately documenting obsolete paths.
- `docs/modules/IAM.md` becomes the read-first source for IAM questions.
  Backend discovery and review must classify every IAM change: update the
  affected handbook sections or state
  `IAM.md impact: none — <specific reason>`.
- This task changes documentation and agent workflow only. Runtime behavior,
  database schema, endpoints and frontend remain unchanged.


## Living BuildingBlocks knowledge: S22

- [S22 — Give BuildingBlocks one living ownership guide](slices/022-building-blocks-knowledge.md)
- B020 follows B019 and extends its read-first/document-impact workflow to
  `docs/building-blocks/README.md`.
- The guide catalogs every exported type, consumer, dependency and contract
  test, and requires admission evidence before shared code can enter the
  project.
- Future BuildingBlocks changes must update the guide or state
  `BuildingBlocks docs impact: none — <specific reason>`; review blocks vague
  or missing impact decisions.


## IAM contract separation: S23

- [S23 — Separate IAM's public contract from its implementation](slices/023-iam-contract-separation.md)
- B021 follows the completed BuildingBlocks knowledge base because it is the
  next architecture task on `src/modules/iam/**`. It creates
  `TenantForge.Modules.Iam.Contract` and moves the pagination, login,
  account and dashboard request/response types into it.
- B022 and B023 continue the same mechanical move in two more batches
  (users/tenants/tenant-members, then roles/invitations/audit), strictly in
  that order — each depends on the previous one because they share the same
  new project and namespace convention. B023 also adds
  `IamContractArchitectureTests`, locking the project's exact 32-type
  exported surface and its zero-outgoing-reference rule.
- This is a pure architecture refactor: no route, JSON shape, status code or
  persisted schema changes across any of the three tasks. The full
  integration suite passing unmodified after each task is the acceptance
  proof, not a new contract.
- Do not run B021/B022/B023 in parallel and do not start implementation as
  part of registering these Specs. B021 is the first candidate once these
  planned rows and Specs are delivered to main.


## Living IAM Contract knowledge base: S24

- [S24 — Give the IAM Contract project one living ownership guide](slices/024-iam-contract-knowledge-base.md)
- B024 follows B023 so `docs/contracts/iam.md` documents the final,
  delivered namespace layout and 32-type roster rather than an in-flight
  one — the same ordering B020 used after B018.
- This task changes documentation and agent workflow only (a new
  `docs/contracts/iam.md`, an `AGENTS.md` "Living IAM Contract knowledge"
  section, and a cross-link from `docs/modules/IAM.md`). Runtime behavior,
  database schema, endpoints and frontend remain unchanged.
- Future changes to `TenantForge.Modules.Iam.Contract` must update the
  guide or state `IAM Contract docs impact: none — <specific reason>`;
  review blocks vague or missing impact decisions, the same rule already in
  force for `docs/modules/IAM.md` and `docs/building-blocks/README.md`.
  
## Visual polish pass: S25

- [S25 — Visual polish pass: brand rhythm, semantic weight, shared states](slices/025-visual-polish-pass.md)
- Diagnosed by running the real application in a browser and screenshotting
  the login, dashboard, users and tenant-scoped pages: a redundant
  "TenantForge" wordmark repeated across the header/sidebar/login hero, one
  flat neutral treatment for every dashboard summary card, the same
  error/empty panel markup duplicated across eight page files, and a
  mobile login layout that scrolls past the full hero before reaching the
  form.
- F025 goes first (shared shell/brand fix; every other page renders through
  the same shell). F026 (shared `StatePanel` + dashboard card tinting) and
  F027 (login mobile reorder) both depend only on F025 and have no file
  overlap with each other, so they may proceed in parallel once F025 is
  `done`.
- This is a presentation-only pass: no new design token, no invented
  metric or content, no API contract change, `docs/design-system.md` is
  extended in place (not replaced), and the existing
  `tenantforge-ui-system` skill still governs every visual decision.
  Registering these Specs does not implement them or change any other
  task's status or dependencies.

## Shop catalog: S26

- [S26 — Shop catalog: persistence, admin authoring, storefront browsing](slices/026-shop-catalog.md)
- TenantForge's first new business module, `TenantForge.Modules.Shop`,
  starts here, mirroring IAM's real structure and its two-method
  composition seam (`AddShopModule`/`UseShopModuleAsync`) from the start,
  since that convention is now established (B016/S18). B025 creates the
  module skeleton and the catalog persistence (categories, products,
  variants, size guide) with no endpoint of its own. B026 adds
  authenticated, tenant-scoped admin authoring endpoints (category CRUD;
  one combined product+variants+size-guide authoring payload). B027 adds
  the first anonymous endpoints TenantForge has ever had — a public,
  unauthenticated storefront catalog read API — introducing the
  `/api/shop/{tenantId}/...` route prefix that every later Shop task
  reuses, distinct from the authenticated `/api/tenants/{tenantId}/shop/...`
  admin prefix.
- F028/F029 build and connect the admin catalog screens inside the
  existing authenticated `DashboardShell`. F030/F031 build and connect the
  public storefront browsing/product-detail pages in a new, separate,
  unauthenticated layout — grounded in the darnoshop.com screenshots
  already reviewed for which UI elements exist (color/size selectors, a
  size-guide table, an image gallery, a quantity stepper), while every
  actual visual decision still follows `docs/design-system.md`.
- No `TenantForge.Modules.Shop.Contract` project exists yet (per the
  `module-contract-project` Skill's admission rule) and no
  `docs/modules/Shop.md` living handbook exists yet (per `AGENTS.md`'s
  "Living module knowledge" precedent — IAM only got its handbook after
  several delivered slices). Both remain future tasks, not part of this
  registration.
- Execution order: B025 → B026 → B027, with F028 → F029 and F030 → F031
  each depending on their own backend counterpart; F028 and F030 both
  depend only on the already-`done` F025 and may proceed in parallel with
  each other. All rows are registered as `planned`; registering them does
  not implement or run any of B025–B027/F028–F031.

## Shop cart: S27

- [S27 — Shop cart](slices/027-shop-cart.md)
- B028 adds `ShopCart`/`ShopCartItem` persistence and the anonymous cart
  API (create, add item with live-stock validation, update quantity,
  remove, fetch with a computed subtotal). Cart ownership is by opaque
  cart id alone — every Shop endpoint from B025 onward is deliberately
  anonymous, matching darnoshop's own guest-first flow; this is a
  reviewed, named scope decision, not a placeholder for "add customer
  accounts later."
- F032 builds the cart page against mocked data; F033 connects it to
  B028 and introduces the cart-id's client-side storage, deliberately
  using `localStorage` (not the `sessionStorage` the existing auth
  adapter uses) because a cart, unlike a login session, should survive a
  closed tab.
- Execution order: B028 depends on B027; F032 depends on F030; F033
  depends on F032 and B028. All rows are registered as `planned`;
  registering them does not implement or run any of B028/F032/F033.

## Shop checkout: S28

- [S28 — Shop checkout: shipping, coupons, address](slices/028-shop-checkout.md)
- B029 adds tenant-scoped admin endpoints for per-province shipping rates
  and coupons, reusing B026's authorization pattern. B030 adds a
  read/compute-only checkout-summary endpoint that prices a cart against
  a shipping address and an optional coupon, and requires an unshippable
  province to say so plainly rather than silently charging zero shipping.
- F034/F035 build and connect the admin shipping-rate/coupon screens.
  F036/F037 build and connect the checkout page (address form, coupon
  field, live summary).
- Execution order: B029 depends on B026; B030 depends on B028 and B029;
  F034 depends on F029; F035 depends on F034 and B029; F036 depends on
  F033; F037 depends on F036 and B030. All rows are registered as
  `planned`; registering them does not implement or run any of
  B029/B030/F034–F037.

## Shop order and sandbox payment: S29

- [S29 — Shop order creation and sandbox payment](slices/029-shop-order-and-sandbox-payment.md)
- B031 turns a validated checkout summary into a real, persisted order
  inside one transaction — snapshotting the cart, decrementing variant
  stock, generating an `OrderNumber` and a `TrackingCode`, and clearing
  the cart — so a stock race under concurrent checkouts can never
  oversell. B032 adds the `IShopPaymentGateway` abstraction and its one
  real implementation, `SandboxPaymentGateway`: an in-app fake "bank page"
  the frontend renders, with an initiate endpoint and a callback endpoint
  that moves the order to `Paid` or leaves it `PendingPayment`/`Failed`.
- This slice's explicit, named non-goal: no real ZarinPal integration, no
  stored card data, and no webhook signature scheme beyond what the
  Sandbox provider needs to demonstrate the `IShopPaymentGateway` seam is
  real — a later task swaps in a real provider behind the same interface,
  and that task is not registered here.
- F038/F039 build and connect the order-review screen, the fake bank
  page, and the payment-result screen.
- Execution order: B031 depends on B030; B032 depends on B031; F038
  depends on F037; F039 depends on F038, B031 and B032. All rows are
  registered as `planned`; registering them does not implement or run any
  of B031/B032/F038/F039.

## Guest order tracking: S30

- [S30 — Guest order tracking](slices/030-guest-order-tracking.md)
- B033 adds an anonymous order-lookup endpoint requiring a tracking code
  and a phone number together (never the tracking code alone, to close an
  enumeration path a short human-typed code alone would otherwise open)
  and returns one identical, generic not-found response whether the
  tracking code is unknown or the phone number simply does not match it —
  never a response that reveals which half was wrong.
- F040/F041 build and connect the guest order-tracking page (a
  tracking-code + phone form and a status result view).
- Execution order: B033 depends on B032; F040 depends on F039; F041
  depends on F040 and B033. All rows are registered as `planned`;
  registering them does not implement or run any of B033/F040/F041.

## Cross-module permissions and navigation: S31

- [S31 — Cross-module permission catalog and modular navigation](slices/031-cross-module-permissions-and-nav.md)
- The user found this himself, after reviewing the real, already-merged
  Shop admin screens (F028/F029, B025–B031 on `main`): `ShellNav.tsx`
  mixes IAM and Shop destinations in one flat, ungrouped list, and every
  mutating Shop admin endpoint checks only tenant **membership**, never
  a permission key — any tenant member, not just an Owner or a
  role-holder, can create or edit Shop categories, products, shipping
  rates and coupons today. Worse, this cannot be fixed inside Shop
  alone: IAM's own permission catalog and role-validation code only
  ever know about IAM's own hardcoded 3 groups, so a tenant owner cannot
  even save a role containing a `Shop.*` key without a shared,
  cross-module catalog contract first.
- B034 adds a small `TenantForge.BuildingBlocks.Permissions` seam
  (`PermissionDescriptor`/`PermissionGroup`/`IPermissionCatalogContributor`/
  `IAggregatedPermissionCatalog`/`AggregatedPermissionCatalog`) and
  migrates IAM's existing catalog onto it with zero visible behavior
  change — including, beyond what was first proposed, threading the
  aggregate catalog into `RolesFeature.ResolvePermissionsAsync` and its
  two external callers (`AuditFeature.cs`, `InvitationsFeature.cs`),
  because that function is what backs the `/me/permissions` endpoint
  the frontend's nav gating already depends on. B035 (depends on B034)
  is the concrete second consumer: it adds `Shop.Catalog.Manage` and
  `Shop.Shipping.Manage`, a permission-checking overload of
  `ShopAuthorization.AuthorizeTenantAccessAsync` mirroring IAM's own
  pattern, and retrofits the 7 real mutating Shop admin endpoints found
  by grepping the current repository (5 read-only endpoints are
  untouched).
- F042 (depends on F029) is a pure presentation restructure of
  `ShellNav.tsx` into two labelled sections ("هویت و دسترسی", "فروشگاه")
  — no behavior change. F043 (depends on F042 and B035) gates the two
  existing Shop nav items on `Shop.Catalog.Manage`, mirroring the
  existing `requires` mechanism exactly; it also confirms, by reading
  the real `RolesPage.tsx`, that its generic `PermissionMatrix`
  rendering needs no change at all once the server catalog includes a
  Shop group.
- Named non-goals: no third Shop permission key; no nav entry added for
  the shipping-rate/coupon admin pages (no such destination exists in
  `ShellNav.tsx` today — that gap, if it is one, is pre-existing and out
  of scope here); no change to `RolesPage.tsx`; no anonymous/read-only
  Shop endpoint change.
- Execution order: B034 has no dependency; B035 depends on B034; F042
  depends on F029; F043 depends on F042 and B035. All rows are
  registered as `planned`; registering them does not implement or run
  any of B034/B035/F042/F043.
