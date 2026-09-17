---
id: F028
slice: S26
title: Admin catalog management mock
agent: ui-engineer
source: tasks/slices/026-shop-catalog.md
---

# Objective

Build the admin catalog management screens — category list/create/edit,
and product list/create/edit with inline variant rows and a size-guide
table editor — against mocked data, matching `docs/design-system.md`
exactly. This is the first Shop frontend task; it has no Shop backend
dependency yet since it is mock-only.

This Spec gives you every file's exact path, the exact mock data
shape (field-for-field matching B026's real API, so F029's later swap
is a pure data-source change), the exact imports to reuse, and a
concrete component skeleton. Follow it literally.

# Context

Load `tenantforge-ui-system` and read `docs/design-system.md` completely
before making any visual decision. Read the complete
`tasks/slices/026-shop-catalog.md` — it is the authoritative contract for
this task and its "connect" counterpart, F029.

This task depends on `F025` (the shared shell/brand foundations fix),
which is `done` — the admin catalog screens render inside the existing
authenticated `DashboardShell`, exactly like `UsersPage.tsx`/`TenantsPage.tsx`
already do, and must reuse its already-corrected single-brand-mark header
and `SecondaryButton` icon-control convention. It does not depend on
`F026`/`F027` (the shared `StatePanel` and login-mobile tasks); if
`StatePanel` (F026) is `done` by the time this task starts, use it for
this task's error/empty states — check the ledger before writing a new
one-off panel.

Real frontend files this task's code is modeled on — read them fully
before writing anything:
- `src/web/src/pages/UsersPage.tsx` — the page-level structure this task
  must match: header (title/description/primary action), a
  `SecondaryButton` refresh control, a form that toggles open/closed
  with `aria-expanded`/`aria-controls`, `react-hook-form` + `zod`
  validation with per-field `<p role="...">` error text, a loading
  skeleton component, an empty-state panel, and a table with a
  `<caption className="sr-only">`.
- `src/web/src/features/users/userTypes.ts` and
  `src/web/src/features/users/userAdapter.ts` — the exact
  type/adapter/error-class shape this task's mock adapter must mirror
  (see Scope §1 below): a typed adapter object literal, a strict
  `parseX()`/`parseListResponse()` pair, and dedicated `Error` subclasses
  for each distinct rejection.
- `src/web/src/components/ui/Button.tsx` and `TextInput.tsx` — import
  `Button`, `SecondaryButton` from `@/components/ui/Button` and
  `TextInput` from `@/components/ui/TextInput`. There is no separate
  `Select`/`Checkbox`/`Dialog` component in this repository yet — build
  every form control from a plain, labelled `<input>`/`<select>` styled
  with the same Tailwind utility classes `TextInput.tsx` uses
  (`rounded-md border border-input bg-surface px-3 py-2 text-sm
  focus-visible:border-ring`), not a new component library.
- `src/web/src/components/shell/DashboardShell.tsx` — import
  `DashboardShell` from `@/components/shell/DashboardShell` and wrap
  every page's content in it, exactly like `UsersPage.tsx` does.
- `src/web/src/components/shell/ShellNav.tsx` — the exact `navItems`
  array this task adds two entries to (see Scope §4).
- `src/web/src/App.tsx` — the exact router shape this task adds two
  routes to (see Scope §5): tenant-scoped authenticated routes are
  `<Route path="/t/:tenantId/...">` nested inside `<Route
  element={<ProtectedLayout />}>`, matching `/t/:tenantId/roles` exactly.

# Scope — every file, in order

## 1. Mock data types

**`src/web/src/features/shop/shopCatalogTypes.ts`** — the exact shape
B026's real API will return (see B026's `CategoryContracts.cs`/
`ProductContracts.cs` — field names below are copied from there,
camelCased as ASP.NET Core's default JSON serialization camelCases every
C# record property):

