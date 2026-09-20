# Shop continuation design — baseline 34dc44e

This directory describes the next Shop work after S26–S31. It does not describe a new module from scratch.

## Delivered baseline

The current module already has tenant-scoped category/product/variant/size-guide administration, public category and product detail, anonymous persistent carts, province shipping rates, coupons, checkout totals, transactional guest orders, sandbox payment, guest tracking, four admin pages and Shop permission enforcement. The API host activates Shop after IAM and Shop owns its EF context and migration-history table.

## Gaps selected for S32–S42

| Slice | User-visible gap | Why it comes now |
|---|---|---|
| S32 | Product images | Current detail page explicitly renders a placeholder because the API has no media fields. |
| S33 | All-product discovery/search/sort | Darno exposes a large browseable catalog; current UI can only enter one category. |
| S34 | Root and child categories | Darno's navigation groups entries such as T-shirt and trouser subtypes; current categories are flat. |
| S35 | Store identity and policies | Current public shell is hard-coded “فروشگاه” and has no support/policy content. |
| S36 | Reservation expiry | Current cart immediately decrements stock and abandoned carts never release it. |
| S37 | Coupon limits | Current coupons have no minimum, cap or redemption limit. |
| S38–S39 | Operator orders | Orders can be created/tracked but tenants cannot list, inspect, fulfil or cancel them. |
| S40–S41 | Production payment | Current provider is intentionally an in-app sandbox. |
| S42 | Anonymous abuse controls | Lookup/cart/order/payment endpoints have no rate-limit policy. |

## Cross-cutting decisions

- Keep guest checkout. Customer accounts are not introduced by this roadmap.
- Keep monetary JSON/storage compatible with the current decimal-Toman implementation through S40. ZarinPal conversion happens once at the provider boundary and is covered by unit/integration tests.
- Keep contracts in the Shop module until a real second .NET consumer exists. A speculative `Shop.Contract` project is rejected.
- Add media through a module-owned storage seam and safe decode/re-encode. Never expose a static uploads directory.
- Treat immediate cart stock decrement as a lease. S36 closes the abandoned-cart leak without replacing the delivered cart API.
- All admin resources remain tenant scoped and use the shared permission catalog. Public routes reveal only published/active data.
- Historical product/price/address data remains snapshotted on orders. Admin order views do not join live catalog fields.

## Frontend delivery order

Frontend work is intentionally mock-first across the whole continuation:

1. `F044`–`F053` build every screen, interaction and important failure state
   with contract-shaped mock clients. This gives the product owner one complete
   UI review before backend integration changes behavior.
2. `F054`–`F063` connect one capability at a time. Components continue to use
   the same client interfaces and Zod wire schemas; each task replaces exactly
   one mock binding with an HTTP implementation.

The complete rule and folder shape live in
[the frontend contract boundary](frontend-contract-boundary.md). A delivered
backend mismatch blocks the connection task for an explicit contract decision;
it must not trigger a silent component-and-adapter rewrite.

Planned and delivered wire shapes are preserved in
[`http-contracts.md`](http-contracts.md) so frontend work does not depend on a
backend executable Spec that is deleted after delivery.

## Reference storefront

The roadmap uses [darnoshop.com](https://darnoshop.com/) as product research, not as code or branding to copy. Search indexing observed a Persian men's clothing catalog with category/subcategory navigation, product search and sorting, sale discovery, product galleries, size/color selection, cart, payment/help pages, shipping/returns/privacy content, support phone/social links and order tracking. TenantForge implements the reusable capabilities behind those flows with its own design system and multi-tenant rules.
