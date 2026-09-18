import type {
  StorefrontCategory,
  StorefrontProductDetail,
  StorefrontProductSummary,
} from './storefrontTypes'

/**
 * S26 storefront mock catalog (F030; F031 swaps this for B027's real,
 * anonymous public API). The read shape matches B027's
 * `StorefrontContracts.cs` field-for-field (camelCased) so the later swap is a
 * pure data-source change.
 *
 * The dataset is intentionally small but covers every state the acceptance
 * criteria require:
 * - 2 categories with 2–3 products each;
 * - a product with a two-column size guide (پیراهن کلاسیک);
 * - a product with NO size guide (پیراهن لینن) so the guide section is simply
 *   absent, never an empty table;
 * - multiple colors/sizes per product so the variant selectors are meaningful;
 * - at least one variant with `stockQuantity: 0` (per-combination sold-out).
 *
 * `imageUrl` / `imageUrls` are mock-only (not part of B027's real response).
 * They are rendered as neutral placeholder tiles — never as fake photos — so
 * the gallery and its lightbox are exercised without inventing content or a
 * new dependency. F031 replaces the placeholders with real `<img>` sources.
 *
 * Error sentinel (mock-only, so the retryable error state is demonstrable in a
 * real browser): any call where `tenantId === 'error'` rejects. This disappears
 * with the mock in F031, where real HTTP failures (5xx/401) drive the same
 * state.
 */

function ensureNotErrorTenant(tenantId: string) {
  if (tenantId === 'error') {
    throw new Error('خطا در بارگذاری کاتالوگ (نمونه)')
  }
}

const CATEGORIES: StorefrontCategory[] = [
  { id: 'cat-1', name: 'پیراهن', slug: 'shirts', displayOrder: 0 },
  { id: 'cat-2', name: 'شلوار', slug: 'pants', displayOrder: 1 },
]