```typescript
/**
 * S26 admin catalog management (F028 mock, F029 connects to B026).
 *
 * These types mirror B026's ProductContracts.cs/CategoryContracts.cs
 * field-for-field (PascalCase C# property names become camelCase JSON,
 * ASP.NET Core's default). F029 changes nothing about this file except
 * removing this comment's "mock" framing — the shape does not change.
 */

export type ShopCategory = {
  id: string
  tenantId: string
  name: string
  slug: string
  displayOrder: number
  isActive: boolean
}

export type CreateCategoryRequest = {
  name: string
  slug: string
  displayOrder: number
}

export type UpdateCategoryRequest = {
  name: string
  slug: string
  displayOrder: number
  isActive: boolean
}

export type ShopProductVariant = {
  id: string
  color: string
  size: string
  sku: string
  stockQuantity: number
  priceOverride: number | null
}

export type ProductVariantInput = {
  color: string
  size: string
  sku: string
  stockQuantity: number
  priceOverride: number | null
}

export type SizeGuideColumn = {
  id: string
  name: string
  displayOrder: number
}

export type SizeGuideCell = {
  columnId: string
  value: string
}

export type SizeGuideRow = {
  id: string
  sizeLabel: string
  displayOrder: number
  cells: SizeGuideCell[]
}

/** The editor's own working shape: one string value per current column, in column order. */
export type SizeGuideRowInput = {
  sizeLabel: string
  values: string[]
}

export type ShopProduct = {
  id: string
  tenantId: string
  categoryId: string
  name: string
  slug: string
  description: string
  basePrice: number
  compareAtPrice: number | null
  isActive: boolean
  variants: ShopProductVariant[]
  sizeGuideColumns: SizeGuideColumn[]
  sizeGuideRows: SizeGuideRow[]
}

export type ShopProductSummary = {
  id: string
  name: string
  slug: string
  categoryId: string
  basePrice: number
  isActive: boolean
  variantCount: number
}

export type ProductFormValues = {
  name: string
  slug: string
  description: string
  categoryId: string
  basePrice: number
  compareAtPrice: number | null
  isActive: boolean
  variants: ProductVariantInput[]
  sizeGuideColumns: string[]
  sizeGuideRows: SizeGuideRowInput[]
}

export class CategoryConflictError extends Error {
  constructor(message = 'دسته‌بندی دیگری با این نامک از قبل وجود دارد.') {
    super(message)
    this.name = 'CategoryConflictError'
  }
}

export class ProductConflictError extends Error {
  constructor(message = 'محصول دیگری با این نامک از قبل وجود دارد.') {
    super(message)
    this.name = 'ProductConflictError'
  }
}
```

## 2. Mock adapter

**`src/web/src/features/shop/mockCatalogAdapter.ts`** — an in-memory mock
adapter, mirroring the mock-adapter pattern already used for other
features before their "connect" task. Store state in a module-level
array (not React state) so it survives remounts within the same page
load, exactly like every other pre-connect mock in this repository:

