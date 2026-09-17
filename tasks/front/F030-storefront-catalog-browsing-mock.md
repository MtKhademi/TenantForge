---
id: F030
slice: S26
title: Storefront catalog browsing and product detail mock
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Build the public-facing storefront: a category grid, product cards within
a category, and a product detail page with an image gallery/lightbox,
variant-aware color and size selectors (each combination shows its own
stock/sold-out state), a size-guide table, a quantity stepper and an
"add to cart" action — all against mocked data.

This Spec gives you the exact file paths, the exact mocked-data types
(matching B027's real public API field-for-field, so F031's later swap
is a pure data-source change), the exact new top-level routes to add
to `App.tsx` (outside `ProtectedLayout` — there is no signed-in session
anywhere on these pages), and a concrete component skeleton for each
page. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` completely.
Read the complete `tasks/slices/026-shop-catalog.md`, in particular its
screenshot-to-task mapping table: this page family's visual shape (color/
size selection, a size-guide table below the gallery, a quantity stepper,
an add-to-cart action) is grounded in the darnoshop.com screenshots the
user and the assistant already reviewed together — this Spec cites that
evidence for *which elements exist and how they relate*, never for
darnoshop's own colors, type or spacing. Every actual visual decision
(color, spacing, radius, shadow, type) still follows `docs/design-system.md`
tokens and the existing shadcn/ui-based component set.

This task depends on `F025` (shared shell/brand foundations), but the
public storefront is a **separate, unauthenticated layout** from
`DashboardShell` — it is not another authenticated admin page and must
not be nested inside `DashboardShell`'s sidebar/header, and must not be
nested inside `App.tsx`'s `<Route element={<ProtectedLayout />}>` block
either (that block requires a session — see `RequireSession` in
`src/web/src/App.tsx`, which redirects to `/login` when signed out; a
storefront visitor is never signed in). Read `src/web/src/App.tsx` fully
before touching it: the storefront routes are new **top-level** `<Route>`
elements, siblings of `<Route path="/login" element={<LoginPage />} />`,
not children of `ProtectedLayout`.

State plainly what minimal shared foundation the storefront still
reuses: the design tokens in `src/web/src/index.css`, and the existing
`Button`/`TextInput`/other shadcn/ui-based primitive components under
`src/web/src/components/ui/` (imported the exact same way admin pages
import them: `import { Button, SecondaryButton } from
'@/components/ui/Button'`). It does not reuse `DashboardShell`,
`ShellNav`, or any authenticated-shell chrome — build a small, dedicated
storefront layout wrapper instead (header with the tenant's brand mark,
no sidebar, no sign-out control).

# Scope — every file, in order

## 1. Mock data types and cart state

**`src/web/src/features/shop/storefrontTypes.ts`** — the exact shape
B027's real public API returns (field names copied from B027's
`StorefrontContracts.cs`, camelCased):

```typescript
/**
 * S26 storefront browsing (F030 mock, F031 connects to B027's real,
 * anonymous public catalog API). No Authorization header is ever sent
 * anywhere on this page family, mocked or real.
 */

export type StorefrontCategory = {
  id: string
  name: string
  slug: string
  displayOrder: number
}

export type StorefrontProductSummary = {
  id: string
  name: string
  slug: string
  effectivePrice: number
  compareAtPrice: number | null
  /** Not part of B027's real response — mock-only, used for the card thumbnail. */
  imageUrl: string
}

export type StorefrontVariant = {
  id: string
  color: string
  size: string
  stockQuantity: number
  effectivePrice: number
}

export type StorefrontSizeGuideColumn = {
  id: string
  name: string
  displayOrder: number
}

export type StorefrontSizeGuideCell = {
  columnId: string
  value: string
}

export type StorefrontSizeGuideRow = {
  sizeLabel: string
  displayOrder: number
  cells: StorefrontSizeGuideCell[]
}

