import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it } from 'vitest'
import App from './App'
import { AuthProvider } from './features/auth/AuthContext'
import { ThemeProvider } from './features/theme/ThemeContext'

function renderApp(initialEntry = '/login') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <ThemeProvider>
        <AuthProvider>
          <App />
        </AuthProvider>
      </ThemeProvider>
    </MemoryRouter>,
  )
}

describe('S00 auth shell', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    window.localStorage.clear()
  })

  it('shows client-side validation errors before checking mock credentials', async () => {
    const user = userEvent.setup()
    renderApp()

    await user.click(screen.getByRole('button', { name: /open dashboard/i }))

    expect(await screen.findByText('Email is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
  })

  it('shows invalid mock credentials separately from validation', async () => {
    const user = userEvent.setup()
    renderApp()

    await user.type(screen.getByLabelText(/email/i), 'wrong@tenantforge.local')
    await user.type(screen.getByLabelText(/password/i), 'not-the-password')
    await user.click(screen.getByRole('button', { name: /open dashboard/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('mock credentials are not recognized')
  })

  it('navigates to the dashboard after successful mock sign-in', async () => {
    const user = userEvent.setup()
    renderApp()

    await user.type(screen.getByLabelText(/email/i), 'admin@tenantforge.local')
    await user.type(screen.getByLabelText(/password/i), 'local-development-password')
    await user.click(screen.getByRole('button', { name: /open dashboard/i }))

    expect(await screen.findByRole('heading', { name: /tenantforge is ready/i })).toBeInTheDocument()
  })

  it('signs out and returns to login', async () => {
    const user = userEvent.setup()
    renderApp()

    await user.type(screen.getByLabelText(/email/i), 'admin@tenantforge.local')
    await user.type(screen.getByLabelText(/password/i), 'local-development-password')
    await user.click(screen.getByRole('button', { name: /open dashboard/i }))
    await screen.findByRole('heading', { name: /tenantforge is ready/i })

    await user.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeInTheDocument()
  })
})