```typescript
import {
  CategoryConflictError,
  ProductConflictError,
  type CreateCategoryRequest,
  type ProductFormValues,
  type ShopCategory,
  type ShopProduct,
  type ShopProductSummary,
  type UpdateCategoryRequest,
} from './shopCatalogTypes'

/**
 * F028 mock catalog adapter — in-memory only, reset on full page reload.
 * F029 deletes this file entirely and replaces every call site with
 * `httpShopCatalogAdapter` (same method names/signatures).
 */
export type ShopCatalogAdapter = {
  listCategories(tenantId: string): Promise<ShopCategory[]>
  createCategory(tenantId: string, request: CreateCategoryRequest): Promise<ShopCategory>
  updateCategory(tenantId: string, categoryId: string, request: UpdateCategoryRequest): Promise<ShopCategory>
  listProducts(tenantId: string): Promise<ShopProductSummary[]>
  getProduct(tenantId: string, productId: string): Promise<ShopProduct>
  createProduct(tenantId: string, values: ProductFormValues): Promise<ShopProduct>
  updateProduct(tenantId: string, productId: string, values: ProductFormValues): Promise<ShopProduct>
}

let nextId = 1
function mockId(prefix: string): string {
  return `mock-${prefix}-${nextId++}`
}

const categories: ShopCategory[] = []
const products: ShopProduct[] = []

function toSummary(product: ShopProduct): ShopProductSummary {
  return {
    id: product.id,
    name: product.name,
    slug: product.slug,
    categoryId: product.categoryId,
    basePrice: product.basePrice,
    isActive: product.isActive,
    variantCount: product.variants.length,
  }
}

function buildProductFromValues(tenantId: string, id: string, values: ProductFormValues): ShopProduct {
  return {
    id,
    tenantId,
    categoryId: values.categoryId,
    name: values.name,
    slug: values.slug,
    description: values.description,
    basePrice: values.basePrice,
    compareAtPrice: values.compareAtPrice,
    isActive: values.isActive,
    variants: values.variants.map((variant) => ({ id: mockId('variant'), ...variant })),
    sizeGuideColumns: values.sizeGuideColumns.map((name, index) => ({
      id: mockId('column'),
      name,
      displayOrder: index,
    })),
    sizeGuideRows: values.sizeGuideRows.map((row, rowIndex) => ({
      id: mockId('row'),
      sizeLabel: row.sizeLabel,
      displayOrder: rowIndex,
      cells: row.values.map((value, columnIndex) => ({
        columnId: `column-${columnIndex}`,
        value,
      })),
    })),
  }
}

export const mockCatalogAdapter: ShopCatalogAdapter = {
  async listCategories(tenantId) {
    return categories.filter((category) => category.tenantId === tenantId)
  },

  async createCategory(tenantId, request) {
    const slug = request.slug.trim().toLowerCase()
    if (categories.some((category) => category.tenantId === tenantId && category.slug === slug)) {
      throw new CategoryConflictError()
    }
    const category: ShopCategory = {
      id: mockId('category'),
      tenantId,
      name: request.name,
      slug,
      displayOrder: request.displayOrder,
      isActive: true,
    }
    categories.push(category)
    return category
  },

  async updateCategory(tenantId, categoryId, request) {
    const category = categories.find((c) => c.tenantId === tenantId && c.id === categoryId)
    if (!category) throw new Error('Category not found')
    const slug = request.slug.trim().toLowerCase()
    if (categories.some((c) => c.tenantId === tenantId && c.slug === slug && c.id !== categoryId)) {
      throw new CategoryConflictError()
    }
    category.name = request.name
    category.slug = slug
    category.displayOrder = request.displayOrder
    category.isActive = request.isActive
    return category
  },

  async listProducts(tenantId) {
    return products.filter((product) => product.tenantId === tenantId).map(toSummary)
  },

  async getProduct(tenantId, productId) {
    const product = products.find((p) => p.tenantId === tenantId && p.id === productId)
    if (!product) throw new Error('Product not found')
    return product
  },

  async createProduct(tenantId, values) {
    const slug = values.slug.trim().toLowerCase()
    if (products.some((p) => p.tenantId === tenantId && p.slug === slug)) {
      throw new ProductConflictError()
    }
    const product = buildProductFromValues(tenantId, mockId('product'), { ...values, slug })
    products.push(product)
    return product
  },

  async updateProduct(tenantId, productId, values) {
    const index = products.findIndex((p) => p.tenantId === tenantId && p.id === productId)
    if (index === -1) throw new Error('Product not found')
    const slug = values.slug.trim().toLowerCase()
    if (products.some((p) => p.tenantId === tenantId && p.slug === slug && p.id !== productId)) {
      throw new ProductConflictError()
    }
    const updated = buildProductFromValues(tenantId, productId, { ...values, slug })
    products[index] = updated
    return updated
  },
}
```

## 3. `src/web/src/pages/shop/admin/CategoriesPage.tsx`

A table of categories (name, slug, display order, active toggle) with a
create/edit form (name, slug, display order, active), matching
`UsersPage.tsx`'s structure. Skeleton (fill in the loading/empty/error
states the same way `UsersPage.tsx` does — this excerpt shows the
required imports, state shape and JSX structure, not every branch):

