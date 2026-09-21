import { z } from 'zod'
import { paginationSchema } from './shopContract'

/**
 * S33 storefront discovery — B037 wire contract (F045 mock phase; F055 binds HTTP).
 *
 * Field-for-field with the live `B037` backend Spec and
 * `docs/design/shop/http-contracts.md` §S33/B037: camelCase JSON member names,
 * exact enum strings, exact nullability and the shared six-field Shop pagination
 * metadata. No UI-only member is added to the wire types; the mock client
 * satisfies these schemas and the later HTTP client will parse through the same
 * schemas.
 */
export const discoverySortSchema = z.enum(['newest', 'price-asc', 'price-desc', 'name'])
export type DiscoverySort = z.infer<typeof discoverySortSchema>

/**
 * The query object every Shop discovery client method accepts. It is the
 * frontend's typed mirror of the B037 query-string parameters — it is not a
 * wire response member, so it is not itself a Zod schema.
 */
export type DiscoveryQuery = {
  q: string
  categorySlug: string | null
  sort: DiscoverySort
  saleOnly: boolean
  pageNumber: number
  pageSize: number
}

export const storefrontProductSummarySchema = z.object({
  id: z.string(),
  name: z.string(),
  slug: z.string(),
  effectivePrice: z.number(),
  compareAtPrice: z.number().nullable(),
  isOnSale: z.boolean(),
  isSoldOut: z.boolean(),
  thumbnailUrl: z.string().nullable(),
})
export type StorefrontProductSummary = z.infer<typeof storefrontProductSummarySchema>

export const storefrontProductListSchema = z.object({
  products: z.array(storefrontProductSummarySchema),
  pagination: paginationSchema,
})
export type StorefrontProductList = z.infer<typeof storefrontProductListSchema>
