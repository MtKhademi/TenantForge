const { createRequire } = require('module');
const path = require('path');
const webRequire = createRequire(path.join(__dirname, '..', 'src', 'web', 'index.js'));
const { chromium } = webRequire('playwright');

(async () => {
  const winChrome = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
  const browser = await chromium.launch({ headless: true, executablePath: winChrome, args: ['--no-sandbox','--disable-gpu','--disable-dev-shm-usage'] });
  const context = await browser.newContext();
  const page = await context.newPage();
  const logs = [];
  page.on('console', (m) => logs.push(m.type() + ': ' + m.text()));
  page.on('pageerror', (e) => logs.push('PAGEERROR: ' + e.message));
  await page.addInitScript(() => { try { sessionStorage.setItem('tfCartLeaseScenario', 'expired'); } catch (e) {} });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('http://127.0.0.1:8099/shop/0RM4B8A9M008Q/cart', { waitUntil: 'load' });
  await page.waitForTimeout(3000);
  const text = await page.evaluate(() => document.body.innerText.slice(0, 600));
  console.log('=== BODY TEXT ===');
  console.log(text);
  console.log('=== CONSOLE ===');
  console.log(logs.join('\n'));
  await page.screenshot({ path: __dirname + '/shots/dbg-expired.png', fullPage: true });
  await browser.close();
  console.log('DONE');
})().catch((e) => { console.error('ERR', e.message); process.exit(1); });
