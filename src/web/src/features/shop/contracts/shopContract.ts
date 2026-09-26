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

/**
 * S42 / B046 — the one generic rate-limit problem every over-limit Shop
 * request receives. It extends `shopProblemSchema` with the three members B046
 * fixes: `status` is always `429`, `type` is always `shop_rate_limit`, and
 * `retryAfterSeconds` is a positive integer (the same value as the
 * `Retry-After` response header — 1 in the delivered backend). A valid input
 * and an invalid input alike produce this identical body, so a caller can never
 * observe which input "really" existed. See the "S42 / B046" section of
 * `docs/design/shop/http-contracts.md`.
 */
export const rateLimitedProblemSchema = shopProblemSchema.extend({
  status: z.literal(429),
  type: z.literal('shop_rate_limit'),
  retryAfterSeconds: z.number().int().positive(),
})
export type RateLimitedProblem = z.infer<typeof rateLimitedProblemSchema>

/**
 * The B046 cooldown, in seconds. The delivered backend rejects an over-limit
 * request immediately (queue length 0) with a `Retry-After` of `1`; the mock
 * reuses the same value so the UI's countdown is honest and the reviewer sees a
 * visible, real pause rather than a multi-minute one.
 */
export const RATE_LIMITED_RETRY_AFTER_SECONDS = 1

/**
 * Narrows any parsed problem to the B046 rate-limit shape. Every Shop surface
 * (lookup, cart, checkout, order, payment) uses this ONE check to recognize a
 * 429 and drive its cooldown — there is no second rate-limit discriminator.
 */
export function isRateLimitedProblem(problem: ShopProblem): problem is RateLimitedProblem {
  return (
    problem.status === 429 &&
    problem.type === 'shop_rate_limit' &&
    typeof problem.retryAfterSeconds === 'number' &&
    Number.isInteger(problem.retryAfterSeconds) &&
    problem.retryAfterSeconds > 0
  )
}
