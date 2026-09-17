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
