import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { AuthProvider } from './features/auth/AuthContext'
import { ThemeProvider } from './features/theme/ThemeContext'

const VALID_EMAIL = 'admin@tenantforge.local'
const VALID_PASSWORD = 'local-development-password'

function loginResponse() {
  return {
    accessToken: 'signed-test-token',
    expiresAtUtc: '2030-01-01T00:00:00Z',
    user: {
      id: 'development-admin',
      email: VALID_EMAIL,
      displayName: 'Platform Administrator',
      isPlatformAdmin: true,
    },
  }
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

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

async function fillCredentialsAndSubmit(
  user: ReturnType<typeof userEvent.setup>,
  { email = VALID_EMAIL, password = VALID_PASSWORD } = {},
) {
  await user.type(screen.getByLabelText(/email/i), email)
  await user.type(screen.getByLabelText(/password/i), password)
  await user.click(screen.getByRole('button', { name: /sign in/i }))
}

describe('S01 development login API', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows client-side validation errors before calling the API', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    renderApp()

    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Email is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('keeps the user on login with accessible 401 feedback for wrong credentials', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Invalid credentials' }, 401)))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user, { password: 'not-the-password' })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The email or password is incorrect. Try again.')
    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /tenantforge is ready/i })).not.toBeInTheDocument()
  })

  it('shows an API-unavailable alert (not invalid credentials) when the request cannot complete', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The sign-in service is unavailable.')
    expect(alert).not.toHaveTextContent('email or password is incorrect')
  })

  it('shows an API-unavailable alert when the API answers with an unexpected error status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Boom' }, 500)))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The sign-in service is unavailable.')
    expect(alert).not.toHaveTextContent('email or password is incorrect')
  })

  it('signs in through the API, stores the signed session and reaches the dashboard', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(loginResponse()))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    expect(await screen.findByRole('heading', { name: /tenantforge is ready/i })).toBeInTheDocument()
    expect(screen.getByText(/signed in as platform administrator/i)).toBeInTheDocument()

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/login',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ email: VALID_EMAIL, password: VALID_PASSWORD }) }),
    )

    const stored = JSON.parse(window.sessionStorage.getItem('tenantforge:auth:session') ?? '{}')
    expect(stored.accessToken).toBe('signed-test-token')
    expect(stored.user.isPlatformAdmin).toBe(true)
  })

  it('restores a valid stored session after a page refresh', () => {
    const stored: Record<string, unknown> = {
      user: loginResponse().user,
      accessToken: 'signed-test-token',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))

    renderApp('/dashboard')

    expect(screen.getByRole('heading', { name: /tenantforge is ready/i })).toBeInTheDocument()
  })

  it('drops an expired stored session and requires a fresh sign-in', () => {
    const stored: Record<string, unknown> = {
      user: loginResponse().user,
      accessToken: 'expired-token',
      expiresAtUtc: '2020-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))

    renderApp('/dashboard')

    expect(screen.getByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(window.sessionStorage.getItem('tenantforge:auth:session')).toBeNull()
  })

  it('signs out and returns to login', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(loginResponse())))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)
    await screen.findByRole('heading', { name: /tenantforge is ready/i })

    await user.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(window.sessionStorage.getItem('tenantforge:auth:session')).toBeNull()
  })
})
