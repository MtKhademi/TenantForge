import { render, screen, within } from '@testing-library/react'
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
  await user.type(screen.getByLabelText('ایمیل'), email)
  await user.type(screen.getByLabelText('رمز عبور'), password)
  await user.click(screen.getByRole('button', { name: 'ورود' }))
}

describe('F004 Persian RTL interface', () => {
  beforeEach(() => {
    window.sessionStorage.clear()
    window.localStorage.clear()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  // The `lang="fa"` / `dir="rtl"` document boundary lives in index.html and
  // is asserted live in the Playwright spec (e2e/s01.spec.ts); jsdom does
  // not parse the HTML shell, so no unit-level duplicate is added here.

  it('shows the session loading screen in Persian while bootstrapping', () => {
    const stored = {
      user: loginResponse().user,
      accessToken: 'signed-test-token',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))
    // Never resolves: the app must stay on the Persian loading screen.
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise(() => {})))

    renderApp('/dashboard')

    expect(screen.getByText('در حال بازیابی نشست شما')).toBeInTheDocument()
  })
})

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

    await user.click(screen.getByRole('button', { name: 'ورود' }))

    expect(await screen.findByText('ایمیل الزامی است.')).toBeInTheDocument()
    expect(screen.getByText('رمز عبور الزامی است.')).toBeInTheDocument()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('keeps the user on login with accessible 401 feedback for wrong credentials', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Invalid credentials' }, 401)))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user, { password: 'not-the-password' })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('ایمیل یا رمز عبور درست نیست. دوباره تلاش کنید.')
    expect(screen.getByRole('heading', { name: 'ورود' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /tenantforge/i })).not.toBeInTheDocument()
  })

  it('shows an API-unavailable alert (not invalid credentials) when the request cannot complete', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('سرویس ورود در دسترس نیست. اتصال را بررسی کنید و دوباره تلاش کنید.')
    expect(alert).not.toHaveTextContent('ایمیل یا رمز عبور درست نیست')
  })

  it('shows an API-unavailable alert when the API answers with an unexpected error status', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Boom' }, 500)))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('سرویس ورود در دسترس نیست. اتصال را بررسی کنید و دوباره تلاش کنید.')
    expect(alert).not.toHaveTextContent('ایمیل یا رمز عبور درست نیست')
  })

  it('signs in through the API, stores the signed session and reaches the dashboard', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(loginResponse()))
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)

    expect(await screen.findByRole('heading', { name: /tenantforge برای نخستین برش/i })).toBeInTheDocument()
    // The display name sits inside a <bdi> for correct bidi rendering.
    expect(within(screen.getByRole('main')).getByText('Platform Administrator', { selector: 'bdi' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'داشبورد' })).toBeInTheDocument()

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/auth/login',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ email: VALID_EMAIL, password: VALID_PASSWORD }) }),
    )

    const stored = JSON.parse(window.sessionStorage.getItem('tenantforge:auth:session') ?? '{}')
    expect(stored.accessToken).toBe('signed-test-token')
    expect(stored.user.isPlatformAdmin).toBe(true)
  })

  it('restores a valid stored session after a page refresh', async () => {
    const stored: Record<string, unknown> = {
      user: loginResponse().user,
      accessToken: 'signed-test-token',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))
    // The stored session is re-verified against the real contract.
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(loginResponse().user)))

    renderApp('/dashboard')

    expect(await screen.findByRole('heading', { name: /tenantforge برای نخستین برش/i })).toBeInTheDocument()
  })

  it('drops an expired stored session and requires a fresh sign-in', async () => {
    const stored: Record<string, unknown> = {
      user: loginResponse().user,
      accessToken: 'expired-token',
      expiresAtUtc: '2020-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Invalid credentials' }, 401)))

    renderApp('/dashboard')

    expect(await screen.findByRole('heading', { name: 'ورود' })).toBeInTheDocument()
    expect(window.sessionStorage.getItem('tenantforge:auth:session')).toBeNull()
  })

  it('shows a Persian session-expired notice after a 401 on bootstrap', async () => {
    const stored: Record<string, unknown> = {
      user: loginResponse().user,
      accessToken: 'expired-token',
      expiresAtUtc: '2030-01-01T00:00:00Z',
    }
    window.sessionStorage.setItem('tenantforge:auth:session', JSON.stringify(stored))
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({ title: 'Invalid credentials' }, 401)))

    renderApp('/dashboard')

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('نشست شما منقضی شده است. دوباره وارد شوید.')
  })

  it('signs out and returns to login', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(loginResponse())))
    const user = userEvent.setup()
    renderApp()

    await fillCredentialsAndSubmit(user)
    await screen.findByRole('heading', { name: /tenantforge برای نخستین برش/i })

    await user.click(screen.getByRole('button', { name: 'خروج' }))

    expect(await screen.findByRole('heading', { name: 'ورود' })).toBeInTheDocument()
    expect(window.sessionStorage.getItem('tenantforge:auth:session')).toBeNull()
  })
})
