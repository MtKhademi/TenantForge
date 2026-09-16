import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * F026: the one shared page-level state panel.
 *
 * Replaces the copy-pasted "X is unavailable / X is forbidden" alert markup
 * that previously lived independently in eight page files. The component owns
 * only the *wrapping* shape (tone surface, alignment, spacing, the
 * `role="alert"` landmark, and the icon / title / description / action
 * slots). It does NOT own the specific icon, the Persian copy, or the retry
 * handler — those stay on the caller so each page keeps its exact current
 * icon, wording and behavior. This is a presentation/structure change, not a
 * copy or behavior change.
 *
 * The rendered output must stay pixel-identical to the previous inline
 * markup, so the two real shapes found across the pages are both preserved:
 * - `density="compact"`: `p-5`, no shadow, smaller icon gap (`gap-3`) and
 *   tighter text spacing (`space-y-1`) — the
 *   Dashboard/Users/Tenants/TenantHome shape.
 * - `density="prominent"`: `p-6` + `shadow-soft`, larger icon gap (`gap-4`)
 *   and looser text spacing (`space-y-1.5`) — the
 *   Roles/Invitations/Audit/TenantScope shape.
 *
 * Callers pass their own icon node (a bare `size-5` icon for the compact
 * shape, or the boxed `size-12` icon-well for the prominent shape) and their
 * own action node (keeping its own `mt-*` spacing), so neither the icon
 * treatment nor the action placement is altered by the refactor. The icon is
 * rendered as a direct flex child — exactly as in the previous inline markup
 * — so per-icon margins such as `mt-0.5` keep applying. The surrounding
 * `<section>`/`<div>` `aria-label` (which varies per page) stays on the
 * caller, not inside this component.
 */
type StatePanelProps = {
  /** The page's own icon for this state (bare or boxed), passed as-is. */
  icon: ReactNode
  /** The panel title (the page's exact current wording). */
  title: string
  /** The panel description (the page's exact current wording). */
  description: ReactNode
  /** Optional retry / recovery action slot, rendered after the description. */
  action?: ReactNode
  /**
   * Panel tone. Only `destructive` is implemented today (the one tone every
   * current call site uses). The prop is accepted so a future `muted`/empty
   * tone can be added without changing call sites — it is not built
   * speculatively now.
   */
  tone?: 'destructive'
  /** The two real spacing shapes found across the pages (see above). */
  density?: 'compact' | 'prominent'
  /** Extra wrapper classes (e.g. a call site that needs a different padding). */
  className?: string
}

const TONE_SURFACE: Record<NonNullable<StatePanelProps['tone']>, string> = {
  destructive: 'border-destructive/40 bg-destructive/10',
}

export function StatePanel({
  icon,
  title,
  description,
  action,
  tone = 'destructive',
  density = 'compact',
  className,
}: StatePanelProps) {
  const prominent = density === 'prominent'
  return (
    <div
      role="alert"
      className={cn(
        'rounded-xl border',
        TONE_SURFACE[tone],
        prominent ? 'p-6 shadow-soft' : 'p-5',
        className,
      )}
    >
      <div className={cn('flex items-start', prominent ? 'gap-4' : 'gap-3')}>
        {icon}
        <div className={prominent ? 'space-y-1.5' : 'space-y-1'}>
          <p className="text-sm font-semibold">{title}</p>
          <p className="text-sm leading-6 text-muted-foreground">{description}</p>
          {action}
        </div>
      </div>
    </div>
  )
}
