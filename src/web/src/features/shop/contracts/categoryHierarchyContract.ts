import { z } from 'zod'

/**
 * S34 category hierarchy — B038 wire contract (F046 mock phase; F056 binds HTTP).
 *
 * Field-for-field with the live `B038` backend Spec and
 * `docs/design/shop/http-contracts.md` §S34/B038: camelCase JSON member names,
 * exact nullability. Admin rows are flat and carry `parentCategoryId`; the
 * public tree is nested exactly two levels deep (a category is either a root
 * or a direct child of a root — there is no third level). No UI-only member is
 * added to the wire types; the mock client satisfies these schemas and the
 * later HTTP client will parse through the same schemas.
 */
export const adminCategorySchema = z.object({
  id: z.string(),
  tenantId: z.string(),
  name: z.string(),
  slug: z.string(),
  displayOrder: z.number().int(),
  isActive: z.boolean(),
  parentCategoryId: z.string().nullable(),
})
export type AdminCategory = z.infer<typeof adminCategorySchema>

export type PublicCategory = {
  id: string
  name: string
  slug: string
  displayOrder: number
  children: PublicCategory[]
}
export const publicCategorySchema: z.ZodType<PublicCategory> = z.lazy(() =>
  z.object({
    id: z.string(),
    name: z.string(),
    slug: z.string(),
    displayOrder: z.number().int(),
    children: z.array(publicCategorySchema),
  }),
)

export type SaveCategoryRequest = {
  name: string
  slug: string
  displayOrder: number
  isActive: boolean
  parentCategoryId: string | null
}
