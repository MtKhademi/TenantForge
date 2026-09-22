const { createRequire } = require('module');
const path = require('path');
// Load playwright from the web app's node_modules (works from either WSL or
// Windows node, regardless of the script's own directory).
const webRequire = createRequire(path.join(__dirname, '..', 'src', 'web', 'index.js'));
const { chromium } = webRequire('playwright');

// Windows static server hosting the production dist/ (see static-server.js).
const BASE = 'http://127.0.0.1:8099';
const TENANT = '0RM4B8A9M008Q';
const TENANT_B = '0RM4B8A9M009Z';
const OUT = __dirname + '/shots';
const SCENARIO_KEY = 'tfCartLeaseScenario';
const CART_KEY = 'tenantforge:shop:cartId';
const DRAFT_KEY = 'tenantforge:shop:orderDraft';

let pass = 0, fail = 0;
const failures = [];
function check(name, cond, extra = '') {
  if (cond) { pass++; console.log(`PASS: ${name}`); }
  else { fail++; failures.push(name + (extra ? ` — ${extra}` : '')); console.log(`FAIL: ${name} ${extra}`); }
}
function log(name) { console.log(`\n==== ${name} ====`); }

const DRAFT = { customerName: 'تست', customerPhone: '09120000000', shippingProvince: 'تهران', shippingCity: 'تهران', shippingAddressLine: 'خیابان تست', shippingPostalCode: '12345', couponCode: null, subTotal: 100000, discountAmount: 0, shippingCost: 10000, grandTotal: 110000 };

async function newScenarioPage(browser, scenario, seed) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const consoleErrors = [];
  page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
  page.on('pageerror', (e) => consoleErrors.push('PAGEERROR: ' + e.message));
  if (scenario || seed) {
    await page.addInitScript(([sc, seedFn]) => {
      try {
        if (sc) sessionStorage.setItem(SCENARIO_KEY, sc);
        if (seedFn) {
          const fn = new Function('CART_KEY', 'DRAFT_KEY', 'TENANT', 'TENANT_B', 'DRAFT', seedFn);
          fn(CART_KEY, DRAFT_KEY, TENANT, TENANT_B, DRAFT);
        }
      } catch (e) { console.log('seed error', e); }
    }, [scenario, seed]);
  }
  return { context, page, consoleErrors };
}

function newConsoleErrors(errors) {
  return errors.filter((t) => !t.includes('/api/') && !t.includes('api/shop') && !t.includes('api/tenants') && !t.includes('404'));
}

function countdownSeconds(text) {
  const m = text.match(/(\d+):(\d{2})/);
  if (!m) return null;
  return parseInt(m[1], 10) * 60 + parseInt(m[2], 10);
}

