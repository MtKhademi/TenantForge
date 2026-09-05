import type { ReactNode } from 'react'
import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { SessionLoadingScreen } from './components/shell/SessionLoadingScreen'
import { useAuth } from './features/auth/AuthContext'
import { TenantScopeProvider } from './features/tenants/TenantScopeContext'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
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

function RedirectHome() {
  const { status } = useAuth()
  if (status === 'bootstrapping') return <SessionLoadingScreen />
  return <Navigate to={status === 'authenticated' ? '/dashboard' : '/login'} replace />
}

/**
 * S07 layout boundary: the tenant scope provider is mounted once around all
 * protected routes so the shared tenant list (header switcher + tenants page)
 * is fetched once per session and survives navigation. It represents
 * selection only — never authorization.
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
      <Route path="/" element={<RedirectHome />} />
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedLayout />}>
        <Route path="/dashboard" element={<DashboardPage />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="/platform/tenants" element={<TenantsPage />} />
        <Route path="/t/:slug" element={<TenantScopePage />} />
      </Route>
      <Route path="*" element={<RedirectHome />} />
    </Routes>
  )
}
