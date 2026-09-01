import type { ReactNode } from 'react'
import { Loader2, LogOut, Menu, Moon, PanelRightClose, PanelRightOpen, ShieldCheck, Sun, X } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SecondaryButton } from '@/components/ui/Button'
import { useAuth } from '@/features/auth/AuthContext'
import { useTheme } from '@/features/theme/ThemeContext'
import { cn } from '@/lib/utils'

const navItems = ['داشبورد', 'هویت پلتفرم', 'وضعیت امنیتی']

export function DashboardShell({ children }: { children: ReactNode }) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const { session, isSigningOut, signOut } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const navigate = useNavigate()

  async function handleSignOut() {
    await signOut()
    navigate('/login', { replace: true })
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside
        className={cn(
          'fixed inset-y-0 start-0 z-30 hidden border-e border-border bg-sidebar text-sidebar-foreground transition-[width] duration-200 ease-admin lg:block',
          sidebarCollapsed ? 'w-20' : 'w-sidebar',
        )}
      >
        <ShellNav collapsed={sidebarCollapsed} />
      </aside>

      <div
        className={cn(
          'min-h-screen transition-[padding-inline-start] duration-200 ease-admin lg:ps-sidebar',
          sidebarCollapsed && 'lg:ps-20',
        )}
      >
        <header className="sticky top-0 z-20 border-b border-border bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
          <div className="flex min-h-16 items-center justify-between gap-3 px-4 md:px-6">
            <div className="flex items-center gap-3">
              <SecondaryButton
                type="button"
                aria-label="باز کردن ناوبری"
                className="px-3 lg:hidden"
                onClick={() => setDrawerOpen(true)}
              >
                <Menu aria-hidden="true" className="size-4" />
              </SecondaryButton>
              <SecondaryButton
                type="button"
                aria-label={sidebarCollapsed ? 'باز کردن نوار کناری' : 'کوچک کردن نوار کناری'}
                className="hidden px-3 lg:inline-flex"
                onClick={() => setSidebarCollapsed((value) => !value)}
              >
                {sidebarCollapsed ? <PanelRightOpen aria-hidden="true" className="size-4" /> : <PanelRightClose aria-hidden="true" className="size-4" />}
              </SecondaryButton>
              <div>
                <p className="text-xs font-semibold tracking-[0.08em] text-muted-foreground">TenantForge</p>
                <h1 className="text-lg font-semibold tracking-tight">داشبورد</h1>
              </div>
            </div>
            <div className="flex items-center gap-2">
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
          <button className="absolute inset-0 bg-slate-950/45" aria-label="بستن ناوبری" type="button" onClick={() => setDrawerOpen(false)} />
          <div className="relative h-full w-[min(22rem,86vw)] border-e border-border bg-sidebar text-sidebar-foreground shadow-raised">
            <div className="flex items-center justify-between border-b border-border p-4">
              <span className="text-sm font-semibold tracking-[0.08em]">TenantForge</span>
              <SecondaryButton type="button" aria-label="بستن ناوبری" className="px-3" onClick={() => setDrawerOpen(false)}>
                <X aria-hidden="true" className="size-4" />
              </SecondaryButton>
            </div>
            <ShellNav collapsed={false} />
          </div>
        </div>
      )}
    </div>
  )
}

function ShellNav({ collapsed }: { collapsed: boolean }) {
  return (
    <nav className="flex h-full flex-col gap-6 p-4" aria-label="ناوبری اصلی">
      <div className="flex items-center gap-3">
        <span className="inline-flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary text-primary-foreground">
          <ShieldCheck aria-hidden="true" className="size-5" />
        </span>
        {!collapsed && (
          <div>
            <p className="text-sm font-semibold">TenantForge</p>
            <p className="text-xs text-muted-foreground">پایه هویت SaaS</p>
          </div>
        )}
      </div>
      <div className="space-y-1">
        {navItems.map((item, index) => (
          <a
            key={item}
            href="#welcome"
            className={cn(
              'flex min-h-10 items-center rounded-md px-3 text-sm font-medium transition-colors hover:bg-muted',
              index === 0 && 'bg-muted text-foreground',
              collapsed && 'justify-center px-2',
            )}
            aria-current={index === 0 ? 'page' : undefined}
          >
            <span className="inline-block size-2 rounded-full bg-primary" aria-hidden="true" />
            {!collapsed && <span className="ms-3">{item}</span>}
          </a>
        ))}
      </div>
      {!collapsed && (
        <div className="mt-auto rounded-lg border border-border bg-surface p-3 text-xs text-muted-foreground">
          نشست API توسعه فقط برای بررسی رفتار تازه‌سازی در همین زبانه مرورگر ذخیره می‌شود.
        </div>
      )}
    </nav>
  )
}
