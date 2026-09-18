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