export type StorefrontProductDetail = {
  id: string
  categoryId: string
  name: string
  slug: string
  description: string
  basePrice: number
  compareAtPrice: number | null
  variants: StorefrontVariant[]
  sizeGuideColumns: StorefrontSizeGuideColumn[]
  sizeGuideRows: StorefrontSizeGuideRow[]
  /** Not part of B027's real response — mock-only, gallery image list. */
  imageUrls: string[]
}
```

**`src/web/src/features/shop/mockStorefrontCartState.ts`** — the
mock cart state F030's add-to-cart action populates and F032 (cart page
mock) extends; a small module-scoped store with a subscribe/notify
pattern so both the cart-icon count in `StorefrontLayout.tsx` and the
future cart page re-render on change, without introducing a new state
library:

```typescript
export type MockCartLine = {
  variantId: string
  productName: string
  variantLabel: string
  unitPrice: number
  quantity: number
  imageUrl: string
}

type Listener = () => void

const lines: MockCartLine[] = []
const listeners = new Set<Listener>()

function notify() {
  for (const listener of listeners) listener()
}

export const mockStorefrontCart = {
  getLines(): MockCartLine[] {
    return lines
  },
  addLine(line: MockCartLine) {
    const existing = lines.find((l) => l.variantId === line.variantId)
    if (existing) existing.quantity += line.quantity
    else lines.push(line)
    notify()
  },
  setQuantity(variantId: string, quantity: number) {
    const line = lines.find((l) => l.variantId === variantId)
    if (line) line.quantity = quantity
    notify()
  },
  removeLine(variantId: string) {
    const index = lines.findIndex((l) => l.variantId === variantId)
    if (index !== -1) lines.splice(index, 1)
    notify()
  },
  subscribe(listener: Listener): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  },
  itemCount(): number {
    return lines.reduce((sum, l) => sum + l.quantity, 0)
  },
  subTotal(): number {
    return lines.reduce((sum, l) => sum + l.unitPrice * l.quantity, 0)
  },
}
```

**`src/web/src/features/shop/mockStorefrontCatalog.ts`** — a small,
hardcoded in-memory catalog (2 categories, 2–3 products each, at least
one product with a two-column size guide and at least one variant with
`stockQuantity: 0`) exposing the same read shape B027 will:

```typescript
import type { StorefrontCategory, StorefrontProductDetail, StorefrontProductSummary } from './storefrontTypes'

export const mockStorefrontCatalog = {
  async listCategories(_tenantId: string): Promise<StorefrontCategory[]> {
    return [
      { id: 'cat-1', name: 'پیراهن', slug: 'shirts', displayOrder: 0 },
      { id: 'cat-2', name: 'شلوار', slug: 'pants', displayOrder: 1 },
    ]
  },
  async listProducts(_tenantId: string, categorySlug: string): Promise<StorefrontProductSummary[]> {
    if (categorySlug !== 'shirts') return []
    return [
      { id: 'prod-1', name: 'پیراهن کلاسیک', slug: 'classic-shirt', effectivePrice: 890000, compareAtPrice: null, imageUrl: '' },
    ]
  },
  async getProduct(_tenantId: string, productSlug: string): Promise<StorefrontProductDetail | null> {
    if (productSlug !== 'classic-shirt') return null
    return {
      id: 'prod-1',
      categoryId: 'cat-1',
      name: 'پیراهن کلاسیک',
      slug: 'classic-shirt',
      description: 'پیراهن نخی کلاسیک',
      basePrice: 890000,
      compareAtPrice: null,
      variants: [
        { id: 'var-1', color: 'سفید', size: 'M', stockQuantity: 10, effectivePrice: 890000 },
        { id: 'var-2', color: 'سفید', size: 'L', stockQuantity: 0, effectivePrice: 890000 },
      ],
      sizeGuideColumns: [
        { id: 'col-1', name: 'دور سینه', displayOrder: 0 },
        { id: 'col-2', name: 'دور کمر', displayOrder: 1 },
      ],
      sizeGuideRows: [
        { sizeLabel: 'M', displayOrder: 0, cells: [{ columnId: 'col-1', value: '96' }, { columnId: 'col-2', value: '80' }] },
        { sizeLabel: 'L', displayOrder: 1, cells: [{ columnId: 'col-1', value: '102' }, { columnId: 'col-2', value: '86' }] },
      ],
      imageUrls: [],
    }
  },
}
```

## 2. `src/web/src/pages/shop/storefront/StorefrontLayout.tsx`

```tsx
import { ShoppingCart } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, Outlet, useParams } from 'react-router-dom'
import { mockStorefrontCart } from '@/features/shop/mockStorefrontCartState'

