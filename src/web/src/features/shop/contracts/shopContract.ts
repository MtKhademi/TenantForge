import { z } from 'zod'

/**
 * Mirrors C# `PaginationMetadata` in
 * src/modules/shop/TenantForge.Modules.Shop/features/pagination/PaginationMetadata.cs
 * field for field. It has SIX fields, not four.
 */
export const paginationSchema = z.object({
  pageNumber: z.number().int(),
  pageSize: z.number().int(),
  totalCount: z.number().int(),
  totalPages: z.number().int(),
  hasPreviousPage: z.boolean(),
  hasNextPage: z.boolean(),
})
export type Pagination = z.infer<typeof paginationSchema>

/** A page request every paginated Shop client method takes. */
export type PageQuery = { pageNumber: number; pageSize: number }

/**
 * RFC7807 = the standard JSON error body ASP.NET Core returns for a failed
 * request (`status`, `type`, `title`, `detail`, optional per-field `errors`).
 * This is the ONLY error body shape any Shop client parses.
 */
export const shopProblemSchema = z.object({
  status: z.number().int(),
  type: z.string().optional(),
  title: z.string().optional(),
  detail: z.string().optional(),
  errors: z.record(z.string(), z.array(z.string())).optional(),
  retryAfterSeconds: z.number().int().positive().optional(),
})
export type ShopProblem = z.infer<typeof shopProblemSchema>

/** The ONE error type every Shop client throws. Never add a second one. */
export class ShopClientError extends Error {
  readonly problem: ShopProblem

  constructor(problem: ShopProblem) {
    super(problem.detail ?? problem.title ?? 'Shop request failed')
    this.name = 'ShopClientError'
    this.problem = problem
  }
}
