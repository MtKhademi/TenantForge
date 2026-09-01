import { expect, test, type Page, type Request } from '@playwright/test'

const VALID_EMAIL = 'admin@tenantforge.local'
const VALID_PASSWORD = 'local-development-password'
const LOGIN_ROUTE = '**/api/auth/login'

function loginResponse() {
  return {
    accessToken: 'signed-e2e-token',
    expiresAtUtc: '2030-01-01T00:00:00Z',
    user: {
      id: 'development-admin',
      email: VALID_EMAIL,
      displayName: 'Platform Administrator',
      isPlatformAdmin: true,
    },
  }
}

async function fillAndSubmit(page: Page, { email = VALID_EMAIL, password = VALID_PASSWORD } = {}) {
  await page.getByLabel('ایمیل').fill(email)
  await page.getByLabel('رمز عبور').fill(password)
  await page.getByRole('button', { name: 'ورود' }).click()
}

test('S01: sign in through the login API, verify 401 and unavailable states', async ({
  page,
}) => {
  const consoleErrors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })

  const loginRequests: Request[] = []
  await page.route(LOGIN_ROUTE, (route) => {
    const request = route.request()
    if (request.method() !== 'POST') {
      return route.fallback()
    }
    loginRequests.push(request)
    const body = request.postDataJSON() as { email?: string; password?: string }
    if (body.email === VALID_EMAIL && body.password === VALID_PASSWORD) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(loginResponse()),
      })
    }
    return route.fulfill({
      status: 401,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        title: 'Invalid credentials',
        detail: 'The email or password is incorrect.',
      }),
    })
  })

  // --- F004: the document boundary is Persian RTL ---
  await expect(page.locator('html')).toHaveAttribute('lang', 'fa')
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')

  // --- Idle state ---
  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'ورود' })).toBeVisible()
  await expect(page.getByText(/برش S01 — ورود توسعه/)).toBeVisible()

  // --- Client validation (no API call) ---
  await page.getByRole('button', { name: 'ورود' }).click()
  await expect(page.getByText('ایمیل الزامی است.')).toBeVisible()
  await expect(page.getByText('رمز عبور الزامی است.')).toBeVisible()
  expect(loginRequests).toHaveLength(0)

  // --- 401 invalid credentials ---
  await fillAndSubmit(page, { password: 'not-the-password' })
  const invalidAlert = page.getByRole('alert')
  await expect(invalidAlert).toBeVisible()
    await expect(invalidAlert).toHaveText(/ایمیل یا رمز عبور درست نیست/)
    await expect(page.getByRole('heading', { name: 'ورود' })).toBeVisible()
    expect(loginRequests).toHaveLength(1)

    // --- Success: reach the dashboard through the API ---
    await fillAndSubmit(page)
    await expect(page.getByRole('heading', { name: /tenantforge برای نخستین برش/i })).toBeVisible()
    await expect(page.getByText(/وارد شده‌اید/)).toBeVisible()
    expect(loginRequests).toHaveLength(2)

  // The stored session carries the signed token.
  const stored = await page.evaluate(() =>
    JSON.parse(window.sessionStorage.getItem('tenantforge:auth:session') ?? 'null'),
  )
  expect(stored?.accessToken).toBe('signed-e2e-token')
  expect(stored?.user.isPlatformAdmin).toBe(true)

  // --- Refresh keeps the session (sessionStorage) ---
  await page.reload()
  await expect(page.getByRole('heading', { name: /tenantforge برای نخستین برش/i })).toBeVisible()

  // --- Theme toggle still works ---
  await page.getByRole('button', { name: /تغییر به پوسته/i }).first().click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', /dark|light/)

  // --- Sign out returns to login ---
  await page.getByRole('button', { name: 'خروج' }).click()
  await expect(page.getByRole('heading', { name: 'ورود' })).toBeVisible()
  const afterSignOut = await page.evaluate(() =>
    window.sessionStorage.getItem('tenantforge:auth:session'),
  )
  expect(afterSignOut).toBeNull()

  // --- Unavailable API: distinct from invalid credentials ---
  await page.route(LOGIN_ROUTE, (route) => route.abort())
  await fillAndSubmit(page)
    const unavailableAlert = page.getByRole('alert')
    await expect(unavailableAlert).toBeVisible()
    await expect(unavailableAlert).toHaveText(/سرویس ورود در دسترس نیست/)
    await expect(unavailableAlert).not.toHaveText(/ایمیل یا رمز عبور درست نیست/)

    // --- Screenshots (desktop + mobile projects) ---
    const screenshotPath =
      test.info().project.name === 'mobile'
        ? '/tmp/opencode/f004-s01-mobile-unavailable.png'
        : '/tmp/opencode/f004-s01-desktop-unavailable.png'
  await page.screenshot({ path: screenshotPath, fullPage: true })

  // No unexpected console errors (the aborted request logs a network error
  // that we intentionally surface in the UI, so whitelist it).
  const unexpected = consoleErrors.filter(
    (line) => !/api\/auth\/login|failed to load resource|net::ERR/i.test(line),
  )
  expect(unexpected).toEqual([])
})
