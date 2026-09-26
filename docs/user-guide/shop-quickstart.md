# Shop quickstart — run the dev environment and create a store

> English guide for the Shop module. The full Persian product guide (platform admin + tenant
> membership) lives next to this file: [user-guide/README.md](README.md). The API contract
> reference for Shop is [docs/design/shop/http-contracts.md](../design/shop/http-contracts.md).
>
> Verified against the code on 2026-09-26. If a step below disagrees with what your screen
> shows, the code wins — this document is a map, not the source of truth.

This guide answers two questions:

1. **How do I run the app and see a store's first (customer-facing) page?**
2. **How do I create and fill a store, step by step, as the platform admin?**

## 1. Mental model — two different "stores"

TenantForge has two sides, and they have **different URLs**. This is the #1 source of confusion.

| Side | Who uses it | URL | Login? |
|---|---|---|---|
| **Platform dashboard** | the platform administrator | `/dashboard`, `/users`, `/platform/tenants` | yes, platform admin |
| **Tenant admin shell** ("back office") | a tenant member/owner who runs the store | `/t/<tenantId>` and `/t/<tenantId>/shop/...` | yes, a member of that tenant |
| **Storefront** (the actual shop customers see) | any visitor | `/shop/<tenantId>` | **no — it is public** |

The storefront is **not** a tab inside the admin. There is no "view my store" button in the
admin shell yet; you open it by typing its public URL. The only place the storefront appears in
the admin shell is as a **live preview** on the *Shop identity & policies* page
(see Step 4 below).

### One tenant = one store

A **tenant** (مستأجر) is the multi-tenant boundary. Every store belongs to exactly one tenant,
and every storefront URL embeds that tenant's id: `/shop/<tenantId>`. The id is the 13-character
TSID you can read from the address bar whenever you are inside the tenant
(`/t/<tenantId>/...`).

## 2. What is real and what is a mock, right now

The Shop module is being delivered in two frontend phases. **Today (after F053, before F054)**
the following is true — this determines what will "stick" when you refresh the page:

**Real API (persisted in PostgreSQL, survives refresh):**

| Screen | Where | Notes |
|---|---|---|
| Products (create/edit/delete, variants, size guide) | `/t/<id>/shop/products` | real B026 catalog API |
| Shipping rates (per-province cost) | `/t/<id>/shop/shipping-rates` | real B029 API, upsert by province name |
| Product detail page | `/shop/<id>/products/<slug>` | real B027 anonymous API (the gallery *images* below it are still mock) |
| Add to cart (from the product page) | — | real B028 cart API; the cart record is real and survives refresh |
| Checkout summary (price/shipping/coupon totals) | `/shop/<id>/checkout` | real B030 API, computed from the real cart + your real shipping rates |
| Order placement + sandbox payment initiation | `/shop/<id>/order-review` | real B031 order API + real B032 sandbox-gateway initiation |
| Order tracking ("پیگیری سفارش") | `/shop/<id>/track-order` | real B033 anonymous API |

**Mock UI (deterministic in-memory data, resets on page reload, not your real data):**

| Screen | Where | Why it is mock |
|---|---|---|
| Storefront catalog / search / sort / sale filter (the first page) | `/shop/<id>` | HTTP binding lands in F055 |
| Category manager | `/t/<id>/shop/categories` | HTTP binding lands in F056 |
| Store identity & policies (the header/footer content you publish) | `/t/<id>/shop/profile` | HTTP binding lands in F057 |
| Cart **page** display + lease countdown | `/shop/<id>/cart` | cart-lease HTTP binding lands in F058 |
| Coupon field at checkout (code evaluation) | `/shop/<id>/checkout` | coupon-rules HTTP binding lands in F059 |
| Sandbox bank approve/decline + payment-result page | `/shop/<id>/bank`, `/payment-result` | payment-lifecycle HTTP binding lands in F062 |
| Admin order list & detail, fulfil/cancel | `/t/<id>/shop/orders` | HTTP binding lands in F060 / F061 |

