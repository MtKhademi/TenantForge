import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

type TooltipProps = {
  label: string
  /** Stable id used to link the label to the trigger via aria-describedby. */
  id: string
  children: ReactNode
  className?: string
}

/**
 * TenantForge tooltip: a short hover/focus label for icon-only controls.
 *
 * - Opens on pointer hover and keyboard focus, closes when the pointer leaves
 *   or focus moves away.
 * - Positioning uses only logical properties (`inset-inline-start: 100%`,
 *   `margin-inline-start`), so for a rail that sits on the inline-start side
 *   the bubble always opens toward the content — correct in both LTR and RTL.
 * - The label is kept in the accessibility tree and linked with
 *   `aria-describedby`, so it is announced even when visually hidden.
 */
export function Tooltip({ label, id, children, className }: TooltipProps) {
  return (
    <span className={cn('group/tt relative block', className)}>
      {children}
      <span
        role="tooltip"
        id={id}
        className="pointer-events-none absolute inset-y-0 start-full z-50 ms-2 flex items-center rounded-md border border-border bg-surface px-2.5 py-1.5 text-xs font-medium text-foreground opacity-0 shadow-soft transition-opacity duration-150 ease-admin group-hover/tt:opacity-100 group-focus-visible/tt:opacity-100 motion-reduce:transition-none"
      >
        {label}
      </span>
    </span>
  )
}