/**
 * S26 storefront (F030): a minimal public layout, deliberately separate
 * from DashboardShell — no sidebar, no sign-out, no authenticated chrome
 * of any kind. Reuses only design tokens and the shared ui/ primitives.
 */
export function StorefrontLayout() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [itemCount, setItemCount] = useState(mockStorefrontCart.itemCount())

  useEffect(() => {
    return mockStorefrontCart.subscribe(() => setItemCount(mockStorefrontCart.itemCount()))
  }, [])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-surface">
        <div className="mx-auto flex max-w-6xl items-center justify-between gap-4 px-4 py-4">
          <Link to={`/shop/${tenantId}`} className="text-lg font-semibold">فروشگاه</Link>
          <Link
            to={`/shop/${tenantId}/cart`}
            className="relative inline-flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm font-semibold hover:bg-muted"
            aria-label="سبد خرید"
          >
            <ShoppingCart aria-hidden="true" className="size-4" />
            {itemCount > 0 && (
              <span className="absolute -top-2 -end-2 inline-flex size-5 items-center justify-center rounded-full bg-primary text-xs text-primary-foreground">
                {itemCount}
              </span>
            )}
          </Link>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
```

## 3. `src/web/src/pages/shop/storefront/CategoryPage.tsx`

Category grid (no `categorySlug` param) or, when a `categorySlug` route
param is present, that category's paginated product-card grid. Mocked
data:

```tsx
import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { mockStorefrontCatalog } from '@/features/shop/mockStorefrontCatalog'
import type { StorefrontCategory, StorefrontProductSummary } from '@/features/shop/storefrontTypes'

export function CategoryPage() {
  const { tenantId = '', categorySlug } = useParams<{ tenantId: string; categorySlug?: string }>()
  const [categories, setCategories] = useState<StorefrontCategory[] | null>(null)
  const [products, setProducts] = useState<StorefrontProductSummary[] | null>(null)

  useEffect(() => {
    void mockStorefrontCatalog.listCategories(tenantId).then(setCategories)
  }, [tenantId])

  useEffect(() => {
    if (!categorySlug) {
      setProducts(null)
      return
    }
    void mockStorefrontCatalog.listProducts(tenantId, categorySlug).then(setProducts)
  }, [tenantId, categorySlug])

  if (!categorySlug) {
    return (
      <section aria-label="دسته‌بندی‌ها" className="space-y-6">
        <h1 className="text-2xl font-semibold">دسته‌بندی‌ها</h1>
        {categories === null && <p>در حال بارگذاری...</p>}
        {categories !== null && categories.length === 0 && <p>دسته‌بندی‌ای موجود نیست.</p>}
        {categories !== null && categories.length > 0 && (
          <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
            {categories.map((category) => (
              <Link
                key={category.id}
                to={`/shop/${tenantId}/categories/${category.slug}`}
                className="rounded-xl border border-border bg-surface p-6 shadow-soft hover:bg-muted"
              >
                {category.name}
              </Link>
            ))}
          </div>
        )}
      </section>
    )
  }

  return (
    <section aria-label="محصولات دسته‌بندی" className="space-y-6">
      {products === null && <p>در حال بارگذاری...</p>}
      {products !== null && products.length === 0 && <p>محصولی در این دسته‌بندی موجود نیست.</p>}
      {products !== null && products.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 md:grid-cols-3">
          {products.map((product) => (
            <Link
              key={product.id}
              to={`/shop/${tenantId}/products/${product.slug}`}
              className="rounded-xl border border-border bg-surface p-4 shadow-soft hover:bg-muted"
            >
              <div className="aspect-square rounded-md bg-muted" aria-hidden="true" />
              <p className="mt-3 font-semibold">{product.name}</p>
              <p className="text-sm text-muted-foreground">{product.effectivePrice.toLocaleString('fa-IR')} تومان</p>
            </Link>
          ))}
        </div>
      )}
    </section>
  )
}
```

## 4. `src/web/src/pages/shop/storefront/ProductDetailPage.tsx`

```tsx
import { useEffect, useMemo, useState } from 'react'
import { useParams } from 'react-router-dom'
import { Button } from '@/components/ui/Button'
import { mockStorefrontCatalog } from '@/features/shop/mockStorefrontCatalog'
import { mockStorefrontCart } from '@/features/shop/mockStorefrontCartState'
import type { StorefrontProductDetail } from '@/features/shop/storefrontTypes'

