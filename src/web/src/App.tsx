import { Lock } from 'lucide-react'
import type { ReactNode } from 'react'
import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { DashboardShell } from './components/shell/DashboardShell'
import { SessionLoadingScreen } from './components/shell/SessionLoadingScreen'
import { SecondaryButton } from './components/ui/Button'
import { useAuth } from './features/auth/AuthContext'
import { TenantScopeProvider, useTenantScope } from './features/tenants/TenantScopeContext'
import { AuditLogPage } from './pages/AuditLogPage'
import { DashboardPage } from './pages/DashboardPage'
import { InvitationsPage } from './pages/InvitationsPage'
import { LoginPage } from './pages/LoginPage'
import { RolesPage } from './pages/RolesPage'
import { TenantHome } from './pages/TenantHome'
import { TenantScopePage } from './pages/TenantScopePage'
import { TenantsPage } from './pages/TenantsPage'
import { UsersPage } from './pages/UsersPage'

/**
 * S02 protected-route boundary:
 *
 * - while a stored session is being verified (`bootstrapping`) it renders a
 *   full-screen loading state — protected content is never mounted first;
 * - a confirmed session renders the page;
 * - a missing or rejected session redirects to login (`replace`, so the
 *   back button does not loop back through the redirect).
 */
function RequireSession({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  if (status === 'bootstrapping') return <SessionLoadingScreen />
  if (status === 'unauthenticated') return <Navigate to="/login" replace />
  return children
}

/**
 * S11 platform boundary (F018): platform-only destinations are gated by
 * `isPlatformAdmin` — never by tenant permission keys. An ordinary account
 * that reaches `/dashboard`, `/users` or `/platform/tenants` directly is shown
 * a designed in-shell denial instead of mounting the page and triggering a
 * platform API `403`. Navigation is presentation only: the server denies the
 * underlying request independently (B012).
 */
function RequirePlatformAdmin({ children }: { children: ReactNode }) {
  const { isPlatformAdmin } = useTenantScope()
  if (isPlatformAdmin) return <>{children}</>
  return <PlatformAccessDenied />
}

/** In-shell denial for a non-admin on a platform destination. */
function PlatformAccessDenied() {
  const { selectHome } = useTenantScope()
  return (
    <DashboardShell>
      <section aria-label="دسترسی مجاز نیست" className="space-y-6">
        <div className="rounded-xl border border-destructive/40 bg-destructive/10 p-6 shadow-soft" role="alert">
          <div className="flex items-start gap-4">
            <span className="inline-flex size-12 shrink-0 items-center justify-center rounded-lg bg-destructive/15 text-destructive">
              <Lock aria-hidden="true" className="size-6" />
            </span>
            <div className="space-y-1.5">
              <p className="text-sm font-semibold">دسترسی به این بخش مجاز نیست</p>
              <p className="text-sm leading-6 text-muted-foreground">
                این صفحه فقط برای مدیر پلتفرم در دسترس است. حساب فعلی دسترسی مدیریت پلتفرم را
                ندارد؛ انتخاب آدرس مرورگر این محدودیت را دور نمی‌زند.
              </p>
              <SecondaryButton type="button" className="mt-4" onClick={selectHome}>
                بازگشت به مستأجران من
              </SecondaryButton>
            </div>
          </div>
        </div>
      </section>
    </DashboardShell>
  )
}

/**
 * S11 account landing (F018): the single, scope-aware entry point after login
 * or reload. A platform administrator goes to the platform dashboard; an
 * ordinary account goes to the in-shell membership chooser — never the
 * platform dashboard, which would trigger a `403`. Mounted inside
 * `ProtectedLayout`, so the session is verified and the tenant-scope provider
 * is available.
 */
function HomeRoute() {
  const { isPlatformAdmin } = useTenantScope()
  if (isPlatformAdmin) return <Navigate to="/dashboard" replace />
  return <TenantHome />
}

/** Unknown routes land on the account home (or login when signed out). */
function RedirectHome() {
  const { status } = useAuth()
  if (status === 'bootstrapping') return <SessionLoadingScreen />
  return <Navigate to={status === 'authenticated' ? '/' : '/login'} replace />
}

/**
 * S07 layout boundary: the tenant scope provider is mounted once around all
 * protected routes so the shared scope list (header switcher + tenants page +
 * membership chooser) is fetched once per session and survives navigation. It
 * represents selection only — never authorization.
 */
function ProtectedLayout() {
  return (
    <RequireSession>
      <TenantScopeProvider>
        <Outlet />
      </TenantScopeProvider>
    </RequireSession>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedLayout />}>
        <Route path="/" element={<HomeRoute />} />
        <Route
          path="/dashboard"
          element={<RequirePlatformAdmin><DashboardPage /></RequirePlatformAdmin>}
        />
        <Route
          path="/users"
          element={<RequirePlatformAdmin><UsersPage /></RequirePlatformAdmin>}
        />
        <Route
          path="/platform/tenants"
          element={<RequirePlatformAdmin><TenantsPage /></RequirePlatformAdmin>}
        />
        <Route path="/t/:tenantId" element={<TenantScopePage />} />
        <Route path="/t/:tenantId/roles" element={<RolesPage />} />
        <Route path="/t/:tenantId/invitations" element={<InvitationsPage />} />
        <Route path="/t/:tenantId/audit" element={<AuditLogPage />} />
      </Route>
      <Route path="*" element={<RedirectHome />} />
    </Routes>
  )
}
