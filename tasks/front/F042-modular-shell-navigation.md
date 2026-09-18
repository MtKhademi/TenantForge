---
id: F042
slice: S31
title: Group ShellNav into labelled module sections
agent: frontend-mentor
source: tasks/slices/031-cross-module-permissions-and-nav.md
---

# Objective

`src/web/src/components/shell/ShellNav.tsx`'s `navItems` is one flat
array mixing IAM destinations and the two Shop destinations, with no
visual grouping — confirmed by reading the real, current file (quoted
in full below). This task is a **pure presentation restructure**: split
the flat array into two labelled `ShellNavSection`s, with no new item,
no href change, no permission-gating change, no behavioral change of
any kind. F043 (next, depends on this) adds the actual permission gate
to the two Shop items.

This Spec gives you the exact full rewritten file. Follow it literally.

# Context

Read `tasks/slices/031-cross-module-permissions-and-nav.md` completely.

The real, current `src/web/src/components/shell/ShellNav.tsx` is 244
lines. Its `navItems` array (lines 52–64) is:

```tsx
const navItems: ShellNavItem[] = [
  { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard, href: '/dashboard', platform: true },
  { id: 'tenants', label: 'مستأجران', icon: Building2, href: '/platform/tenants', platform: true },
  { id: 'members', label: 'اعضای مستأجر', icon: UsersRound, href: '/t/', tenantScopedSuffix: '' },
  { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories' },
  { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products' },
  { id: 'roles', label: 'نقش‌ها', icon: KeyRound, href: '/t/', tenantScopedSuffix: '/roles' },
  { id: 'invitations', label: 'دعوت‌ها', icon: MailPlus, href: '/t/', tenantScopedSuffix: '/invitations', requires: [INVITATIONS_VIEW_KEY] },
  { id: 'audit', label: 'گزارش فعالیت', icon: ScrollText, href: '/t/', tenantScopedSuffix: '/audit', requires: [AUDIT_VIEW_KEY] },
  { id: 'users', label: 'کاربران پلتفرم', icon: Users, href: '/users', platform: true },
  { id: 'identity', label: 'هویت پلتفرم', icon: IdCard, href: '#identity', placeholder: true, platform: true },
  { id: 'security', label: 'وضعیت امنیتی', icon: Shield, href: '#security', placeholder: true, platform: true },
]
```

and every downstream consumer — `visibleItems` (the `useMemo` filter)
and the `<ul>{visibleItems.map(...)}</ul>` render loop — reads a flat
`ShellNavItem[]`, with no notion of a group today.

`docs/design-system.md` has no existing "section heading" token
(checked before writing this Spec — do not invent one). This task
composes a section heading from the design system's existing
typographic/color utilities already used elsewhere in this same file
(`text-xs text-muted-foreground`, the same pairing the session-notice
box at the bottom of this file already uses), not a new component.

# Scope

Replace `src/web/src/components/shell/ShellNav.tsx` entirely with:

