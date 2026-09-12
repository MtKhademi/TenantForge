import { Building2, IdCard, KeyRound, LayoutDashboard, MailPlus, ScrollText, ShieldCheck, Shield, Users, UsersRound } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useMemo } from 'react'
import { Link, useLocation, useMatch } from 'react-router-dom'
import { Tooltip } from '@/components/ui/Tooltip'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import { cn } from '@/lib/utils'

type ShellNavItem = {
  id: string
  label: string
  icon: LucideIcon
  /** Real destination, or a placeholder anchor for future slices. */
  href: string
  placeholder?: boolean
  /**
   * S11 (F018): a platform-only destination (داشبورد، مستأجران،
   * کاربران پلتفرم، هویت پلتفرم). Gated by `isPlatformAdmin`, never by
   * tenant permission keys. S16 (F023): shown only in **platform scope** —
   * inside a tenant the nav exposes the tenant destinations only, and an
   * admin enters platform scope explicitly through the header switcher, so
   * the platform directory links are never visible from within a tenant.
   */
  platform?: boolean
  /**
   * Tenant-scoped destination: the real href is `/t/:tenantId` + this
   * suffix; an empty suffix is the tenant root, i.e. the member page.
   * Outside a tenant the item is an inert placeholder, like the remaining
   * named destinations.
   */
  tenantScopedSuffix?: string
}

/**
 * The destinations the shell exposes so far. S16 (F023) makes the nav
 * scope-aware: the tenant items — اعضای مستأجر، نقش‌ها، دعوت‌ها، گزارش
 * فعالیت — keep the URL's tenant id and are the only real destinations
 * inside a tenant; the platform items (admin-only) appear only in platform
 * scope. هویت پلتفرم and وضعیت امنیتی remain inert placeholders for later
 * slices.
 */
const navItems: ShellNavItem[] = [
  { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard, href: '/dashboard', platform: true },
  { id: 'tenants', label: 'مستأجران', icon: Building2, href: '/platform/tenants', platform: true },
  { id: 'members', label: 'اعضای مستأجر', icon: UsersRound, href: '/t/', tenantScopedSuffix: '' },
  { id: 'roles', label: 'نقش‌ها', icon: KeyRound, href: '/t/', tenantScopedSuffix: '/roles' },
  { id: 'invitations', label: 'دعوت‌ها', icon: MailPlus, href: '/t/', tenantScopedSuffix: '/invitations' },
  { id: 'audit', label: 'گزارش فعالیت', icon: ScrollText, href: '/t/', tenantScopedSuffix: '/audit' },
  { id: 'users', label: 'کاربران پلتفرم', icon: Users, href: '/users', platform: true },
  { id: 'identity', label: 'هویت پلتفرم', icon: IdCard, href: '#identity', placeholder: true, platform: true },
  { id: 'security', label: 'وضعیت امنیتی', icon: Shield, href: '#security', placeholder: true, platform: true },
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
 * drawer so both stay in sync. S16 (F023) makes it scope-aware:
 *
 * - the URL is the only source of scope. `useMatch` on the real tenant route
 *   yields the current tenant id; platform and tenant destination sets are
 *   derived from it, never from stored state.
 * - inside a tenant, the nav shows the tenant destinations (اعضای مستأجر،
 *   نقش‌ها، دعوت‌ها، گزارش فعالیت) with the URL's tenant id preserved; an
 *   admin sees the platform items only in platform scope, entered
 *   explicitly through the header switcher.
 * - active state is **exact route matching**: `/t/{id}` highlights only
 *   members, each child highlights only its own item, and the platform
 *   مستأجران entry is never current on `/t/...`. At most one visible item
 *   per navigation instance carries `aria-current="page"`.
 * - items navigate through the router (`Link`) — no document reload;
 *   placeholders stay inert anchors.
 *
 * - Every item keeps a meaningful icon in every mode (no dot placeholders).
 * - In collapsed mode the visible text is hidden, but the accessible name is
 *   preserved via `aria-label` and a Persian tooltip is provided.
 * - The active route is shown with a pill plus an inline-start indicator bar,
 *   which is the logical "first" edge in RTL.
 */
export function ShellNav({ collapsed = false }: ShellNavProps) {
  const location = useLocation()
  const { isPlatformAdmin } = useTenantScope()
  // Matched-route tenant id — `useMatch` keeps this in sync with the router,
  // so rapid navigation, reload and Back/Forward always re-derive the scope.
  const tenantMatch = useMatch('/t/:tenantId/*')
  const inTenantScope = Boolean(tenantMatch)
  const rawTenantId = tenantMatch?.params.tenantId ?? ''
  const tenantId = inTenantScope && rawTenantId.length > 0 ? decodeURIComponent(rawTenantId) : ''

  // S11 (F018) + S16 (F023): platform destinations are shown only to a
  // platform administrator (`isPlatformAdmin`, never tenant permission keys)
  // and only in platform scope. Tenant-scoped items resolve against the
  // URL's tenant id and are inert placeholders outside a tenant.
  const visibleItems = useMemo(() => {
    return navItems.filter(
      (item) =>
        !item.platform || (isPlatformAdmin && !inTenantScope),
    )
  }, [isPlatformAdmin, inTenantScope])

  return (
    <nav className="flex h-full flex-col gap-6 p-4" aria-label="ناوبری اصلی">
      <BrandRow collapsed={collapsed} />

      <ul className="space-y-1">
        {visibleItems.map((item) => {
          const scopedAvailable =
            item.tenantScopedSuffix !== undefined && inTenantScope && tenantId.length > 0
          const resolvedHref = scopedAvailable
            ? `/t/${encodeURIComponent(tenantId)}${item.tenantScopedSuffix}`
            : item.href
          // S16 (F023): a tenant-scoped item (اعضای مستأجر، نقش‌ها، …) is a real,
          // active link only while the URL's tenant id makes it available;
          // outside a tenant it is an inert placeholder — discoverable but not
          // navigable — exactly as the platform placeholders are (F018 parity).
          // `item.placeholder` additionally marks the always-inert platform
          // items (هویت پلتفرم، وضعیت امنیتی).
          const placeholder =
            !scopedAvailable &&
            (Boolean(item.placeholder) || item.tenantScopedSuffix !== undefined)
          // Exact match only: the member item matches `/t/{id}` itself, each
          // child item its own child route, and platform items their own
          // routes. No prefix matches, so the platform tenant-list entry
          // never masquerades as the current page inside a tenant.
          const active = !placeholder && location.pathname === resolvedHref
          const Icon = item.icon
          const link = (
            <Link
              key={item.id}
              to={placeholder ? item.href : resolvedHref}
              aria-label={collapsed ? item.label : undefined}
              aria-describedby={collapsed ? `${item.id}-tooltip` : undefined}
              aria-current={active ? 'page' : undefined}
              onClick={(event) => {
                if (placeholder) {
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
                placeholder && 'cursor-default opacity-60',
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
            </Link>
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
