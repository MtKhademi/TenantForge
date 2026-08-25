import { expect, test } from '@playwright/test'

test('S00 login and dashboard shell demo flow', async ({ page }) => {
  const consoleErrors: string[] = []
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text())
  })

  await page.goto('/login')
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()

  await page.getByLabel('Email').fill('wrong@tenantforge.local')
  await page.getByLabel('Password').fill('not-the-password')
  await page.getByRole('button', { name: /open dashboard/i }).click()
  await expect(page.getByRole('alert')).toContainText('mock credentials are not recognized')

  await page.getByLabel('Email').fill('admin@tenantforge.local')
  await page.getByLabel('Password').fill('local-development-password')
  await page.getByRole('button', { name: /open dashboard/i }).click()
  await expect(page.getByRole('heading', { name: /tenantforge is ready/i })).toBeVisible()

  await page.reload()
  await expect(page.getByRole('heading', { name: /tenantforge is ready/i })).toBeVisible()

  await page.getByRole('button', { name: /switch to/i }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', /dark|light/)

  if (test.info().project.name === 'mobile') {
    await page.getByRole('button', { name: /open navigation/i }).click()
    await expect(page.getByRole('dialog', { name: /mobile navigation/i })).toBeVisible()
    await page.screenshot({ path: '/tmp/opencode/s00-mobile.png', fullPage: true })
  } else {
    await page.screenshot({ path: '/tmp/opencode/s00-desktop.png', fullPage: true })
  }

  expect(consoleErrors).toEqual([])
})
