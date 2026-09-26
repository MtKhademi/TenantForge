import { Clock3 } from 'lucide-react'
import type { ReactNode } from 'react'

/**
 * S42 (B046) — the visible per-action countdown chip (F053).
 *
 * Rendered by a page next to (or under) the action that just received a 429,
 * while that action is disabled. It announces the remaining seconds live so a
 * screen reader and a sighted user both see the cooldown, and it is a distinct,
 * restrained warning tone — never a blocking modal, because ONLY the one
 * triggering action is disabled and the rest of the page stays usable.
 *
 * Direction-aware: it uses logical spacing only and a `dir="ltr"` tabular number
 * so the Persian sentence around the Latin digits stays RTL. Renders nothing
 * when `remainingSeconds` is `0` (the action is re-enabled; no stale chip lingers).
 */
export function RateLimitCountdown({
  remainingSeconds,
  actionLabel,
}: {
  remainingSeconds: number
  actionLabel?: string
}): ReactNode {
  if (remainingSeconds <= 0) return null
  return (
    <p
      role="status"
      aria-live="polite"
      className="inline-flex items-center gap-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-1.5 text-sm font-medium text-warning"
    >
      <Clock3 aria-hidden="true" className="size-4 shrink-0" />
      <span>
        {actionLabel ? `${actionLabel} موقتاً در دسترس نیست. ` : ''}
        لطفاً <bdi dir="ltr" className="tabular-nums font-semibold">{remainingSeconds}</bdi> ثانیه دیگر
        صبر کنید.
      </span>
    </p>
  )
}