```tsx
import { Loader2, RefreshCw, Tag } from 'lucide-react'
import { zodResolver } from '@hookform/resolvers/zod'
import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useParams } from 'react-router-dom'
import { z } from 'zod'
import { DashboardShell } from '@/components/shell/DashboardShell'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { mockCatalogAdapter } from '@/features/shop/mockCatalogAdapter'
import { CategoryConflictError, type ShopCategory } from '@/features/shop/shopCatalogTypes'

const categorySchema = z.object({
  name: z.string().min(1, 'نام دسته‌بندی الزامی است.').max(120),
  slug: z.string().min(1, 'نامک الزامی است.').max(120),
  displayOrder: z.coerce.number().int().min(0),
})
type CategoryFormValues = z.infer<typeof categorySchema>

export function CategoriesPage() {
  const { tenantId = '' } = useParams<{ tenantId: string }>()
  const [categories, setCategories] = useState<ShopCategory[] | null>(null)
  const [isBusy, setIsBusy] = useState(true)
  const [formOpen, setFormOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)

  const { register, handleSubmit, reset, setError, formState: { errors } } =
    useForm<CategoryFormValues>({
      resolver: zodResolver(categorySchema),
      defaultValues: { name: '', slug: '', displayOrder: 0 },
    })

  const loadCategories = useCallback(() => {
    setIsBusy(true)
    return mockCatalogAdapter
      .listCategories(tenantId)
      .then(setCategories)
      .finally(() => setIsBusy(false))
  }, [tenantId])

  useEffect(() => {
    void loadCategories()
  }, [loadCategories])

  const onSubmit = useCallback(
    async (values: CategoryFormValues) => {
      try {
        if (editingId) {
          await mockCatalogAdapter.updateCategory(tenantId, editingId, { ...values, isActive: true })
        } else {
          await mockCatalogAdapter.createCategory(tenantId, values)
        }
        reset()
        setFormOpen(false)
        setEditingId(null)
        void loadCategories()
      } catch (error) {
        if (error instanceof CategoryConflictError) {
          setError('slug', { message: error.message })
        }
      }
    },
    [editingId, tenantId, reset, setError, loadCategories],
  )

  return (
    <DashboardShell>
      <section aria-label="دسته‌بندی‌های فروشگاه" className="space-y-6">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="max-w-2xl">
            <p className="text-sm font-semibold text-primary">مدیریت فروشگاه</p>
            <h2 className="mt-2 text-2xl font-semibold tracking-tight md:text-3xl">دسته‌بندی‌ها</h2>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">
              دسته‌بندی‌های محصولات فروشگاه را مشاهده و مدیریت کنید.
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-2">
            <SecondaryButton type="button" disabled={isBusy} onClick={() => void loadCategories()}>
              <RefreshCw aria-hidden="true" className="size-4" />
              <span className="hidden sm:inline">به‌روزرسانی</span>
            </SecondaryButton>
            <Button
              type="button"
              onClick={() => {
                setEditingId(null)
                reset()
                setFormOpen((open) => !open)
              }}
            >
              ایجاد دسته‌بندی
            </Button>
          </div>
        </div>

        {formOpen && (
          <form
            className="rounded-xl border border-border bg-surface p-5 shadow-soft"
            onSubmit={handleSubmit(onSubmit)}
            noValidate
          >
            <div className="grid gap-4 md:grid-cols-3">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-name">نام</label>
                <TextInput id="category-name" {...register('name')} />
                {errors.name && <p className="mt-2 text-sm text-destructive">{errors.name.message}</p>}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-slug">نامک</label>
                <TextInput id="category-slug" {...register('slug')} />
                {errors.slug && <p className="mt-2 text-sm text-destructive">{errors.slug.message}</p>}
              </div>
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="category-order">ترتیب نمایش</label>
                <TextInput id="category-order" type="number" {...register('displayOrder')} />
              </div>
            </div>
            <div className="mt-4 flex gap-2">
              <Button type="submit">ذخیره</Button>
              <SecondaryButton type="button" onClick={() => setFormOpen(false)}>لغو</SecondaryButton>
            </div>
          </form>
        )}

        {isBusy && !categories && <Loader2 className="size-6 animate-spin" aria-label="در حال بارگذاری" />}

        {categories && categories.length === 0 && (
          <div className="rounded-xl border border-border bg-surface p-8 text-center shadow-soft">
            <Tag aria-hidden="true" className="mx-auto size-8 text-muted-foreground" />
            <p className="mt-3 text-sm font-semibold">دسته‌بندی‌ای موجود نیست</p>
          </div>
        )}

        {categories && categories.length > 0 && (
          <div className="overflow-hidden rounded-xl border border-border bg-surface shadow-soft">
            <table className="w-full text-sm">
              <caption className="sr-only">فهرست دسته‌بندی‌ها</caption>
              <thead>
                <tr className="border-b border-border text-start">
                  <th scope="col" className="px-4 py-3 text-start font-semibold">نام</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">نامک</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">ترتیب</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">وضعیت</th>
                  <th scope="col" className="px-4 py-3 text-start font-semibold">عملیات</th>
                </tr>
              </thead>
              <tbody>
                {categories.map((category) => (
                  <tr key={category.id} className="border-b border-border last:border-b-0">
                    <td className="px-4 py-3">{category.name}</td>
                    <td className="px-4 py-3">{category.slug}</td>
                    <td className="px-4 py-3">{category.displayOrder}</td>
                    <td className="px-4 py-3">{category.isActive ? 'فعال' : 'غیرفعال'}</td>
                    <td className="px-4 py-3">
                      <SecondaryButton
                        type="button"
                        onClick={() => {
                          setEditingId(category.id)
                          reset({ name: category.name, slug: category.slug, displayOrder: category.displayOrder })
                          setFormOpen(true)
                        }}
                      >
                        ویرایش
                      </SecondaryButton>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </DashboardShell>
  )
}
```

