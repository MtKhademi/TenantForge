/**
 * S26 storefront browsing — real, anonymous API data types (F031) for
 * B027's public catalog API. No Authorization header is ever sent anywhere
 * on this page family. Field-for-field with B027's `StorefrontContracts.cs`
 * (camelCased); the real responses carry no image fields.
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
}