const PRODUCTS: StorefrontProductDetail[] = [
  {
    id: 'prod-1',
    categoryId: 'cat-1',
    name: 'پیراهن کلاسیک',
    slug: 'classic-shirt',
    description: 'پیراهن نخی کلاسیک با دوخت تمیز برای استفاده روزمره.',
    basePrice: 890000,
    compareAtPrice: 1200000,
    variants: [
      { id: 'var-cs-sf-m', color: 'سفید', size: 'M', stockQuantity: 10, effectivePrice: 890000 },
      { id: 'var-cs-sf-l', color: 'سفید', size: 'L', stockQuantity: 0, effectivePrice: 890000 },
      { id: 'var-cs-sf-xl', color: 'سفید', size: 'XL', stockQuantity: 4, effectivePrice: 890000 },
      { id: 'var-cs-ab-m', color: 'آبی', size: 'M', stockQuantity: 7, effectivePrice: 890000 },
      { id: 'var-cs-ab-l', color: 'آبی', size: 'L', stockQuantity: 0, effectivePrice: 890000 },
      { id: 'var-cs-ab-xl', color: 'آبی', size: 'XL', stockQuantity: 3, effectivePrice: 890000 },
    ],
    sizeGuideColumns: [
      { id: 'col-cs-1', name: 'دور سینه', displayOrder: 0 },
      { id: 'col-cs-2', name: 'دور کمر', displayOrder: 1 },
    ],
    sizeGuideRows: [
      { sizeLabel: 'M', displayOrder: 0, cells: [{ columnId: 'col-cs-1', value: '96' }, { columnId: 'col-cs-2', value: '80' }] },
      { sizeLabel: 'L', displayOrder: 1, cells: [{ columnId: 'col-cs-1', value: '102' }, { columnId: 'col-cs-2', value: '86' }] },
      { sizeLabel: 'XL', displayOrder: 2, cells: [{ columnId: 'col-cs-1', value: '108' }, { columnId: 'col-cs-2', value: '92' }] },
    ],
    imageUrls: ['img-1', 'img-2', 'img-3'],
  },
  {
    id: 'prod-2',
    categoryId: 'cat-1',
    name: 'پیراهن لینن',
    slug: 'linen-shirt',
    description: 'پیراهن لینن سبک و خنک، مناسب فصل گرما.',
    basePrice: 1150000,
    compareAtPrice: null,
    variants: [
      { id: 'var-ls-kr-m', color: 'کرم', size: 'M', stockQuantity: 5, effectivePrice: 1150000 },
      { id: 'var-ls-kr-l', color: 'کرم', size: 'L', stockQuantity: 2, effectivePrice: 1150000 },
      { id: 'var-ls-sb-m', color: 'سبز', size: 'M', stockQuantity: 0, effectivePrice: 1150000 },
      { id: 'var-ls-sb-l', color: 'سبز', size: 'L', stockQuantity: 6, effectivePrice: 1150000 },
    ],
    sizeGuideColumns: [],
    sizeGuideRows: [],
    imageUrls: ['img-1', 'img-2'],
  },
  {
    id: 'prod-3',
    categoryId: 'cat-1',
    name: 'پیراهن اکسفورد',
    slug: 'oxford-shirt',
    description: 'پیراهن اکسفورد رسمی برای محیط کار.',
    basePrice: 780000,
    compareAtPrice: 900000,
    variants: [
      { id: 'var-os-sf-m', color: 'سفید', size: 'M', stockQuantity: 8, effectivePrice: 780000 },
      { id: 'var-os-sf-l', color: 'سفید', size: 'L', stockQuantity: 5, effectivePrice: 780000 },
      { id: 'var-os-ms-m', color: 'مشکی', size: 'M', stockQuantity: 4, effectivePrice: 780000 },
      { id: 'var-os-ms-l', color: 'مشکی', size: 'L', stockQuantity: 0, effectivePrice: 780000 },
    ],
    sizeGuideColumns: [
      { id: 'col-os-1', name: 'دور سینه', displayOrder: 0 },
      { id: 'col-os-2', name: 'دور کمر', displayOrder: 1 },
    ],
    sizeGuideRows: [
      { sizeLabel: 'M', displayOrder: 0, cells: [{ columnId: 'col-os-1', value: '95' }, { columnId: 'col-os-2', value: '78' }] },
      { sizeLabel: 'L', displayOrder: 1, cells: [{ columnId: 'col-os-1', value: '101' }, { columnId: 'col-os-2', value: '84' }] },
    ],
    imageUrls: ['img-1', 'img-2', 'img-3'],
  },
  {
    id: 'prod-4',
    categoryId: 'cat-2',
    name: 'شلوار چینوس',
    slug: 'chinos',
    description: 'شلوار چینوس روزمره با پارچه مقاوم.',
    basePrice: 1020000,
    compareAtPrice: null,
    variants: [
      { id: 'var-ch-xy-32', color: 'خاکی', size: '32', stockQuantity: 6, effectivePrice: 1020000 },
      { id: 'var-ch-xy-34', color: 'خاکی', size: '34', stockQuantity: 9, effectivePrice: 1020000 },
      { id: 'var-ch-xy-36', color: 'خاکی', size: '36', stockQuantity: 2, effectivePrice: 1020000 },
      { id: 'var-ch-ab-32', color: 'آبی', size: '32', stockQuantity: 7, effectivePrice: 1020000 },
      { id: 'var-ch-ab-34', color: 'آبی', size: '34', stockQuantity: 0, effectivePrice: 1020000 },
      { id: 'var-ch-ab-36', color: 'آبی', size: '36', stockQuantity: 4, effectivePrice: 1020000 },
    ],
    sizeGuideColumns: [
      { id: 'col-ch-1', name: 'دور کمر', displayOrder: 0 },
      { id: 'col-ch-2', name: 'طول پا', displayOrder: 1 },
    ],
    sizeGuideRows: [
      { sizeLabel: '32', displayOrder: 0, cells: [{ columnId: 'col-ch-1', value: '80' }, { columnId: 'col-ch-2', value: '100' }] },
      { sizeLabel: '34', displayOrder: 1, cells: [{ columnId: 'col-ch-1', value: '84' }, { columnId: 'col-ch-2', value: '102' }] },
      { sizeLabel: '36', displayOrder: 2, cells: [{ columnId: 'col-ch-1', value: '88' }, { columnId: 'col-ch-2', value: '104' }] },
    ],
    imageUrls: ['img-1', 'img-2'],
  },
  {
    id: 'prod-5',
    categoryId: 'cat-2',
    name: 'شلوار جین',
    slug: 'jeans',
    description: 'شلوار جین استاندارد با فرم مستقیم.',
    basePrice: 1350000,
    compareAtPrice: 1500000,
    variants: [
      { id: 'var-jn-sm-32', color: 'سرمه‌ای', size: '32', stockQuantity: 12, effectivePrice: 1350000 },
      { id: 'var-jn-sm-34', color: 'سرمه‌ای', size: '34', stockQuantity: 5, effectivePrice: 1350000 },
      { id: 'var-jn-ms-32', color: 'مشکی', size: '32', stockQuantity: 3, effectivePrice: 1350000 },
      { id: 'var-jn-ms-34', color: 'مشکی', size: '34', stockQuantity: 0, effectivePrice: 1350000 },
    ],
    sizeGuideColumns: [
      { id: 'col-jn-1', name: 'دور کمر', displayOrder: 0 },
      { id: 'col-jn-2', name: 'طول پا', displayOrder: 1 },
    ],
    sizeGuideRows: [
      { sizeLabel: '32', displayOrder: 0, cells: [{ columnId: 'col-jn-1', value: '80' }, { columnId: 'col-jn-2', value: '100' }] },
      { sizeLabel: '34', displayOrder: 1, cells: [{ columnId: 'col-jn-1', value: '84' }, { columnId: 'col-jn-2', value: '102' }] },
    ],
    imageUrls: ['img-1', 'img-2', 'img-3'],
  },
]

function toSummary(product: StorefrontProductDetail): StorefrontProductSummary {
  return {
    id: product.id,
    name: product.name,
    slug: product.slug,
    effectivePrice: product.basePrice,
    compareAtPrice: product.compareAtPrice,
    imageUrl: '',
  }
}

export const mockStorefrontCatalog = {
  async listCategories(tenantId: string): Promise<StorefrontCategory[]> {
    ensureNotErrorTenant(tenantId)
    // A simulated network delay makes the loading state observable in a real
    // browser without adding a dependency.
    await new Promise((resolve) => setTimeout(resolve, 350))
    return [...CATEGORIES]
  },

  async listProducts(tenantId: string, categorySlug: string): Promise<StorefrontProductSummary[]> {
    ensureNotErrorTenant(tenantId)
    await new Promise((resolve) => setTimeout(resolve, 350))
    const category = CATEGORIES.find((c) => c.slug === categorySlug)
    if (!category) return []
    return PRODUCTS.filter((p) => p.categoryId === category.id)
      .sort((a, b) => a.name.localeCompare(b.name, 'fa'))
      .map(toSummary)
  },

  async getProduct(tenantId: string, productSlug: string): Promise<StorefrontProductDetail | null> {
    ensureNotErrorTenant(tenantId)
    await new Promise((resolve) => setTimeout(resolve, 350))
    return PRODUCTS.find((p) => p.slug === productSlug) ?? null
  },
}