```tsx
import { Building2, IdCard, KeyRound, LayoutDashboard, MailPlus, ScrollText, ShieldCheck, Shield, ShoppingBag, Users, UsersRound } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import { useMemo } from 'react'
import { Link, useLocation, useMatch } from 'react-router-dom'
import { Tooltip } from '@/components/ui/Tooltip'
import { useTenantPermissions } from '@/features/roles/tenantPermissions'
import { AUDIT_VIEW_KEY, INVITATIONS_VIEW_KEY, type PermissionKey } from '@/features/roles/roleTypes'
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
  /**
   * S12 (F019): the server-resolved tenant permission keys this destination
   * requires. While the resolved set is loading or any key is missing, the
   * item stays visible but **inert** (aria-disabled, no navigation) — an
   * honest affordance, never a security boundary: B013 denies the
   * underlying request with 403 when the URL is reached directly.
   */
  requires?: PermissionKey[]
}

/**
 * S31 (F042): a labelled group of destinations, rendered as its own
 * heading + item list. Purely a presentation grouping — it carries no
 * behavior of its own; every `ShellNavItem` inside it keeps working
 * exactly as it did in the previous flat array.
 */
type ShellNavSection = {
  id: string
  label: string
  items: ShellNavItem[]
}

/**
 * S31 (F042): the destinations grouped by owning module, so the sidebar
 * visually separates "هویت و دسترسی" (platform + IAM tenant destinations)
 * from "فروشگاه" (Shop tenant destinations). S16 (F023) still governs
 * scope-awareness item-by-item inside each section; S12 (F019) still
 * governs `requires`-gating item-by-item. Grouping here changes neither —
 * only how the same items are laid out.
 */
const navSections: ShellNavSection[] = [
  {
    id: 'identity',
    label: 'هویت و دسترسی',
    items: [
      { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard, href: '/dashboard', platform: true },
      { id: 'tenants', label: 'مستأجران', icon: Building2, href: '/platform/tenants', platform: true },
      { id: 'members', label: 'اعضای مستأجر', icon: UsersRound, href: '/t/', tenantScopedSuffix: '' },
      { id: 'roles', label: 'نقش‌ها', icon: KeyRound, href: '/t/', tenantScopedSuffix: '/roles' },
      { id: 'invitations', label: 'دعوت‌ها', icon: MailPlus, href: '/t/', tenantScopedSuffix: '/invitations', requires: [INVITATIONS_VIEW_KEY] },
      { id: 'audit', label: 'گزارش فعالیت', icon: ScrollText, href: '/t/', tenantScopedSuffix: '/audit', requires: [AUDIT_VIEW_KEY] },
      { id: 'users', label: 'کاربران پلتفرم', icon: Users, href: '/users', platform: true },
      { id: 'identity-directory', label: 'هویت پلتفرم', icon: IdCard, href: '#identity', placeholder: true, platform: true },
      { id: 'security', label: 'وضعیت امنیتی', icon: Shield, href: '#security', placeholder: true, platform: true },
    ],
  },
  {
    id: 'shop',
    label: 'فروشگاه',
    items: [
      { id: 'shop-categories', label: 'دسته‌بندی‌های فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/categories' },
      { id: 'shop-products', label: 'محصولات فروشگاه', icon: ShoppingBag, href: '/t/', tenantScopedSuffix: '/shop/products' },
    ],
  },
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
 *
 * S31 (F042): destinations are grouped into labelled `ShellNavSection`s
 * ("هویت و دسترسی", "فروشگاه") purely for visual clarity — a section
 * heading is hidden in collapsed mode (there is no room for it), exactly
 * as item labels are hidden in collapsed mode. Every item's own
 * scope/permission/active-state behavior above is untouched: this only
 * changes how the same flat list of items is laid out.
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

  // S12 (F019): the server-resolved permission set for the current tenant
  // gates the invitation and audit destinations. It only resolves inside a
  // tenant; in platform scope it stays null and the platform items are
  // ungated here (they are `isPlatformAdmin`-gated instead).
  const resolved = useTenantPermissions(inTenantScope ? tenantId : undefined)

  // S11 (F018) + S16 (F023): platform destinations are shown only to a
  // platform administrator (`isPlatformAdmin`, never tenant permission keys)
  // and only in platform scope. Tenant-scoped items resolve against the
  // URL's tenant id and are inert placeholders outside a tenant. S31 (F042):
  // filtering now runs per-section, then sections with zero visible items
  // are dropped entirely so no empty heading is ever shown.
  const visibleSections = useMemo(() => {
    return navSections
      .map((section) => ({
        ...section,
        items: section.items.filter(
          (item) => !item.platform || (isPlatformAdmin && !inTenantScope),
        ),
      }))
      .filter((section) => section.items.length > 0)
  }, [isPlatformAdmin, inTenantScope])

  return (
    <nav className="flex h-full flex-col gap-6 p-4" aria-label="ناوبری اصلی">
      <BrandRow collapsed={collapsed} />

      <div className="space-y-6">
        {visibleSections.map((section) => (
          <div key={section.id}>
            {!collapsed && (
              <p className="mb-2 px-3 text-xs font-medium text-muted-foreground">
                {section.label}
              </p>
            )}
            <ul className="space-y-1">
              {section.items.map((item) => {
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
                // S12 (F019): a destination with required tenant permissions is inert
                // while the server-resolved set is loading, failed or missing the
                // keys. Hiding/disabling is presentation only — B013 denies the
                // underlying read with a non-leaking 403 when the URL is reached
                // directly.
                const permissionGated =
                  Boolean(item.requires?.length) &&
                  inTenantScope &&
                  resolved.permissions !== null &&
                  !item.requires!.every((key) => resolved.permissions?.has(key) ?? false)
                const permissionPending =
                  Boolean(item.requires?.length) && inTenantScope && resolved.isResolving
                const inert = placeholder || permissionGated || permissionPending
                // Exact match only: the member item matches `/t/{id}` itself, each
                // child item its own child route, and platform items their own
                // routes. No prefix matches, so the platform tenant-list entry
                // never masquerades as the current page inside a tenant.
                const active = !inert && location.pathname === resolvedHref
                const Icon = item.icon
                const link = (
                  <Link
                    key={item.id}
                    to={inert ? item.href : resolvedHref}
                    aria-label={collapsed ? item.label : undefined}
                    aria-describedby={collapsed ? `${item.id}-tooltip` : undefined}
                    aria-current={active ? 'page' : undefined}
                    aria-disabled={inert || undefined}
                    onClick={(event) => {
                      if (inert) {
                        // Keep the named item discoverable without navigating to a
                        // destination the current tenant permissions do not grant.
                        event.preventDefault()
                      }
                    }}
                    className={cn(
                      'relative flex min-h-10 items-center rounded-md text-sm font-medium transition-colors hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring',
                      active
                        ? 'bg-muted text-foreground'
                        : 'text-sidebar-foreground/80 hover:text-foreground',
                      inert && 'cursor-default opacity-60',
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
          </div>
        ))}
      </div>

      {!collapsed && (
        <div className="mt-auto rounded-lg border border-border bg-surface p-3 text-xs text-muted-foreground">
          نشست شما فقط در همین زبانه نگهداری می‌شود و با خروج یا بستن مرورگر پایان می‌یابد.
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
```