Practical consequences to expect:

- Things you create on the **mock** screens (profile, coupons, the storefront catalog, admin
  orders) disappear when you reload the browser. That is by design for this phase, not a bug.
  Real records (products, shipping rates, carts, placed orders) survive.
- A real purchase can even complete end to end today (real cart → real checkout summary →
  real order + real sandbox payment initiation) — but the **cart page** still *displays* a
  canned mock cart, the bank page and the payment-result page still resolve through the mock
  payments client, and the admin **orders** page only ever shows the mock's demo orders (it is
  not connected to the real order records yet — that arrives with F060).
- The demo products on the mock first page have **no real database records**, so clicking one
  opens the real product-detail page, which answers "not found". Your own products (Step 6)
  render correctly at their real slugs.
- A small **dev-only scenario toolbar** appears in the bottom corners of the screen while you run
  `npm run dev`. Those buttons re-seed the mock into named states (empty, error, 429, …). They
  never exist in a production build, and the last connection task (F063) deletes them.

## 3. Run the development environment

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download),
[Node.js 20+](https://nodejs.org/), [Docker](https://www.docker.com/).
On the reference WSL setup there is no Linux `dotnet` binary — use `dotnet.exe` from the Windows
SDK (details in [../architecture.md](../architecture.md)).

**Terminal 1 — PostgreSQL**

```bash
docker compose up -d postgres
```

PostgreSQL 16, database/user/password `tenantforge`, port 5432.

**Terminal 2 — the .NET API (port 5000)**

```bash
# Linux / macOS
dotnet run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000

# Reference WSL setup (API runs on the Windows host)
ASPNETCORE_ENVIRONMENT=Development dotnet.exe run --project src/api/TenantForge.Api/TenantForge.Api.csproj --urls http://0.0.0.0:5000
```

On startup the API applies EF Core migrations and seeds the development platform administrator
when it is absent. Check health:

```bash
curl http://localhost:5000/health
```

**Terminal 3 — the web app (port 5173)**

```bash
cd src/web
npm install
npm run dev
```

Open `http://localhost:5173/login`. Vite proxies `/api/*` to the API host, so the browser stays
same-origin. If you run the API as a Windows process from WSL, the proxy already targets the
WSL gateway IP automatically; you can override it with `VITE_API_PROXY_TARGET`.

### Development sign-in

In the `Development` environment the login screen shows a **"حساب توسعه مستندشده"** card with the
seeded credentials:

```text
Email:    admin@tenantforge.local
Password: local-development-password
```

These are development-only; the production login screen never displays them.

## 4. See a store's first page (fastest path, 2 minutes)

You do **not** need to create anything for this.

1. In any browser (signed-out/incognito is ideal — the storefront is public), open:

   ```text
   http://localhost:5173/shop/demo
   ```

   `demo` is just a tenant id; the mock discovery client answers for any id.

2. You are looking at the store's **first page** — the storefront catalog
   (`StorefrontCatalogPage`): the store header (name + tagline), the category bar, the
   product grid with search box, sort and category/sale filters, pagination, and the footer
   (support phone, Instagram, five policy links).

3. Walk the customer journey (mix of real and mock — see section 2 for the full map):

   - the **first page grid** is the discovery *mock*: the demo products it shows have no real
     database records, so clicking one lands on the real product-detail page's "not found" state.
   - product detail: **real** once you use one of *your* product slugs (Step 6). The gallery
     image strip below it is still mock.
   - the **cart page** (`/shop/demo/cart`) displays a canned *mock* cart with a lease countdown,
     independent of anything you added; the "add to cart" button on the product page itself does
     hit the **real** cart API.
   - `/shop/demo/checkout` → checkout: the live totals (subtotal, shipping cost from your real
     rates, grand total) come from the **real** B030 summary API; the coupon code field is still
     evaluated against the mock coupon list.
   - `/shop/demo/order-review` → **real** order creation (B031) + real sandbox payment initiation
     (B032); in dev you then land on the **sandbox bank** (`/shop/demo/bank`, dev-only route),
     where approve/decline and the resulting payment-result page still resolve through the mock
     payments client.
   - `/shop/demo/track-order` → **real** order lookup: it answers only for orders the real
     backend created, by tracking code + phone.

If you would rather see the seeded demo state the mocks were built around, use tenant id
`0RN590ZYXKNZ2`: `http://localhost:5173/shop/0RN590ZYXKNZ2` — the orders/payments mocks only
seed their demo data for that id.

## 5. Create a store, step by step (as platform admin)

This is the real flow. You are the **platform admin** (`admin@tenantforge.local`); a store is a
**tenant**, and the store's first owner is chosen at creation time.

### Step 1 — create the platform user who will own the store

The platform admin cannot own a tenant by default, so create an owner account first.

1. After login you are on the platform dashboard. In the right sidebar, under **هویت و دسترسی**,
   open **کاربران پلتفرم** (`/users`).
2. **ایجاد کاربر جدید** (new user). Fill in *ایمیل* (e.g. `owner@demo.local`), *نام نمایشی*
   (e.g. `Demo Owner`) and *رمز عبور* (your choice — this is a dev password, keep it local).
3. Result: success message, the user appears in the table.
   Note: creating a user does **not** join them to any tenant yet.

### Step 2 — create the tenant (the store)

1. In the same section, open **مستأجران** (`/platform/tenants`).
2. **ایجاد مستأجر جدید** (new tenant). Fields:
   - *نام مستأجر* — e.g. `Demo Store`
   - *شناسه* (slug) — e.g. `demo-store`
   - *مالک نخست* (first owner) — pick the user from Step 1. Use **بارگذاری کاربران بیشتر**
     if they are not on the first page.
3. Result: the tenant exists and the chosen user is its **Owner** — and in TenantForge the Owner
   holds **every** permission of that tenant, including all three Shop keys
   (`Shop.Catalog.Manage`, `Shop.Shipping.Manage`, `Shop.Orders.View`). No extra role setup is
   needed for the owner.

> **Dev shortcut:** the owner picker lists all platform users, **including the platform admin**.
> If you want *your* admin account to be able to run the store's back office, select
> `admin@tenantforge.local` as the first owner. Remember: a platform admin who is **not** a
> member of a tenant gets an in-shell access-denied page when entering it — platform status alone
> never grants tenant access.

### Step 3 — enter the tenant and open the shop section

1. On the tenant row click **ورود** (or pick the tenant in the header scope switcher). You are now
   at `/t/<tenantId>` — note the `<tenantId>` in the address bar; you will need it for the
   storefront URL.
2. The right sidebar now has a second section, **فروشگاه** (Shop), with six items:
   - دسته‌بندی‌های فروشگاه — categories (mock phase)
   - محصولات فروشگاه — products (real API)
   - نرخ‌های ارسال — shipping rates (real API)
   - کدهای تخفیف — coupons (mock phase)
   - سفارش‌ها — orders (mock phase)
   - هویت و سیاست‌های فروشگاه — identity & policies (mock phase)

   If an item is greyed out, your account lacks that permission — as the Owner you have all of
   them, so a greyed item means you entered a tenant you are not a member of.

### Step 4 — give the store its identity (name, tagline, policies)

Open **هویت و سیاست‌های فروشگاه** (`/t/<id>/shop/profile`).

- **Identity block:** نام فروشگاه, tagline, support phone, Instagram URL. These render in the
  storefront **header** (name + tagline) and **footer** (plus phone/Instagram and the five
  policy links) — and you can see them live in the in-page storefront preview.
- **Policies:** about, shipping, payment, returns, privacy — each renders its own public page
  (`/shop/<id>/about`, `/shipping`, `/payment`, `/returns`, `/privacy`).
- Toggle **isPublished** and save. An unpublished (or missing) profile makes the storefront show
  a neutral "not yet open" header/footer **but the catalog still works** — publication gates
  only the identity surface.

⚠️ *Mock-phase note:* this form talks to the profile **mock**, which comes pre-seeded with the
sample store «فروشگاه نور» and keeps its state in memory only — the values you type survive
until you reload the page (and after a reload you see the sample again). The real `ShopProfile`
persistence (B039) is already delivered; the frontend binding is F057.

### Step 5 — create categories

The **دسته‌بندی‌های فروشگاه** page (`/t/<id>/shop/categories`) is the mock-phase UI: two-level
tree (roots + one level of children), create/edit form with name, slug, display order, parent
(only active roots are offered as parents), active toggle. Everything you do here is in-memory
and resets on reload.

**Known gap — read this once:** the **Products** page reads its category picker from the **real**
catalog API, while the category *manager* above is still a mock. The real backend has no seed
categories (B025 decided against catalog seed data), so on a fresh tenant the product form's
category picker can be **empty**, and a product cannot be saved without a category.

Two options today:

1. **Create the root category through the real API** (dev-only, 10 seconds). From the browser
   DevTools console of an authenticated admin tab you can read the token, or just grab a bearer
   token once:

   ```bash
   TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
     -H 'Content-Type: application/json' \
     -d '{"email":"owner@demo.local","password":"<your dev password>"}' | \
     python3 -c 'import sys,json;print(json.load(sys.stdin)["accessToken"])')

   curl -s -X POST "http://localhost:5000/api/tenants/<tenantId>/shop/categories" \
     -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
     -d '{"name":"General","slug":"general","displayOrder":100,"parentCategoryId":null}'
   ```

   Then **refresh the products page** — the category appears in the picker. (The real endpoint
   is `POST /api/tenants/{tenantId}/shop/categories`, requires `Shop.Catalog.Manage`, rejects
   duplicate slugs with 409, and validates `name`/`slug` ≤ 120 chars.)
2. Or simply treat Step 6 as "watch the product form" and add the product once F056 has bound the
   category manager to this same real API (then the in-UI form in Step 5 is enough).

### Step 6 — add the first product (real, persisted)

Open **محصولات فروشگاه** (`/t/<id>/shop/products`).

1. **ایجاد محصول** (new product). Required fields:
   - *نام محصول* (name), *نامک* (slug — unique per tenant, duplicate → 409 shown under the
     field), *دسته‌بندی* (category — from the picker fed by the real API, Step 5),
   - *قیمت پایه* (base price, positive number); optional compare-at price; active toggle;
   - **variants** — at least one row: color, size, SKU, stock quantity, optional price override;
   - optional **size guide** — add columns, then one row per size.
2. Save. The product is in PostgreSQL and survives refresh.
3. On the **storefront**, open `/shop/<tenantId>/products/<slug>` — the real product detail page
   loads it (gallery section is mock until F054).

### Step 7 — set shipping rates (real, persisted)

**نرخ‌های ارسال** (`/t/<id>/shop/shipping-rates`): one row per province — *نام استان* +
*هزینه ارسال* (cost ≥ 0). Submitting an existing province **updates** its cost (upsert by
province name); there is no separate province table. The real checkout-summary API (B030) prices
the cart using these rates, so a rate you set here changes the shipping cost shown at checkout.

### Step 8 — publish & view your real store

1. Open the public URL with **your** tenant id:

   ```text
   http://localhost:5173/shop/<tenantId>
   ```

2. What you will see, honestly (the exact mix is in section 2):
   - **Header/footer identity** → mock (your Step 4 values, only until reload; real after F057).
   - **Category bar** → mock.
   - **The all-products first page** → mock catalog (the seeded demo products, not yours yet;
     real after F055).
   - **Product detail for your product** → **real** — navigate to
     `/shop/<tenantId>/products/<your-slug>` and your Step 6 product renders from the database.
   - **Checkout totals** → **real**, priced with your Step 7 shipping rates (B030).
   - **Cart-page display, bank approve/decline, payment result, admin orders** → mock
     (F058/F060/F061/F062).
   - **Track order** → real.

### Step 9 (optional) — play with the dev scenario toolbar

While running `npm run dev`, a small toolbar sits in the bottom corners of the screen
(aria-label «ابزار سناریوهای نمایشی (فقط توسعه)»). Each button re-seeds **one capability's**
mock into a named state — empty gallery, upload failing, cart lease expired, rate-limited (429),
server unavailable — and reloads the page. Use it to preview how the store degrades in each
failure state. It is removed by F063 and never bundled in production.

## 6. URLs cheat-sheet

| What | URL |
|---|---|
| Login | `/login` |
| Platform dashboard | `/dashboard` |
| Platform users | `/users` |
| Tenants (create a store) | `/platform/tenants` |
| Tenant home (members) | `/t/<tenantId>` |
| Roles / invitations / audit | `/t/<tenantId>/roles` · `/invitations` · `/audit` |
| Shop admin — categories / products / shipping / coupons / orders / profile | `/t/<tenantId>/shop/categories` · `/products` · `/shipping-rates` · `/coupons` · `/orders` · `/profile` |
| **Storefront first page** | `/shop/<tenantId>` |
| Storefront — category / product / cart / checkout / order-review | `/shop/<tenantId>/categories/<slug>` · `/products/<slug>` · `/cart` · `/checkout` · `/order-review` |
| Storefront — payment (dev sandbox bank), result, tracking | `/shop/<tenantId>/bank` (dev only) · `/payment-result` · `/track-order` |
| Storefront — policy pages | `/shop/<tenantId>/about` · `/shipping` · `/payment` · `/returns` · `/privacy` |

## 7. Troubleshooting

| Symptom | Likely cause | What to do |
|---|---|---|
| Login says the service is unavailable | API not running, or WSL can't reach the Windows API host | `curl http://localhost:5000/health`; in WSL use the gateway IP or `VITE_API_PROXY_TARGET` |
| «دسترسی به این بخش مجاز نیست» inside a tenant | Your account is a platform admin but **not a member** of that tenant | Create the tenant with your user as first owner, or have the owner check memberships. Platform status never grants tenant access |
| A shop nav item is greyed out | The account lacks that tenant permission | Owner has all; for other members assign a role that contains the key (e.g. `Shop.Catalog.Manage`) on the **نقش‌ها** page |
| Products page: no category to pick | Fresh tenant, no real categories yet, and the category manager is still mock | Use the curl from Step 5 (option 1), or wait for F056 |
| The storefront shows demo products, not yours | The first page is still the discovery mock (F055 pending) | Your real products are visible on the real product-detail page; the grid switches over after F055 |
| The cart page shows a different (demo) cart than what I added | The cart *page* display is the lease mock (F058 pending); the add-to-cart button uses the real cart API | Expected for now; the real cart record exists and checkout prices from it |
| Profile/coupon/storefront-grid values vanished after reload | Those screens are mock-phase (F055/F057/F059 pending) | Expected for now; not a data loss of real records |
| Orders page shows the same demo orders | Admin orders are mock-seeded (F060 pending) | Expected; real order reading arrives with F060 |
| Storefront header says «هنوز آماده‌سازی نشده» | Profile not published (or mock reset on reload) | Publish the profile (Step 4). In the mock phase you must re-save after every reload |
| Session expired mid-work | The dev token expired or the tab's session storage was cleared | Log in again |

## 8. Where this is heading

The mock→HTTP bindings are already specced in `tasks/TASKS.md`: **F054 → F063** connect, in
order, media, discovery, categories, profile, cart, coupons, orders, order operations, payments
and rate limiting to the delivered B036–B046 backend contracts. When that sequence lands, every
row in the "mock" table above becomes real data, the scenario toolbars are deleted, and the
step-by-step flow of this guide stays valid — only the "what is real" column changes.