export function ProductDetailPage() {
  const { tenantId = '', productSlug = '' } = useParams<{ tenantId: string; productSlug: string }>()
  const [product, setProduct] = useState<StorefrontProductDetail | null | undefined>(undefined)
  const [color, setColor] = useState<string | null>(null)
  const [size, setSize] = useState<string | null>(null)
  const [quantity, setQuantity] = useState(1)
  const [addedMessage, setAddedMessage] = useState<string | null>(null)

  useEffect(() => {
    setProduct(undefined)
    void mockStorefrontCatalog.getProduct(tenantId, productSlug).then(setProduct)
  }, [tenantId, productSlug])

  const colors = useMemo(() => [...new Set(product?.variants.map((v) => v.color) ?? [])], [product])
  const sizes = useMemo(() => [...new Set(product?.variants.map((v) => v.size) ?? [])], [product])
  const selectedVariant = useMemo(
    () => product?.variants.find((v) => v.color === color && v.size === size) ?? null,
    [product, color, size],
  )
  const soldOut = Boolean(color && size && (!selectedVariant || selectedVariant.stockQuantity === 0))

  if (product === undefined) return <p>در حال بارگذاری...</p>
  if (product === null) return <p role="alert">محصول یافت نشد.</p>

  return (
    <section aria-label={product.name} className="grid gap-8 md:grid-cols-2">
      <div className="aspect-square rounded-xl bg-muted" aria-hidden="true" />

      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-semibold">{product.name}</h1>
          <p className="mt-2 text-muted-foreground">{product.description}</p>
        </div>

        <div>
          <p className="mb-2 text-sm font-semibold">رنگ</p>
          <div className="flex gap-2">
            {colors.map((c) => (
              <button
                key={c}
                type="button"
                aria-pressed={color === c}
                className="rounded-md border border-border px-3 py-1.5 text-sm aria-pressed:border-primary aria-pressed:bg-primary/10"
                onClick={() => setColor(c)}
              >
                {c}
              </button>
            ))}
          </div>
        </div>

        <div>
          <p className="mb-2 text-sm font-semibold">سایز</p>
          <div className="flex gap-2">
            {sizes.map((s) => (
              <button
                key={s}
                type="button"
                aria-pressed={size === s}
                className="rounded-md border border-border px-3 py-1.5 text-sm aria-pressed:border-primary aria-pressed:bg-primary/10"
                onClick={() => setSize(s)}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        {soldOut && (
          <p role="status" className="text-sm font-semibold text-destructive">این ترکیب رنگ/سایز موجود نیست.</p>
        )}

        {product.sizeGuideColumns.length > 0 && (
          <table className="w-full text-sm">
            <caption className="mb-2 text-start font-semibold">راهنمای سایز</caption>
            <thead>
              <tr>
                <th className="px-2 py-1 text-start">سایز</th>
                {product.sizeGuideColumns.map((col) => (
                  <th key={col.id} className="px-2 py-1 text-start">{col.name}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {product.sizeGuideRows.map((row) => (
                <tr key={row.sizeLabel}>
                  <td className="px-2 py-1">{row.sizeLabel}</td>
                  {product.sizeGuideColumns.map((col) => (
                    <td key={col.id} className="px-2 py-1">
                      {row.cells.find((cell) => cell.columnId === col.id)?.value ?? ''}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <div className="flex items-center gap-3">
          <label className="text-sm font-semibold" htmlFor="quantity">تعداد</label>
          <input
            id="quantity"
            type="number"
            min={1}
            max={selectedVariant?.stockQuantity ?? 1}
            value={quantity}
            disabled={soldOut || !selectedVariant}
            onChange={(event) => setQuantity(Math.min(Number(event.target.value), selectedVariant?.stockQuantity ?? 1))}
            className="w-20 rounded-md border border-input bg-surface px-2 py-1 text-sm"
          />
        </div>

        <Button
          type="button"
          disabled={!selectedVariant || soldOut}
          onClick={() => {
            if (!selectedVariant) return
            mockStorefrontCart.addLine({
              variantId: selectedVariant.id,
              productName: product.name,
              variantLabel: `${selectedVariant.color} / ${selectedVariant.size}`,
              unitPrice: selectedVariant.effectivePrice,
              quantity,
              imageUrl: '',
            })
            setAddedMessage('به سبد خرید افزوده شد.')
          }}
        >
          افزودن به سبد خرید
        </Button>
        {addedMessage && <p role="status" className="text-sm font-medium text-success">{addedMessage}</p>}
      </div>
    </section>
  )
}
```

(The gallery lightbox: add a simple click-to-enlarge overlay over the
image block above using the same `Button`/design tokens — no new
dependency; a minimal `<dialog>`-based or fixed-position overlay is
sufficient and matches this repository's "no new UI library" rule.)

## 5. Add the public storefront routes to `App.tsx`

Edit `src/web/src/App.tsx`. It currently reads:

```tsx
export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedLayout />}>
```

Add the storefront routes as siblings of `/login`, **before** the
`ProtectedLayout` route (order does not affect matching here, but this
keeps every public route grouped together for readability):

```tsx
export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/shop/:tenantId" element={<StorefrontLayout />}>
        <Route index element={<CategoryPage />} />
        <Route path="categories/:categorySlug" element={<CategoryPage />} />
        <Route path="products/:productSlug" element={<ProductDetailPage />} />
      </Route>
      <Route element={<ProtectedLayout />}>
```

Add the matching imports near the top of the file, alongside the
existing page imports:

```tsx
import { StorefrontLayout } from './pages/shop/storefront/StorefrontLayout'
import { CategoryPage } from './pages/shop/storefront/CategoryPage'
import { ProductDetailPage } from './pages/shop/storefront/ProductDetailPage'
```

# Non-goals

- No connection to a real API (F031 connects this page once B027 exists).
- No cart page (F032) — only the add-to-cart interaction and its
  immediate feedback.
- No admin-facing element anywhere on these pages.

# Acceptance

- The category grid and product cards render against mocked data with
  the required states (loading, empty, error).
- The product detail page's variant selectors correctly reflect
  per-combination stock/sold-out state from mocked data, and the
  quantity stepper never allows exceeding the selected variant's mocked
  stock.
- The size-guide table renders correctly for a mocked product that has
  one, and the section is simply absent (not an empty table) for a
  mocked product that has none.
- The storefront layout has no authenticated-shell chrome (no sidebar,
  no sign-out) and no new design token.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900, 1024×768 and 390×844:

- Browse from the category grid into a product's detail page, select
  different color/size combinations and confirm stock/sold-out state
  updates correctly, open the gallery lightbox, and add an in-stock
  combination to the mocked cart with visible success feedback.
- No browser console error.

# Lifecycle

Add row `F030` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F030-storefront-catalog-browsing-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
