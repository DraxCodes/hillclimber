// Regression guard: /status and /activity must be behind the PIN gate.
//
// A fresh browser context (no localStorage session) must see the PIN form and no page
// content; entering the PIN must reveal the content. This is a free check — it does not
// drive `opencode` and spends no tokens.
//
// Requires Playwright and a running server:
//   cd scripts/ui && npm install
//   node security.mjs
//   BASE_URL=http://localhost:5179 PIN=1234 node security.mjs
//
// Exit code is non-zero on any failure.

import { chromium } from 'playwright';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';

const failures = [];
const check = (ok, msg) => {
  console.log((ok ? '  ok   ' : '  FAIL ') + msg);
  if (!ok) failures.push(msg);
};

const browser = await chromium.launch({ headless: true });

for (const { path, content } of [
  { path: '/status', content: 'Project health' },
  { path: '/activity', content: 'OpenRouter connection' },
]) {
  const context = await browser.newContext({ viewport: { width: 900, height: 1000 } });
  const page = await context.newPage();

  await page.goto(BASE + path, { waitUntil: 'networkidle' });
  await page.waitForTimeout(500);
  check(await page.locator('#pin').count() === 1, `${path}: PIN gate shown in a fresh session`);
  check(await page.locator('text=Enter the access PIN').count() === 1, `${path}: locked copy shown`);
  check(await page.locator(`text=${content}`).count() === 0, `${path}: no content before unlock`);

  await page.fill('#pin', PIN);
  await page.click('text=unlock →');
  await page.waitForTimeout(800);
  check(await page.locator('#pin').count() === 0, `${path}: gate dismissed after unlock`);
  check(await page.locator(`text=${content}`).count() === 1, `${path}: content visible after unlock`);

  await context.close();
}

console.log(failures.length ? `FAILURES: ${failures.length}` : 'FAILURES: 0');
await browser.close();
process.exitCode = failures.length ? 1 : 0;