What changed, precisely: the flat `navItems` array became two
`navSections` entries; `visibleItems` became `visibleSections` (same
filter predicate, now applied per-section, with empty sections dropped);
the `<ul>` render loop is now nested one level inside a `<div>` per
section with an optional heading `<p>` (hidden when `collapsed`, exactly
like item labels already are). The `identity` placeholder item's `id`
changed from `'identity'` to `'identity-directory'` only to avoid
colliding with the new section id `'identity'` — this is an internal
React `key`/lookup value never rendered or read by any other file
(confirmed by `grep -rn "'identity'" src/web/src` before this change —
no other file references this item id string). Every prop, every href,
every `requires` array, every icon, every class name, every aria
attribute is otherwise identical to the file before this task.

# Non-goals

- No new nav item, no href change, no icon change, no new permission key
  (F043, next).
- No new design-system token — the section heading reuses
  `text-xs text-muted-foreground` already used in this same file.
- No change to `useTenantPermissions`, `TenantScopeContext`, or any
  other file.

# Acceptance

- The rendered nav shows exactly the same destinations, in exactly the
  same relative order within each group, as before this task — only
  visually grouped under two headings.
- Collapsed rail and mobile drawer both render correctly with no
  section heading shown (no layout shift, no orphaned empty section).
- `npm run build` and `npm run lint` in `src/web/` pass.

# Verification

```bash
cd src/web && npm run lint && npm run build
```

Manual, real browser: at 1440×900 (expanded desktop rail) and 390×844
(mobile drawer), screenshot the nav before and after this change while
signed in as a tenant Owner inside a tenant — confirm every existing
destination is present, unchanged, now under "هویت و دسترسی" or
"فروشگاه"; then screenshot the collapsed desktop rail and confirm icons
and tooltips are unchanged. No new browser console error.

# Lifecycle

Add row `F042` to the Front queue in `tasks/TASKS.md` with status
`planned`, dependency `F029`, and Spec link
`tasks/front/F042-modular-shell-navigation.md`.

Keep this full Spec while the task is `planned`, `in_progress` or
`review`. After final delivery approval, change the ledger row to `done`,
replace its Spec cell with `—`, and delete this exact file in the same
commit. `tasks/slices/031-cross-module-permissions-and-nav.md` is the
permanent record and is never deleted.
