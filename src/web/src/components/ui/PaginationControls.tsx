import { ChevronLeft, ChevronRight } from 'lucide-react'
import { PAGE_SIZE_OPTIONS, type PageSizeOption, type PaginationMeta } from '@/features/pagination/paginationTypes'
import { cn } from '@/lib/utils'

/**
 * S15 (F022): the one reusable pagination control every table/list uses —
 * previous/next, current/total pages, the visible "N–M از Z" range and a
 * page-size selector. RTL: "next" (later pages) is the chevron pointing
 * toward reading-start in RTL, i.e. visually left (`ChevronLeft`); "previous"
 * is `ChevronRight`. Both carry accessible Persian labels regardless of the
 * icon direction.
 *
 * `totalPages === 0` (no matches at all) is shown honestly — no "صفحه ۱ از ۰"
 * and no fabricated row range — by hiding the page indicator and range and
 * disabling both navigation buttons.
 */
export type PaginationControlsProps = {
  meta: PaginationMeta
  disabled?: boolean
  onPageChange: (pageNumber: number) => void
  onPageSizeChange: (pageSize: PageSizeOption) => void
  /** Distinguishes each control's accessible names when a page hosts more than one (RolesPage). */
  label?: string
}

function toPersianDigits(value: number): string {
  return new Intl.NumberFormat('fa-IR').format(value)
}

export function PaginationControls({
  meta,
  disabled = false,
  onPageChange,
  onPageSizeChange,
  label,
}: PaginationControlsProps) {
  const { pageNumber, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage } = meta
  const hasRows = totalCount > 0
  const rangeStart = hasRows ? (pageNumber - 1) * pageSize + 1 : 0
  const rangeEnd = hasRows ? Math.min(pageNumber * pageSize, totalCount) : 0
  const prefix = label ? `${label}: ` : ''

  return (
    <div
      className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-sm"
      role="group"
      aria-label={label ? `صفحه‌بندی ${label}` : 'صفحه‌بندی'}
    >
      <p className="text-muted-foreground">
        {hasRows ? (
          <>
            {prefix}نمایش <bdi>{toPersianDigits(rangeStart)}</bdi> تا <bdi>{toPersianDigits(rangeEnd)}</bdi> از{' '}
            <bdi>{toPersianDigits(totalCount)}</bdi>
          </>
        ) : (
          <>{prefix}موردی برای نمایش نیست</>
        )}
      </p>

      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-2 text-xs">
          <span className="text-muted-foreground">تعداد در صفحه</span>
          <select
            aria-label={label ? `تعداد ردیف در هر صفحه — ${label}` : 'تعداد ردیف در هر صفحه'}
            value={pageSize}
            disabled={disabled}
            onChange={(event) => onPageSizeChange(Number(event.target.value) as PageSizeOption)}
            className="min-h-9 rounded-md border border-input bg-surface px-2 py-1 text-xs text-foreground transition-colors focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60"
          >
            {PAGE_SIZE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {toPersianDigits(option)}
              </option>
            ))}
          </select>
        </label>

        <div className="flex items-center gap-1">
          <button
            type="button"
            aria-label={label ? `صفحه قبل — ${label}` : 'صفحه قبل'}
            disabled={disabled || !hasPreviousPage}
            onClick={() => onPageChange(pageNumber - 1)}
            className={cn(
              'inline-flex size-9 items-center justify-center rounded-md border border-border bg-surface text-foreground transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-40',
            )}
          >
            <ChevronRight aria-hidden="true" className="size-4" />
          </button>

          <span className="min-w-16 px-1 text-center text-xs text-muted-foreground" aria-live="polite">
            {totalPages > 0 ? (
              <>
                صفحه <bdi>{toPersianDigits(pageNumber)}</bdi> از <bdi>{toPersianDigits(totalPages)}</bdi>
              </>
            ) : (
              '—'
            )}
          </span>

          <button
            type="button"
            aria-label={label ? `صفحه بعد — ${label}` : 'صفحه بعد'}
            disabled={disabled || !hasNextPage}
            onClick={() => onPageChange(pageNumber + 1)}
            className={cn(
              'inline-flex size-9 items-center justify-center rounded-md border border-border bg-surface text-foreground transition-colors hover:bg-muted disabled:pointer-events-none disabled:opacity-40',
            )}
          >
            <ChevronLeft aria-hidden="true" className="size-4" />
          </button>
        </div>
      </div>
    </div>
  )
}
