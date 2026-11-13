// Repeatable check of every interactive surface. Requires Playwright and a running
// server (default http://localhost:5179):
//
//   cd scripts/ui && npm install
//   node surfaces.mjs
//   BASE_URL=http://localhost:5179 node surfaces.mjs
//
// It drives the PIN gate, folder picker, climb controls, theme toggle, the live output
// panel, and the /status and /activity pages. The climb it starts is stopped
// immediately, so it does not spend meaningfully. Exit code is non-zero on any failure.

import { chromium } from 'playwright';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';
const FIXTURE = process.env.FIXTURE || 'C:/Users/joelp/AppData/Local/Temp/climber-fixture';

const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok, detail });
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ' — ' + detail : ''}`);
};

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 900, height: 1200 } });
const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));

const setField = async (sel, value) => {
  await page.locator(sel).click();
  await page.locator(sel).press('Control+a');
  await page.locator(sel).fill(value);
  await page.locator(sel).blur();
  await page.waitForTimeout(120);
};

// --- PIN gate -------------------------------------------------------------
await page.goto(BASE, { waitUntil: 'networkidle' });
check('PIN gate is shown', (await page.locator('#pin').count()) === 1);

await page.fill('#pin', '0000');
await page.click('text=unlock →');
await page.waitForTimeout(300);
check('wrong PIN is rejected', (await page.locator('.notice-bad').innerText()).includes('Incorrect'));

await page.fill('#pin', PIN);
await page.click('text=unlock →');
await page.waitForTimeout(500);
check('correct PIN unlocks the dashboard', (await page.locator('.telemetry').count()) > 0);

// --- spend line -----------------------------------------------------------
check('spend line is present', (await page.locator('.spendline').count()) === 1);
check('spend line shows a connection state', (await page.locator('.spendline .conn').innerText()).toLowerCase().includes('openrouter'));
check('spend line links to /activity', (await page.locator('.spendline a.spend-more[href="/activity"]').count()) === 1);

// --- controls: existence + labels ----------------------------------------
for (const sel of ['#folder', '#goal', '#max']) {
  check(`control ${sel} exists`, (await page.locator(sel).count()) === 1);
}
check('start/stop/lock buttons exist', (await page.locator('.btn-row .btn').count()) >= 3);

// --- empty goal validation -----------------------------------------------
await setField('#folder', FIXTURE);
await setField('#goal', '  ');
await page.locator('.btn-row .btn', { hasText: 'start' }).first().click();
await page.waitForTimeout(400);
check('empty goal shows a clean error', (await page.locator('.notice-bad').innerText()) === 'A goal prompt is required.');

// --- folder picker --------------------------------------------------------
await page.click('text=browse');
await page.waitForTimeout(300);
check('folder picker opens', (await page.locator('.browser').count()) === 1);
await page.locator('.dir').first().click().catch(() => {});
await page.waitForTimeout(250);
await page.click('text=cancel');
await page.waitForTimeout(200);
check('picker cancel closes it', (await page.locator('.browser').count()) === 0);
check('picker cancel keeps the folder value', (await page.inputValue('#folder')) !== '');

// --- theme toggle ---------------------------------------------------------
const themeBefore = await page.evaluate(() => document.documentElement.getAttribute('data-theme-pref'));
await page.click('#theme-toggle');
await page.waitForTimeout(250);
const themeAfter = await page.evaluate(() => document.documentElement.getAttribute('data-theme-pref'));
check('theme toggle changes the preference', themeBefore !== themeAfter, `${themeBefore} -> ${themeAfter}`);

// --- start / live output / stop ------------------------------------------
await setField('#folder', FIXTURE);
await setField('#goal', 'Implement the unimplemented methods in Calculator.cs so all tests pass.');
await setField('#max', '1');
await page.locator('.btn-row .btn', { hasText: 'start' }).first().click();
await page.waitForTimeout(1500);
check('run starts', ['running', 'completed', 'stopped'].includes((await page.locator('.status-word').innerText()).toLowerCase()));
check('live output panel fills', (await page.locator('.log-line').count()) > 0);
await page.locator('.btn-row .btn', { hasText: 'stop' }).first().click().catch(() => {});
await page.waitForTimeout(1200);
check('stop returns to a settled state', ['stopped', 'completed', 'failed', 'idle'].includes((await page.locator('.status-word').innerText()).toLowerCase()));

// --- lock ----------------------------------------------------------------
await page.locator('.btn-row .btn', { hasText: 'lock' }).first().click();
await page.waitForTimeout(300);
check('lock returns to the PIN gate', (await page.locator('#pin').count()) === 1);

// --- navigation: /status and /activity (re-unlock first) -----------------
await page.fill('#pin', PIN);
await page.click('text=unlock →');
await page.waitForTimeout(400);

await page.click('a.nav-link[href="/status"]');
await page.waitForTimeout(900);
check('/status loads', page.url().endsWith('/status'));
check('/status has panels', (await page.locator('.panel').count()) >= 4);

await page.click('a.nav-link[href="/activity"]');
await page.waitForTimeout(1200);
check('/activity loads', page.url().endsWith('/activity'));
const activityTitles = (await page.locator('.panel-title').allInnerTexts()).map((t) => t.toLowerCase());
check('/activity shows connection', activityTitles.some((t) => t.includes('connection')));
check('/activity shows account spend', activityTitles.some((t) => t.includes('account spend')));
check('/activity shows the spend graph section', activityTitles.some((t) => t.includes('activity')));
check('/activity shows saved-with-skills honesty', (await page.locator('body').innerText()).includes('Not measured'));

// --- console cleanliness --------------------------------------------------
check('no console errors', consoleErrors.length === 0, JSON.stringify(consoleErrors));

console.log(`\n${results.length} checks, ${results.filter((r) => !r.ok).length} failures`);
await browser.close();
process.exitCode = results.some((r) => !r.ok) ? 1 : 0;
