import type { ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { SessionLoadingScreen } from './components/shell/SessionLoadingScreen'
import { useAuth } from './features/auth/AuthContext'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'

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

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<RedirectHome />} />
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/dashboard"
        element={
          <RequireSession>
            <DashboardPage />
          </RequireSession>
        }
      />
      <Route path="*" element={<RedirectHome />} />
    </Routes>
  )
}
