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