(async () => {
  await require('fs').mkdirSync(OUT, { recursive: true });
  const launchOpts = {
    headless: true,
    args: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
  };
  // Windows node: drive the system Chrome (Playwright's own browser is not
  // installed and its CDN is geo-blocked here). WSL node would use the same
  // executable via interop, but the CDP port can't cross the NAT boundary, so
  // this path is only exercised from Windows node.
  const winChrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
  if (process.env.CDP_EXEC) launchOpts.executablePath = process.env.CDP_EXEC;
  else if (process.platform === 'win32' && require('fs').existsSync(winChrome)) launchOpts.executablePath = winChrome;
  const browser = await chromium.launch(launchOpts);
  const RECOVERY = 'رزرو سبد خرید منقضی شد';
  const COUNTDOWN = 'رزرو سبد خرید تا';

  // A. countdown renders + ticks
  {
    log('A. shortLease: countdown renders and ticks down (1440x900)');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    const s1 = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('A1 countdown renders mm:ss', s1 !== null, `s1=${s1}`);
    const itemCount = await page.locator('section[aria-label="سبد خرید"] input[type="number"]').count();
    check('A2 seeded cart items render', itemCount >= 2, `items=${itemCount}`);
    await page.screenshot({ path: `${OUT}/a-cart-short-lease-1440.png`, fullPage: true });
    await page.waitForTimeout(2600);
    const s2 = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('A3 countdown ticks down', s2 !== null && s1 !== null && s2 < s1, `s1=${s1} s2=${s2}`);
    check('A4 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // B. mutation extends the lease
  {
    log('B. shortLease: update quantity extends the countdown');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    const before = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    await page.waitForTimeout(3000);
    const mid = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('B1 pre-mutation countdown decreased', mid !== null && before !== null && mid < before, `before=${before} mid=${mid}`);
    await page.evaluate(() => {
      const btn = Array.from(document.querySelectorAll('button')).find((b) => (b.getAttribute('aria-label') || '').startsWith('افزایش تعداد'));
      if (btn) btn.click();
    });
    await page.waitForTimeout(1000);
    const after = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('B2 mutation extends lease (jumped up)', after !== null && mid !== null && after > mid, `mid=${mid} after=${after}`);
    const qty = await page.locator('section[aria-label="سبد خرید"] input[type="number"]').first().inputValue();
    check('B3 quantity updated to 2', qty === '2', `qty=${qty}`);
    check('B4 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // C. plain GET does not extend (SPA nav away + back, NO document reload)
  {
    log('C. shortLease: SPA re-entry GET does NOT extend the lease');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 10000 });
    const t0 = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    await page.waitForTimeout(3500);
    const t1 = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('C1 countdown decreased before nav', t1 !== null && t0 !== null && t1 < t0, `t0=${t0} t1=${t1}`);
    // In-app (SPA) navigation: logo -> catalog, then the header cart link ->
    // cart. No document reload, so the in-memory mock cart (and its un-
    // extended lease) survives. The re-entry getCart must NOT push the lease.
    await page.getByRole('link', { name: /فروشگاه|بازگشت/, exact: false }).first().click().catch(async () => {
      // Fallback: click the logo link (first header link).
      await page.locator('header a').first().click();
    });
    await page.waitForTimeout(3000);
    await page.getByRole('link', { name: /سبد خرید/ }).first().click();
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 10000 });
    const t2 = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    check('C2 GET did not extend (continues lower than t1)', t2 !== null && t1 !== null && t2 < t0 && t2 <= t1 + 1, `t0=${t0} t1=${t1} t2=${t2}`);
    check('C3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // D. 410 expired on cart page
  {
    log('D. expired: cart page 410 recovery + catalog link');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'expired');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    const recovery = page.getByText(RECOVERY, { exact: false }).first();
    await recovery.waitFor({ timeout: 8000 });
    check('D1 recovery title renders', (await recovery.count()) > 0);
    const back = page.getByRole('link', { name: /بازگشت به فروشگاه/ }).first();
    check('D2 recovery back-to-catalog present', (await back.count()) > 0);
    check('D3 catalog link correct', (await back.getAttribute('href')) === `/shop/${TENANT}`);
    const stored = await page.evaluate((k) => JSON.parse(localStorage.getItem(k) || '{}'), CART_KEY);
    check('D4 tenant cart id cleared on expiry', stored[TENANT] === undefined, JSON.stringify(stored));
    await page.screenshot({ path: `${OUT}/d-cart-expired-1440.png`, fullPage: true });
    check('D5 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // E. 410 expired on checkout page
  {
    log('E. expired: checkout page same 410 recovery');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'expired');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/checkout`, { waitUntil: 'load' });
    const recovery = page.getByText(RECOVERY, { exact: false }).first();
    await recovery.waitFor({ timeout: 8000 });
    check('E1 checkout recovery renders', (await recovery.count()) > 0);
    check('E2 checkout back-link correct', (await page.getByRole('link', { name: /بازگشت به فروشگاه/ }).first().getAttribute('href')) === `/shop/${TENANT}`);
    await page.screenshot({ path: `${OUT}/e-checkout-expired-1440.png`, fullPage: true });
    check('E3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // F. 410 expired on order-review page
  {
    log('F. expired: order-review page same 410 recovery');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'expired', `
      const rec = {}; rec[TENANT] = DRAFT;
      try { sessionStorage.setItem(DRAFT_KEY, JSON.stringify(rec)); } catch (e) {}
    `);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/order-review`, { waitUntil: 'load' });
    const recovery = page.getByText(RECOVERY, { exact: false }).first();
    await recovery.waitFor({ timeout: 8000 });
    check('F1 order-review recovery renders', (await recovery.count()) > 0);
    check('F2 order-review back-link correct', (await page.getByRole('link', { name: /بازگشت به فروشگاه/ }).first().getAttribute('href')) === `/shop/${TENANT}`);
    await page.screenshot({ path: `${OUT}/f-review-expired-1440.png`, fullPage: true });
    check('F3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // G. plain 404 is NOT expiry
  {
    log('G. notFound: plain 404 renders ordinary error, NOT expiry recovery');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'notFound');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.waitForTimeout(1200);
    const recoveryCount = await page.getByText(RECOVERY, { exact: false }).count();
    check('G1 404 does NOT show expiry recovery', recoveryCount === 0, `recoveryCount=${recoveryCount}`);
    const errCount = await page.getByText('مشکلی پیش آمد', { exact: false }).count();
    check('G2 404 shows ordinary error state', errCount > 0, `err=${errCount}`);
    check('G3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // H. network failure -> retry
  {
    log('H. unavailable: network failure shows retry, NOT expiry recovery');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'unavailable');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText('اتصال برقرار نشد', { exact: false }).first().waitFor({ timeout: 8000 });
    const recoveryCount = await page.getByText(RECOVERY, { exact: false }).count();
    check('H1 network failure does NOT show expiry recovery', recoveryCount === 0);
    check('H2 unavailable shows retry control', (await page.getByRole('button', { name: /تلاش دوباره/ }).first().count()) > 0);
    await page.screenshot({ path: `${OUT}/h-cart-unavailable-1440.png`, fullPage: true });
    check('H3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // I. tenant isolation on expiry
  {
    log('I. expiry clears ONLY current tenant (second tenant untouched)');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'expired', `
      const rec = {}; rec[TENANT] = 'AAAA000000001'; rec[TENANT_B] = 'BBBB000000001';
      try { localStorage.setItem(CART_KEY, JSON.stringify(rec)); } catch (e) {}
    `);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(RECOVERY, { exact: false }).first().waitFor({ timeout: 8000 });
    const stored = await page.evaluate((k) => JSON.parse(localStorage.getItem(k) || '{}'), CART_KEY);
    check('I1 current tenant cart id cleared', stored[TENANT] === undefined, JSON.stringify(stored));
    check('I2 second tenant cart id preserved', stored[TENANT_B] === 'BBBB000000001', JSON.stringify(stored));
    check('I3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // J. countdown strictly decreasing (no poll / no auto-extend)
  {
    log('J. shortLease: countdown strictly decreases (no poll, no auto-extend)');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    const samples = [];
    for (let i = 0; i < 4; i++) {
      samples.push(countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText()));
      await page.waitForTimeout(1200);
    }
    check('J1 samples captured', samples.every((s) => s !== null), JSON.stringify(samples));
    check('J2 strictly decreasing', samples.every((s, i) => i === 0 || s < samples[i - 1]), JSON.stringify(samples));
    check('J3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // K. unmount mid-countdown: no console error, timer cleaned
  {
    log('K. unmount mid-countdown: no new console error, countdown gone');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    await page.waitForTimeout(1500);
    await page.goto(`${BASE}/shop/${TENANT}`, { waitUntil: 'load' });
    await page.waitForTimeout(2000);
    check('K1 countdown gone after unmount', (await page.getByText(COUNTDOWN, { exact: false }).count()) === 0);
    const fresh = newConsoleErrors(consoleErrors);
    check('K2 no NEW console errors after unmount', fresh.length === 0, JSON.stringify(fresh));
    await context.close();
  }

  // L. mobile 390x844 expiry alert
  {
    log('L. mobile 390x844: expiry alert renders, no RTL overflow');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'expired');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    const recovery = page.getByText(RECOVERY, { exact: false }).first();
    await recovery.waitFor({ timeout: 8000 });
    const box = await recovery.boundingBox();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth + 1);
    check('L1 recovery alert visible on mobile', box !== null && box.width > 0, `box=${JSON.stringify(box)}`);
    check('L2 no horizontal RTL overflow', overflow === false, `overflow=${overflow}`);
    await page.screenshot({ path: `${OUT}/l-cart-expired-390.png`, fullPage: true });
    check('L3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // M. tablet 1024x768 success state
  {
    log('M. tablet 1024x768: cart success state renders');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    check('M1 subtotal row renders at 1024x768', (await page.getByText('جمع کل', { exact: false }).count()) > 0);
    await page.screenshot({ path: `${OUT}/m-cart-short-lease-1024.png`, fullPage: true });
    check('M2 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  // N. remove-item mutation extends lease (second mutation type)
  {
    log('N. shortLease: remove item extends the countdown');
    const { context, page, consoleErrors } = await newScenarioPage(browser, 'shortLease');
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto(`${BASE}/shop/${TENANT}/cart`, { waitUntil: 'load' });
    await page.getByText(COUNTDOWN, { exact: false }).first().waitFor({ timeout: 8000 });
    const mid = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    await page.waitForTimeout(2500);
    const pre = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    const before = await page.locator('section[aria-label="سبد خرید"] input[type="number"]').count();
    await page.evaluate(() => {
      const btn = Array.from(document.querySelectorAll('button')).find((b) => (b.getAttribute('aria-label') || '').startsWith('حذف '));
      if (btn) btn.click();
    });
    await page.waitForTimeout(1000);
    const after = countdownSeconds(await page.getByText(COUNTDOWN, { exact: false }).first().innerText());
    const afterCount = await page.locator('section[aria-label="سبد خرید"] input[type="number"]').count();
    check('N1 remove extends lease (jumped up)', after !== null && pre !== null && after > pre, `pre=${pre} after=${after}`);
    check('N2 item count decreased', afterCount === before - 1, `before=${before} afterCount=${afterCount}`);
    check('N3 no NEW console errors', newConsoleErrors(consoleErrors).length === 0, JSON.stringify(newConsoleErrors(consoleErrors)));
    await context.close();
  }

  await browser.close();
  console.log(`\n================ RESULTS ================`);
  console.log(`PASS: ${pass}  FAIL: ${fail}`);
  if (failures.length) { console.log('Failures:'); failures.forEach((f) => console.log('  - ' + f)); }
  process.exit(fail === 0 ? 0 : 1);
})().catch((err) => { console.error('SCRIPT ERROR:', err); process.exit(2); });
