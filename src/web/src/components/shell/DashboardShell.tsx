import type { ReactNode } from 'react'
import { Building2, Loader2, LogOut, Menu, Moon, PanelRightClose, PanelRightOpen, Sun, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { TenantSwitcher } from '@/components/shell/TenantSwitcher'
import { SecondaryButton } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/AuthContext'
import { useTheme } from '@/features/theme/ThemeContext'
import { useTenantScope } from '@/features/tenants/TenantScopeContext'
import { cn } from '@/lib/utils'
import { ShellNav } from '@/components/shell/ShellNav'

/**
 * S02 application shell (post F005).
 *
 * Layout contract:
 * - The right-side (inline-start) sidebar is the single source of layout width.
 *   Expanded and collapsed widths are design tokens (`--sidebar-width`,
 *   `--sidebar-collapsed-width`) and the content wrapper's inline-start
 *   padding is driven from the same pair, so header and main always reflow
 *   into exactly the space the rail leaves — no hardcoded 272/80 pairs that
 *   can drift.
 * - Both the rail width and the content padding transition together with one
 *   shared duration/easing, so a toggle is one coherent motion, never two
 *   out-of-sync ones.
 * - On narrow viewports the rail is removed entirely; navigation moves to a
 *   drawer (Escape closes it, focus is managed) instead of forcing an icon
 *   rail into mobile widths.
 */
export function DashboardShell({ children }: { children: ReactNode }) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const { session, isSigningOut, signOut } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const { tenants } = useTenantScope()
  const location = useLocation()
  const navigate = useNavigate()

  // The URL is the source of truth for the active tenant; resolve it here
  // only to surface it in the header. Selection never grants access.
  const activeTenant = (() => {
    if (!location.pathname.startsWith('/t/')) return null
    const segment = location.pathname.split('/')[2]
    if (!segment) return null
    const slug = decodeURIComponent(segment)
    return tenants?.find((tenant) => tenant.slug === slug) ?? null
  })()

  const menuButtonRef = useRef<HTMLButtonElement | null>(null)
  const drawerCloseRef = useRef<HTMLButtonElement | null>(null)

  async function handleSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  function closeDrawer() {
    setDrawerOpen(false)
  }

  // Drawer keyboard + focus contract: Escape closes; opening moves focus into
  // the dialog, closing returns it to the button that opened it.
  useEffect(() => {
    if (!drawerOpen) return

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') closeDrawer()
    }
    document.addEventListener('keydown', onKeyDown)
    // Move focus into the dialog once the DOM has updated.
    const frame = requestAnimationFrame(() => drawerCloseRef.current?.focus())
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      cancelAnimationFrame(frame)
      menuButtonRef.current?.focus()
    }
  }, [drawerOpen])

  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside
        id="application-sidebar"
        className={cn(
          'fixed inset-y-0 start-0 z-30 hidden border-e border-border bg-sidebar text-sidebar-foreground transition-[width] duration-(--motion-duration) ease-admin lg:block',
          sidebarCollapsed ? 'w-sidebar-collapsed' : 'w-sidebar',
        )}
      >
        <ShellNav collapsed={sidebarCollapsed} />
      </aside>

      <div
        className={cn(
          'min-h-screen transition-[padding-inline-start] duration-(--motion-duration) ease-admin lg:ps-sidebar',
          sidebarCollapsed && 'lg:ps-sidebar-collapsed',
        )}
      >
        <header className="sticky top-0 z-20 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
          <div className="flex min-h-16 items-center justify-between gap-3 px-4 md:px-6">
            <div className="flex items-center gap-3">
              <SecondaryButton
                type="button"
                aria-label="باز کردن ناوبری"
                aria-haspopup="dialog"
                className="px-3 lg:hidden"
                onClick={() => setDrawerOpen(true)}
                ref={(node) => {
                  menuButtonRef.current = node
                }}
              >
                <Menu aria-hidden="true" className="size-4" />
              </SecondaryButton>
              <SecondaryButton
                type="button"
                aria-label={sidebarCollapsed ? 'باز کردن نوار کناری' : 'کوچک کردن نوار کناری'}
                aria-controls="application-sidebar"
                aria-expanded={!sidebarCollapsed}
                className="hidden px-3 lg:inline-flex"
                onClick={() => setSidebarCollapsed((value) => !value)}
              >
                {sidebarCollapsed ? (
                  <PanelRightOpen aria-hidden="true" className="size-4" />
                ) : (
                  <PanelRightClose aria-hidden="true" className="size-4" />
                )}
              </SecondaryButton>
              <div>
                <p className="flex items-center gap-2 text-xs font-semibold tracking-[0.08em] text-muted-foreground">
                  TenantForge
                  {activeTenant && (
                    <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-[11px] font-semibold text-primary">
                      <Building2 aria-hidden="true" className="size-3" />
                      <bdi>{activeTenant.name}</bdi>
                    </span>
                  )}
                </p>
                <h1 className="text-lg font-semibold tracking-tight">داشبورد</h1>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <TenantSwitcher />
              <button
                type="button"
                aria-label={theme === 'dark' ? 'تغییر به پوسته روشن' : 'تغییر به پوسته تیره'}
                className="inline-flex min-h-10 items-center gap-2 rounded-md border border-border bg-surface px-3 text-sm font-medium hover:bg-muted"
                onClick={toggleTheme}
              >
                {theme === 'dark' ? <Sun aria-hidden="true" className="size-4" /> : <Moon aria-hidden="true" className="size-4" />}
                <span className="hidden sm:inline">{theme === 'dark' ? 'روشن' : 'تیره'}</span>
              </button>
              <div className="hidden text-end sm:block">
                <p className="text-sm font-semibold"><bdi>{session?.user.displayName}</bdi></p>
                <p className="text-xs text-muted-foreground"><bdi>{session?.user.email}</bdi></p>
              </div>
              <SecondaryButton
                type="button"
                aria-label={isSigningOut ? 'در حال خروج' : 'خروج'}
                className="px-3"
                disabled={isSigningOut}
                onClick={handleSignOut}
              >
                {isSigningOut ? (
                  <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
                ) : (
                  <LogOut aria-hidden="true" className="size-4" />
                )}
              </SecondaryButton>
            </div>
          </div>
        </header>

        <main className="mx-auto w-full max-w-content px-4 py-8 md:px-6 lg:py-10">{children}</main>
      </div>

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="ناوبری موبایل">
          <button
            className="absolute inset-0 bg-slate-950/45"
            aria-label="بستن ناوبری"
            type="button"
            onClick={closeDrawer}
          />
          <div className="relative h-full w-[min(22rem,86vw)] border-e border-border bg-sidebar text-sidebar-foreground shadow-raised">
            <div className="flex items-center justify-between border-b border-border p-4">
              <span className="text-sm font-semibold tracking-[0.08em]">TenantForge</span>
              <SecondaryButton
                type="button"
                aria-label="بستن ناوبری"
                className="px-3"
                onClick={closeDrawer}
                ref={(node) => {
                  drawerCloseRef.current = node
                }}
              >
                <X aria-hidden="true" className="size-4" />
              </SecondaryButton>
            </div>
            <ShellNav />
          </div>
        </div>
      )}
    </div>
  )
}
