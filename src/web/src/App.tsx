import type { ReactNode } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './features/auth/AuthContext'

function RequireSession({ children }: { children: ReactNode }) {
  const { session } = useAuth()
  return session ? children : <Navigate to="/login" replace />
}

function RedirectHome() {
  const { session } = useAuth()
  return <Navigate to={session ? '/dashboard' : '/login'} replace />
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
