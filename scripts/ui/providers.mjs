// Free regression check of the provider registry's UI. Requires Playwright and a running
// server with at least two configured providers (the default Surplus + OpenRouter setup):
//
//   cd scripts/ui && npm install
//   node providers.mjs
//   BASE_URL=http://localhost:5179 node providers.mjs
//
// It verifies that the provider bar renders every configured provider, that
// a non-primary provider asks for approval by name, that cancelling keeps the primary, and
// that /activity renders one card per provider. It never starts a run and spends no tokens.
//
// Exit code is non-zero on any failure.

import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';
const SHOTS = process.env.SHOTS_DIR || join(tmpdir(), 'climber-ui-shots');
mkdirSync(SHOTS, { recursive: true });

const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok, detail });
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ' — ' + detail : ''}`);
};

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 900, height: 1300 } });
const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));

// --- unlock ---------------------------------------------------------------
await page.goto(BASE, { waitUntil: 'networkidle' });
await page.fill('#pin', PIN);
await page.click('text=unlock →');
await page.waitForTimeout(1000);
check('PIN unlocks the dashboard', (await page.locator('.providerbar .method').count()) > 0);

// --- every configured provider renders -----------------------------------
await page
  .waitForFunction(() => (document.querySelectorAll('.providerbar .method[data-provider]').length ?? 0) > 0, null, { timeout: 20_000 })
  .catch(() => {});

const buttons = page.locator('.providerbar .method');
await buttons.first().waitFor({ timeout: 10_000 }).catch(() => {});
const providerLabels = (await buttons.allInnerTexts()).map((text) => text.trim().toLowerCase());
const providerIds = await page.locator('.providerbar .method[data-provider]').evaluateAll((nodes) =>
  nodes.map((node) => node.getAttribute('data-provider')).filter(Boolean));

check('at least two providers are configured', providerLabels.length >= 2, providerLabels.join(', '));
check('the provider bar has one button per provider', providerLabels.length === providerIds.length,
  `${providerLabels.length} buttons / ${providerIds.length} ids`);
check('every provider button carries a provider id', providerIds.length > 0, providerIds.join(', '));

const activeLabel = (await page.locator('.providerbar .method.active').innerText()).trim().toLowerCase();
check('the first configured provider is active by default', activeLabel === providerLabels[0], activeLabel);

// --- a fallback asks for approval by name --------------------------------
const fallbackLabel = providerLabels[1];
await buttons.nth(1).click();
await page.waitForTimeout(300);
const approval = page.locator('.approval-row').first();
check('choosing a fallback asks for approval', (await approval.count()) === 1);
const approvalText = (await approval.innerText()).toLowerCase();
check('approval copy names the provider', approvalText.includes(fallbackLabel.slice(0, 20)), approvalText);
check('approval offers a cancellation', (await approval.locator('button', { hasText: 'cancel' }).count()) === 1);

await approval.locator('button', { hasText: 'cancel' }).first().click();
await page.waitForTimeout(300);
check('cancelling keeps the primary selected',
  (await page.locator('.providerbar .method.active').innerText()).trim().toLowerCase() === activeLabel);
await page.screenshot({ path: join(SHOTS, 'providers.png'), fullPage: true });

// --- /activity renders one card per provider -----------------------------
await page.click('a.nav-link[href="/activity"]');
await page.waitForTimeout(1200);
const activityTitles = (await page.locator('.panel-title').allInnerTexts()).map((title) => title.toLowerCase());
check('/activity loads', page.url().endsWith('/activity'));
for (const label of providerLabels) {
  check(`/activity has a card for ${label}`, activityTitles.some((title) => title.includes(label)));
}

const metas = (await page.locator('.panel-meta').allInnerTexts()).map((meta) => meta.trim().toLowerCase());
check('/activity marks the first provider primary', metas.includes('primary'));
check('/activity marks later providers as fallbacks', metas.includes('fallback'));
await page.screenshot({ path: join(SHOTS, 'providers-activity.png'), fullPage: true });

// --- console cleanliness --------------------------------------------------
check('no console errors', consoleErrors.length === 0, JSON.stringify(consoleErrors));

console.log(`\n${results.length} checks, ${results.filter((r) => !r.ok).length} failures; screenshots in ${SHOTS}`);
await browser.close();
process.exitCode = results.some((r) => !r.ok) ? 1 : 0;