## 4. `src/web/src/pages/shop/admin/ProductsPage.tsx`

A table of products (name, slug, category, base price, variant count,
active toggle) and a create/edit form with:

- the product's own fields (name, slug, description, category picker —
  a plain `<select>` populated from `mockCatalogAdapter.listCategories`,
  base price, compare-at price);
- an inline, repeatable variant-row editor (color, size, SKU, stock
  quantity, price override) with add/remove row actions, using
  `react-hook-form`'s `useFieldArray` on `variants`
  (`const { fields, append, remove } = useFieldArray({ control, name:
  'variants' })`, exactly the standard `react-hook-form` pattern —
  `react-hook-form` is already a project dependency, see
  `UsersPage.tsx`'s `useForm` import);
- a size-guide table editor built the same way, with two independent
  `useFieldArray`s: one for `sizeGuideColumns` (an array of plain
  strings, each rendered as one `<TextInput>` with a remove button) and
  one for `sizeGuideRows` (each row a `sizeLabel` `TextInput` plus one
  `TextInput` per current column — rendered by mapping over
  `sizeGuideColumns.length`, not by a second `useFieldArray` per row's
  values, since `values` is a plain string array field on each row
  object, not a set of row objects itself).

  Adding a column must push an empty string onto every existing row's
  `values` array; removing a column must splice that column's index out
  of every row's `values` array. Implement this in the column
  add/remove handlers by reading the current rows with
  `getValues('sizeGuideRows')`, mapping each row's `values` to add/
  remove the one entry, and writing the result back with
  `setValue('sizeGuideRows', updatedRows)` — so the two field arrays
  always stay in sync, in the same handler that adds/removes the
  column itself.

Follow `CategoriesPage.tsx`'s exact structural pattern (header, toggle
form, skeleton, empty state, table) for the outer page shape; this file
additionally needs the variant/size-guide sub-editors inside the form.
Validate with:

```typescript
const productSchema = z.object({
  name: z.string().min(1, 'نام محصول الزامی است.'),
  slug: z.string().min(1, 'نامک الزامی است.'),
  description: z.string(),
  categoryId: z.string().min(1, 'دسته‌بندی الزامی است.'),
  basePrice: z.coerce.number().positive('قیمت پایه باید مثبت باشد.'),
  compareAtPrice: z.coerce.number().nullable(),
  isActive: z.boolean(),
  variants: z.array(z.object({
    color: z.string().min(1),
    size: z.string().min(1),
    sku: z.string().min(1),
    stockQuantity: z.coerce.number().int().min(0),
    priceOverride: z.coerce.number().nullable(),
  })).min(1, 'حداقل یک تنوع رنگ/سایز الزامی است.'),
  sizeGuideColumns: z.array(z.string()),
  sizeGuideRows: z.array(z.object({ sizeLabel: z.string(), values: z.array(z.string()) })),
})
type ProductFormValuesInput = z.infer<typeof productSchema>
```

