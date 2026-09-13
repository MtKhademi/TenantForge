import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  DEFAULT_PAGE_NUMBER,
  DEFAULT_PAGE_SIZE,
  isPageSizeOption,
  type PageSizeOption,
} from './paginationTypes'

/**
 * S15 (F022): keeps one table's `pageNumber`/`pageSize` in the route query
 * string so reload and browser Back/Forward restore the active page. A
 * `prefix` (e.g. `"member"`) lets one page host two independent paginated
 * lists (`RolesPage`'s role list and its member-assignment table) without
 * their query keys colliding.
 *
 * Invalid or missing URL values fall back to the defaults **before** any
 * request is made — a malformed `?page=abc` never reaches the adapter.
 */
export type UrlPageState = {
  pageNumber: number
  pageSize: PageSizeOption
  /** Navigate to a specific page (kept within `[1, ∞)`; the caller clamps against totalPages). */
  setPageNumber(page: number): void
  setPageSize(size: PageSizeOption): void
  /** Reset to page 1, keeping the current page size. Used on filter/tenant/size changes. */
  resetToPage1(): void
}

function paramNames(prefix?: string) {
  return {
    page: prefix ? `${prefix}Page` : 'page',
    size: prefix ? `${prefix}Size` : 'size',
  }
}

function parsePageNumber(raw: string | null): number {
  if (raw === null) return DEFAULT_PAGE_NUMBER
  const parsed = Number.parseInt(raw, 10)
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : DEFAULT_PAGE_NUMBER
}

function parsePageSize(raw: string | null): PageSizeOption {
  if (raw === null) return DEFAULT_PAGE_SIZE
  const parsed = Number.parseInt(raw, 10)
  return Number.isInteger(parsed) && isPageSizeOption(parsed) ? parsed : DEFAULT_PAGE_SIZE
}

export function useUrlPageState(prefix?: string): UrlPageState {
  const [searchParams, setSearchParams] = useSearchParams()
  const { page: pageKey, size: sizeKey } = paramNames(prefix)

  const pageNumber = parsePageNumber(searchParams.get(pageKey))
  const pageSize = parsePageSize(searchParams.get(sizeKey))

  const setPageNumber = useCallback(
    (page: number) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          next.set(pageKey, String(Math.max(1, Math.trunc(page))))
          return next
        },
        { replace: false },
      )
    },
    [pageKey, setSearchParams],
  )

  const setPageSize = useCallback(
    (size: PageSizeOption) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          next.set(sizeKey, String(size))
          next.set(pageKey, String(DEFAULT_PAGE_NUMBER))
          return next
        },
        { replace: false },
      )
    },
    [pageKey, sizeKey, setSearchParams],
  )

  const resetToPage1 = useCallback(() => {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current)
        next.set(pageKey, String(DEFAULT_PAGE_NUMBER))
        return next
      },
      { replace: true },
    )
  }, [pageKey, setSearchParams])

  return useMemo(
    () => ({ pageNumber, pageSize, setPageNumber, setPageSize, resetToPage1 }),
    [pageNumber, pageSize, setPageNumber, setPageSize, resetToPage1],
  )
}
