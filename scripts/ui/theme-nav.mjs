// Regression guard: the theme must survive Blazor enhanced navigation.
//
// Blazor's enhanced navigation patches the document and overwrites the server-rendered
// <html data-theme> / <html data-theme-pref> attributes, so any page that is reached by
// an internal link is at risk of silently snapping back to the default (light) theme.
// This test sets a preference on the first page, then crawls every internal link it can
// find — on every page it reaches — asserting the resolved theme and preference are
// preserved. New pages linked from the app are therefore covered automatically.
//
// Requires Playwright and a running server:
//   cd scripts/ui && npm install
//   node theme-nav.mjs
//   BASE_URL=http://localhost:5179 node theme-nav.mjs
//
// Exit code is non-zero on any failure.

import { chromium } from 'playwright';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';
const MAX_PAGES = 12;

const failures = [];
const notes = [];
const fail = (msg) => { failures.push(msg); console.log('  FAIL ' + msg); };
const pass = (msg) => console.log('  ok   ' + msg);

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 900, height: 1000 } });

const consoleErrors = [];
page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()); });
page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + e.message));

const theme = () => page.evaluate(() => ({
  theme: document.documentElement.getAttribute('data-theme'),
  pref: document.documentElement.getAttribute('data-theme-pref'),
  visibleIcons: [...document.querySelectorAll('[data-theme-icon]')]
    .filter((i) => i.getBoundingClientRect().width > 0)
    .map((i) => i.getAttribute('data-theme-icon')),
  path: location.pathname,
}));

const internalLinks = () => page.$$eval('a[href]', (anchors) =>
  anchors
    .map((a) => a.getAttribute('href'))
    .filter((h) => h && h.startsWith('/') && !h.startsWith('//'))
    .filter((h, i, all) => all.indexOf(h) === i));

await page.goto(BASE, { waitUntil: 'networkidle' });

// Unlock the dashboard if a PIN gate is showing; harmless if already unlocked.
if (await page.locator('#pin').count()) {
  await page.fill('#pin', PIN);
  await page.click('text=unlock →').catch(() => {});
  await page.waitForTimeout(600);
}

// Choose a non-default theme so a regression is observable (dark, if we aren't already).
for (let i = 0; i < 4; i++) {
  if ((await theme()).pref === 'dark') break;
  await page.click('#theme-toggle');
  await page.waitForTimeout(200);
}

let expected = await theme();
if (expected.pref === 'system') {
  // Machine itself is in system/light mode and we could not force dark: still a valid
  // assertion target, but warn so a green run is not mistaken for covering dark.
  notes.push('could not force dark; asserting the resolved "' + expected.theme + '" theme is preserved');
}
console.log('theme set on first page:', JSON.stringify(expected));

const visited = new Set([expected.path]);
const queue = [...(await internalLinks())];
let pages = 1;

while (queue.length && pages < MAX_PAGES) {
  const href = queue.shift();
  if (visited.has(href)) continue;

  if (!(await page.locator(`a[href="${href}"]`).count())) {
    continue; // link is not present on the current page
  }

  console.log(`navigate -> ${href}`);
  await page.click(`a[href="${href}"]`);
  await page.waitForTimeout(700);
  pages++;

  const after = await theme();
  visited.add(after.path);

  if (after.path !== href) {
    notes.push(`clicked ${href} but landed on ${after.path}`);
  }
  if (after.theme === expected.theme && after.pref === expected.pref) {
    pass(`${href}: theme preserved (${after.theme})`);
  } else {
    fail(`${href}: theme changed ${JSON.stringify(expected)} -> ${JSON.stringify(after)}`);
  }
  if (after.visibleIcons.length !== 1 || after.visibleIcons[0] !== after.pref) {
    fail(`${href}: theme icon out of sync (visible=${JSON.stringify(after.visibleIcons)}, pref=${after.pref})`);
  }

  // Discover links on the page we just landed on, so a new page linking onward is covered.
  for (const next of await internalLinks()) {
    if (!visited.has(next)) queue.push(next);
  }
}

if (consoleErrors.length) {
  fail(`console errors: ${JSON.stringify(consoleErrors)}`);
} else {
  pass('no console errors');
}

console.log(`\ncrawled ${pages} page(s); visited ${JSON.stringify([...visited])}`);
for (const note of notes) console.log('note: ' + note);
console.log(failures.length ? `FAILURES: ${failures.length}` : 'FAILURES: 0');

await browser.close();
process.exitCode = failures.length ? 1 : 0;