This mirrors `ProductFormValues` from `shopCatalogTypes.ts` exactly.

## 5. Add the two Shop admin entries to `ShellNav.tsx`

Edit `src/web/src/components/shell/ShellNav.tsx`. It currently declares
`navItems` starting with:

```typescript
const navItems: ShellNavItem[] = [
  { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard, href: '/dashboard', platform: true },
  { id: 'tenants', label: 'مستأجران', icon: Building2, href: '/platform/tenants', platform: true },
  { id: 'members', label: 'اعضای مستأجر', icon: UsersRound, href: '/t/', tenantScopedSuffix: '' },
```

Add two tenant-scoped entries right after `members`, using the same
`tenantScopedSuffix` convention (the real href becomes
`/t/:tenantId` + the suffix) and the `ShoppingBag` icon from
`lucide-react` (already an installed dependency):

```typescript
  { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories' },
  { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products' },
```

Add `ShoppingBag` to the existing `lucide-react` import at the top of
the file. It currently reads:

```typescript
import { Building2, IdCard, KeyRound, LayoutDashboard, MailPlus, ScrollText, ShieldCheck, Shield, Users, UsersRound } from 'lucide-react'
```

Change it to:

```typescript
import { Building2, IdCard, KeyRound, LayoutDashboard, MailPlus, ScrollText, ShieldCheck, Shield, ShoppingBag, Users, UsersRound } from 'lucide-react'
```

## 6. Add the two routes to `App.tsx`

Edit `src/web/src/App.tsx`. It currently has, inside
`<Route element={<ProtectedLayout />}>`:

```tsx
        <Route path="/t/:tenantId" element={<TenantScopePage />} />
        <Route path="/t/:tenantId/roles" element={<RolesPage />} />
        <Route path="/t/:tenantId/invitations" element={<InvitationsPage />} />
        <Route path="/t/:tenantId/audit" element={<AuditLogPage />} />
```

Add two more routes right after the `audit` line, matching the exact
same unwrapped pattern (no `RequirePlatformAdmin` — these are ordinary
tenant-member destinations, gated server-side by
`ShopAuthorization.AuthorizeTenantAccessAsync`, same as `roles`/
`invitations`/`audit`):

```tsx
        <Route path="/t/:tenantId/shop/categories" element={<CategoriesPage />} />
        <Route path="/t/:tenantId/shop/products" element={<ProductsPage />} />
```

Add the two matching imports near the top of the file, alongside the
existing page imports (e.g. right after the `RolesPage` import line):

```tsx
import { CategoriesPage } from './pages/shop/admin/CategoriesPage'
import { ProductsPage } from './pages/shop/admin/ProductsPage'
```

# Non-goals

- No connection to a real API — B026 does not exist yet (F029 connects
  this page once it does).
- No storefront-facing page (that is F030) — this task is the
  authenticated admin side only.
- No image upload UI for product photos — out of scope for this task and
  this slice (B026 carries no image field).

# Acceptance

- Category and product list/create/edit work fully against mocked data,
  including the size-guide table editor's column/row synchronization
  described above.
- Every required state (idle, loading, empty, validation failure,
  success feedback) is implemented for both pages.
- The pages render inside `DashboardShell` exactly like other admin
  pages, reusing its header, `SecondaryButton` icon controls and, if
  already delivered, `StatePanel`.
- No new design token; every visual choice reuses existing tokens/
  components.

# Verification

Automated:

```bash
cd src/web
npm run build
npm run lint
```

Manual, in a real browser, at 1440×900, 1024×768 and 390×844:

- Create a category, create a product with at least two variants and a
  two-column size guide, add a third column and confirm every existing
  row gains a matching empty cell, remove a column and confirm its
  values disappear from every row, edit an existing product and confirm
  the form loads its current variants/size guide correctly.
- No browser console error.

# Lifecycle

Add row `F028` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F025`, and Spec link
`tasks/front/F028-admin-catalog-management-mock.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/026-shop-catalog.md` is the permanent record and is
never deleted.
