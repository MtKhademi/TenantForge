import { z } from 'zod'

/**
 * S32 product media — B036 wire contract (F044 mock phase; F054 binds HTTP).
 *
 * Field-for-field with `docs/design/shop/http-contracts.md` §B036 and the B036
 * backend Spec's `ProductMediaContracts.cs` (camelCase JSON, canonical
 * 13-character TSID strings, ISO-8601 UTC where a timestamp appears). These
 * Zod schemas are the single source of truth for the wire shape: the mock
 * client satisfies them and the later `httpShopMediaClient` parses through the
 * same ones. No UI-only member is added to any wire type.
 *
 * The B026 `ShopProduct` shape is defined here as `shopProductSchema`; the
 * existing `ShopProduct` type in `../shopCatalogTypes.ts` is re-exported as
 * `z.infer<typeof shopProductSchema>`, so there is exactly one product type
 * (no competing DTO).
 */

export const shopProductVariantSchema = z.object({
  id: z.string(),
  color: z.string(),
  size: z.string(),
  sku: z.string(),
  stockQuantity: z.number().int(),
  priceOverride: z.number().nullable(),
})
export const sizeGuideColumnSchema = z.object({
  id: z.string(),
  name: z.string(),
  displayOrder: z.number().int(),
})
export const sizeGuideCellSchema = z.object({
  columnId: z.string(),
  value: z.string(),
})
export const sizeGuideRowSchema = z.object({
  id: z.string(),
  sizeLabel: z.string(),
  displayOrder: z.number().int(),
  cells: z.array(sizeGuideCellSchema),
})
export const shopProductSchema = z.object({
  id: z.string(),
  tenantId: z.string(),
  categoryId: z.string(),
  name: z.string(),
  slug: z.string(),
  description: z.string(),
  basePrice: z.number(),
  compareAtPrice: z.number().nullable(),
  isActive: z.boolean(),
  variants: z.array(shopProductVariantSchema),
  sizeGuideColumns: z.array(sizeGuideColumnSchema),
  sizeGuideRows: z.array(sizeGuideRowSchema),
})

// B036 product media members (exact names/types/nullability from the contract).
export const productImageSchema = z.object({
  id: z.string(),
  altText: z.string(),
  displayOrder: z.number().int(),
  width: z.number().int(),
  height: z.number().int(),
  contentUrl: z.string(),
})
export type ProductImage = z.infer<typeof productImageSchema>

export const productGallerySchema = z.object({
  images: z.array(productImageSchema),
  galleryVersion: z.number().int(),
})
export type ProductGallery = z.infer<typeof productGallerySchema>

export const shopProductWithGallerySchema = shopProductSchema.extend({
  images: z.array(productImageSchema),
  galleryVersion: z.number().int(),
})
export type ShopProductWithGallery = z.infer<typeof shopProductWithGallerySchema>

// Re-exported by `shopCatalogTypes.ts` so the delivered B026 type name is now
// inferred from the schema (single source of truth).
export type ShopProduct = z.infer<typeof shopProductSchema>
export type ShopProductVariant = z.infer<typeof shopProductVariantSchema>
export type SizeGuideColumn = z.infer<typeof sizeGuideColumnSchema>
export type SizeGuideCell = z.infer<typeof sizeGuideCellSchema>
export type SizeGuideRow = z.infer<typeof sizeGuideRowSchema>

export type ReorderProductImagesRequest = {
  imageIds: string[]
  expectedGalleryVersion: number
}

/** B036 hard cap: a gallery holds at most eight images. */
export const MAX_GALLERY_IMAGES = 8
