/**
 * S15 server pagination (F022) — the shared contract every business
 * collection response now carries alongside its existing array key (B015).
 *
 * The request side (`pageNumber`/`pageSize`) and response side
 * (`PaginationMeta`) are intentionally the smallest shared shape: callers
 * still own their own array key (`users`, `tenants`, `members`, `roles`,
 * `invitations`, `events`) — this module never renames or wraps those.
 */

/** One-based page request. Every list/selector adapter accepts this pair. */
export type PaginationQuery = {
  pageNumber: number
  pageSize: number
}

/**
 * The `pagination` sibling object every paged response now returns
 * (docs/design/s15-list-pagination.md). `totalCount` is always the number of
 * matching, authorized rows before paging — never the page length.
 */
export type PaginationMeta = {
  pageNumber: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

/** Offered page sizes, smallest first. */
export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const
export type PageSizeOption = (typeof PAGE_SIZE_OPTIONS)[number]

/** New table views default to 20 per the S15 contract. */
export const DEFAULT_PAGE_SIZE: PageSizeOption = 20
export const DEFAULT_PAGE_NUMBER = 1

export function isPageSizeOption(value: number): value is PageSizeOption {
  return (PAGE_SIZE_OPTIONS as readonly number[]).includes(value)
}

/** Strict parse: never trust a half-shaped `pagination` object from the API. */
export function parsePaginationMeta(payload: unknown): PaginationMeta {
  if (typeof payload !== 'object' || payload === null) {
    throw new Error('Invalid pagination metadata')
  }
  const meta = payload as Record<string, unknown>
  if (
    typeof meta.pageNumber !== 'number' ||
    !Number.isInteger(meta.pageNumber) ||
    typeof meta.pageSize !== 'number' ||
    !Number.isInteger(meta.pageSize) ||
    typeof meta.totalCount !== 'number' ||
    !Number.isInteger(meta.totalCount) ||
    typeof meta.totalPages !== 'number' ||
    !Number.isInteger(meta.totalPages) ||
    typeof meta.hasPreviousPage !== 'boolean' ||
    typeof meta.hasNextPage !== 'boolean'
  ) {
    throw new Error('Invalid pagination metadata')
  }
  return {
    pageNumber: meta.pageNumber,
    pageSize: meta.pageSize,
    totalCount: meta.totalCount,
    totalPages: meta.totalPages,
    hasPreviousPage: meta.hasPreviousPage,
    hasNextPage: meta.hasNextPage,
  }
}

/** Builds the `pageNumber`/`pageSize` query string segment for an adapter path. */
export function appendPaginationParams(params: URLSearchParams, query: PaginationQuery): void {
  params.set('pageNumber', String(query.pageNumber))
  params.set('pageSize', String(query.pageSize))
}

/**
 * S15 (F022): a mutation (delete/expiry) can leave the currently requested
 * page beyond the new `totalPages`. Returns the bounded recovery page
 * (`max(1, totalPages)`), or `null` when the current page is still valid —
 * callers only refetch when this returns non-null, avoiding a refetch loop
 * under continuing concurrent changes.
 */
export function recoveryPageNumber(meta: PaginationMeta): number | null {
  if (meta.totalPages === 0) return meta.pageNumber > 1 ? 1 : null
  if (meta.pageNumber > meta.totalPages) return meta.totalPages
  return null
}
