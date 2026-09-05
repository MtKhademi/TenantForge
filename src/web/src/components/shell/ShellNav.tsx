import { Building2, IdCard, LayoutDashboard, ShieldCheck, Shield, Users } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useLocation } from 'react-router-dom'
import { Tooltip } from '@/components/ui/Tooltip'
import { cn } from '@/lib/utils'

type ShellNavItem = {
  id: string
  label: string
  icon: LucideIcon
  /** Real destination, or a placeholder anchor for future slices. */
  href: string
  placeholder?: boolean
  /**
   * Match a whole pathname prefix instead of an exact route (used by
   * مستأجران, which is active on the platform page and inside `/t/:slug`).
   */
  activePrefixes?: string[]
}

/**
 * The destinations the shell exposes so far. داشبورد, کاربران and مستأجران
 * are real routes (S02/S06/S07); the remaining items are named placeholders
 * for later slices, so they keep inert anchors and dimmed tooltips.
 */
const navItems: ShellNavItem[] = [
  { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard, href: '/dashboard' },
  { id: 'tenants', label: 'مستأجران', icon: Building2, href: '/platform/tenants', activePrefixes: ['/platform/tenants', '/t/'] },
  { id: 'users', label: 'کاربران', icon: Users, href: '/users' },
  { id: 'identity', label: 'هویت پلتفرم', icon: IdCard, href: '#identity', placeholder: true },
  { id: 'security', label: 'وضعیت امنیتی', icon: Shield, href: '#security', placeholder: true },
]

type ShellNavProps = {
  /**
   * Icon-rail mode: real icons only, Persian tooltips, compact active state.
   * Used by the collapsed desktop rail. The mobile drawer and the expanded
   * desktop rail always show icon + label.
   */
  collapsed?: boolean
}

/**
 * S02 application navigation, shared by the desktop rail and the mobile
 * drawer so both stay in sync.
 *
 * - Every item keeps a meaningful icon in every mode (no dot placeholders).
 * - In collapsed mode the visible text is hidden, but the accessible name is
 *   preserved via `aria-label` and a Persian tooltip is provided.
 * - The active route is shown with a pill plus an inline-start indicator bar,
 *   which is the logical "first" edge in RTL.
 */
export function ShellNav({ collapsed = false }: ShellNavProps) {
  const location = useLocation()

  return (
    <nav className="flex h-full flex-col gap-6 p-4" aria-label="ناوبری اصلی">
      <BrandRow collapsed={collapsed} />

      <ul className="space-y-1">
        {navItems.map((item) => {
          const active =
            !item.placeholder &&
            (item.activePrefixes
              ? item.activePrefixes.some((prefix) => location.pathname.startsWith(prefix))
              : location.pathname === item.href)
          const Icon = item.icon
          const link = (
            <a
              key={item.id}
              href={item.href}
              aria-label={collapsed ? item.label : undefined}
              aria-describedby={collapsed ? `${item.id}-tooltip` : undefined}
              aria-current={active ? 'page' : undefined}
              onClick={(event) => {
                if (item.placeholder) {
                  // Keep the named item discoverable without jumping to an
                  // anchor that does not exist in this slice.
                  event.preventDefault()
                }
              }}
              className={cn(
                'relative flex min-h-10 items-center rounded-md text-sm font-medium transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                active
                  ? 'bg-muted text-foreground'
                  : 'text-sidebar-foreground/80 hover:text-foreground',
                item.placeholder && 'cursor-default opacity-60',
                collapsed ? 'justify-center px-0' : 'px-3',
              )}
            >
              {/* Active indicator: a thin bar on the inline-start edge. */}
              {active && (
                <span
                  aria-hidden="true"
                  className="absolute inset-y-2 start-0 w-0.5 rounded-full bg-primary"
                />
              )}
              <Icon
                aria-hidden="true"
                className={cn('size-5 shrink-0', collapsed && 'mx-auto')}
              />
              {!collapsed && <span className="ms-3 truncate">{item.label}</span>}
            </a>
          )

          if (!collapsed) return <li key={item.id}>{link}</li>

          return (
            <li key={item.id}>
              <Tooltip label={item.label} id={`${item.id}-tooltip`} className="block">
                {link}
              </Tooltip>
            </li>
          )
        })}
      </ul>

      {!collapsed && (
        <div className="mt-auto rounded-lg border border-border bg-surface p-3 text-xs text-muted-foreground">
          نشست API توسعه فقط برای بررسی رفتار تازه‌سازی در همین زبانه مرورگر ذخیره می‌شود.
        </div>
      )}
    </nav>
  )
}

/**
 * Brand mark: the icon stays recognizable in compact mode; only the text
 * that cannot fit is hidden (not the mark itself).
 */
function BrandRow({ collapsed }: { collapsed: boolean }) {
  return (
    <div className={cn('flex items-center gap-3', collapsed && 'justify-center')}>
      <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary text-primary-foreground">
        <ShieldCheck aria-hidden="true" className="size-5" />
      </span>
      {!collapsed && (
        <div className="min-w-0">
          <p className="truncate text-sm font-semibold">TenantForge</p>
          <p className="truncate text-xs text-muted-foreground">پایه هویت SaaS</p>
        </div>
      )}
    </div>
  )
}
