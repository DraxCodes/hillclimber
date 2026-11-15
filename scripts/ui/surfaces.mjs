// Repeatable check of every interactive surface. Requires Playwright and a running
// server (default http://localhost:5179):
//
//   cd scripts/ui && npm install
//   node surfaces.mjs
//   BASE_URL=http://localhost:5179 node surfaces.mjs
//
// It drives the PIN gate, the run|hillclimb method switch, the folder picker, the run
// and climb controls, the theme toggle, the live output panel, and the /status and
// /activity pages, and writes one screenshot per page.
//
// The runs it starts are stopped immediately, but they do reach a paid provider. This
// harness is pinned to a configuration with Surplus as the ONLY provider: it refuses to
// start (exit 2) otherwise or when Surplus is not connected, and it never approves a
// fallback. Each run's echoed command line must use a `surplus/` model and must not
// reference `openrouter/`; when the first run does not, it stops before starting another
// paid run. The two-provider approval UX is covered by the free `providers.mjs`.
//
// Exit codes: 0 all checks passed, 1 a check failed, 2 refused to continue (Surplus-only
// configuration not in place, Surplus not connected, or a paid run did not use Surplus).

import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';
const FIXTURE = process.env.FIXTURE || 'C:/Users/joelp/AppData/Local/Temp/climber-fixture';
const REPO = process.env.REPO || join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const SHOTS = process.env.SHOTS_DIR || join(tmpdir(), 'climber-ui-shots');
mkdirSync(SHOTS, { recursive: true });

const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok, detail });
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${name}${detail ? ' — ' + detail : ''}`);
};
const shot = async (page, name) => page.screenshot({ path: join(SHOTS, name), fullPage: true });

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
const status = async () => (await page.locator('.status-word').innerText()).toLowerCase();
const start = async () => page.locator('.btn-row .btn', { hasText: 'start' }).first().click();
const settle = async () => page.waitForTimeout(2500);

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
const surplusConn = page.locator('.spendline .conn[data-provider="surplus"]');
check('spend line is present', (await page.locator('.spendline').count()) === 1);
check('spend line shows Surplus as primary', (await surplusConn.innerText()).toLowerCase().includes('surplus'));
check('spend line shows a spend figure', (await page.locator('.spendline .spend-item').first().innerText()).trim().length > 0);

// --- provider guard: paid checks only ever run on Surplus -----------------
// The run and climb below call a paid API. Surplus is the only provider they may spend
// on, so the harness requires Surplus to be the single configured provider and never
// approves anything else. Stop here instead of drifting into a failed run or a charge.
const summary = () => `${results.length} checks, ${results.filter((r) => !r.ok).length} failures`;
const refuse = async (reason) => {
  console.error(`\n${reason}`);
  console.error(`REFUSED — ${summary()}. No further paid runs were started.`);
  await browser.close();
  process.exit(2);
};

const configuredProviders = (await page.locator('.providerbar .method').allInnerTexts())
  .map((text) => text.trim().toLowerCase());
if (configuredProviders.length !== 1 || configuredProviders[0] !== 'surplus') {
  await refuse(
    `This paid harness needs Surplus as the only configured provider (saw: ${configuredProviders.join(', ') || 'none'}).\n` +
      'Set Climber:Providers to a single Surplus entry (see "Testing" in README.md), then retry. ' +
      'Omitting the array synthesises the legacy Surplus + OpenRouter pair, which also refuses.',
  );
}

await page
  .waitForFunction(() => !/checking/i.test(document.querySelector('.spendline .conn[data-provider="surplus"]')?.textContent ?? ''), null, { timeout: 20_000 })
  .catch(() => {});
const surplusState = (await surplusConn.innerText()).split(':').pop().trim().toLowerCase();
if (surplusState !== 'connected') {
  await refuse(
    `Surplus is not connected (state: ${surplusState}).\n` +
      'The token-spending checks only run through Surplus; configure Climber:SurplusApiKey or SURPLUS_API_KEY for the app, then retry.',
  );
}

const logText = async () => (await page.locator('.log-line').allInnerTexts()).join('\n');
const approvalOffered = async () => (await page.locator('.approval-row').count()) > 0;

// Only the echoed command lines matter: prompt text and model prose can mention any provider.
const commandLines = (text) => text.split('\n').filter((line) => line.includes('opencode run'));
const surplusCommand = (text) => {
  const commands = commandLines(text);
  return commands.length > 0 && commands.every((line) => /--model[=\s]+surplus\//.test(line));
};
const openRouterCommand = (text) => /openrouter\//.test(commandLines(text).join('\n'));

// --- method switch --------------------------------------------------------
check('method switch is present', (await page.locator('.methodbar .method').count()) === 2);
check('run is the default method', (await page.locator('.methodbar .method.active').innerText()).toLowerCase() === 'run');

// --- run-mode controls ----------------------------------------------------
for (const sel of ['#folder', '#prompt']) {
  check(`control ${sel} exists`, (await page.locator(sel).count()) === 1);
}
check('run mode hides max iterations', (await page.locator('#max').count()) === 0);
check('agent line is displayed', (await page.locator('.agent-line .chip').count()) >= 1);
check('start/stop/lock buttons exist', (await page.locator('.btn-row .btn').count()) >= 3);

// --- agents resolved from the target folder's .opencode/agents -----------
await setField('#folder', REPO);
await page.waitForTimeout(400);
const runAgent = (await page.locator('.agent-line').innerText()).toLowerCase();
check('run mode shows the repo default agent', runAgent.includes('task-orchestrator'), runAgent);

// --- empty prompt validation (run) ---------------------------------------
await setField('#prompt', '  ');
await start();
await page.waitForTimeout(400);
check('empty prompt shows a clean error', (await page.locator('.notice-bad').innerText()) === 'A prompt is required.');

// --- switch to hillclimb --------------------------------------------------
await page.click('button.method:has-text("hillclimb")');
await page.waitForTimeout(300);
check('hillclimb becomes the active method', (await page.locator('.methodbar .method.active').innerText()).toLowerCase() === 'hillclimb');
check('hillclimb reveals max iterations', (await page.locator('#max').count()) === 1);
const climbAgents = (await page.locator('.agent-line').innerText()).toLowerCase();
check('hillclimb shows the agent trio',
  climbAgents.includes('task-orchestrator') && climbAgents.includes('plan-architect') && climbAgents.includes('quality-gatekeeper'),
  climbAgents);
await setField('#prompt', '  ');
await start();
await page.waitForTimeout(400);
check('empty goal shows a clean error', (await page.locator('.notice-bad').innerText()) === 'A goal prompt is required.');
await page.click('button.method:has-text("run")');
await page.waitForTimeout(300);
check('run mode hides max iterations again', (await page.locator('#max').count()) === 0);

// --- pre-flight: an impossible target is blocked -------------------------
await setField('#folder', 'C:\\definitely-not-a-real-folder-xyz');
await setField('#prompt', 'do a small thing');
await start();
await page.waitForTimeout(400);
const preflightError = await page.locator('.notice-bad').innerText();
check('missing target folder is blocked', preflightError.includes('does not exist'), preflightError);
check('blocked start does not run', (await status()) !== 'running');

// --- folder picker: open / navigate / choose / cancel ---------------------
await setField('#folder', FIXTURE);
await page.click('text=browse');
await page.waitForTimeout(300);
check('folder picker opens', (await page.locator('.browser').count()) === 1);
await page.locator('.dir').first().click().catch(() => {});
await page.waitForTimeout(250);
const chosen = await page.locator('.browser-current').innerText();
await page.click('text=use this folder');
await page.waitForTimeout(250);
check('picker "use this folder" sets the folder', (await page.inputValue('#folder')) === chosen, chosen);

await page.click('text=browse');
await page.waitForTimeout(300);
await page.click('text=cancel');
await page.waitForTimeout(200);
check('picker cancel closes it', (await page.locator('.browser').count()) === 0);
check('picker cancel keeps the folder value', (await page.inputValue('#folder')) === chosen);

// --- theme toggle ---------------------------------------------------------
const themeBefore = await page.evaluate(() => document.documentElement.getAttribute('data-theme-pref'));
await page.click('#theme-toggle');
await page.waitForTimeout(250);
const themeAfter = await page.evaluate(() => document.documentElement.getAttribute('data-theme-pref'));
check('theme toggle changes the preference', themeBefore !== themeAfter, `${themeBefore} -> ${themeAfter}`);
await shot(page, 'home.png');

// --- run mode: start / live output / stop --------------------------------
await setField('#folder', FIXTURE);
await setField('#prompt', 'Say hello in one short line.');
await start();
// The clear belongs at launch, not at settle: the box is empty 150ms after the click,
// long before the 2.5s settle below.
await page.waitForTimeout(150);
check('prompt clears as soon as the run starts', (await page.inputValue('#prompt')).trim() === '', `status ${await status()}`);
await settle();
check('run starts and settles', ['running', 'completed', 'stopped', 'failed'].includes(await status()), await status());
check('run live output panel fills', (await page.locator('.log-line').count()) > 0);
const primaryTab = (await page.locator('.outtab').first().innerText()).toLowerCase();
check('output labels the running agent', primaryTab.includes('orchestrator') || primaryTab.includes('main'), primaryTab);
await page.locator('.btn-row .btn', { hasText: 'stop' }).first().click().catch(() => {});
await page.waitForTimeout(1200);
check('run stop returns to a settled state', ['stopped', 'completed', 'failed', 'idle'].includes(await status()));

// --- the cleared prompt is offered back, and the box takes focus ----------
const focused = await page.evaluate(() => document.activeElement?.id ?? '');
check('the emptied box takes focus once idle', focused === 'prompt', focused || 'no element');
const recall = page.locator('.prompt-recall');
check('a settled run offers the last prompt back', (await recall.count()) === 1, `count ${await recall.count()}`);
if ((await recall.count()) === 1) {
  await recall.click();
  await page.waitForTimeout(250);
  check('recall restores the prompt that was sent', (await page.inputValue('#prompt')) === 'Say hello in one short line.', await page.inputValue('#prompt'));
  check('recall hides once the box has text again', (await recall.count()) === 0);
}
const runLog = await logText();
const runCommand = (runLog.match(/--model[=\s]+\S+/) ?? [''])[0];
const runOnSurplus = surplusCommand(runLog) && !openRouterCommand(runLog);
check('run runs on Surplus', runOnSurplus, runCommand);
const runApproval = await approvalOffered();
check('run never offers a fallback', !runApproval);
if (!runOnSurplus || runApproval) {
  await refuse('The run did not stay on Surplus. Stopping before the climb would start another paid run.');
}

// --- hillclimb mode: start / stop ----------------------------------------
await page.click('button.method:has-text("hillclimb")');
await page.waitForTimeout(300);
await setField('#folder', FIXTURE);
await setField('#max', '1');
await setField('#prompt', 'Implement the unimplemented methods in Calculator.cs so all tests pass.');
await start();
await settle();
check('climb starts and settles', ['running', 'completed', 'stopped', 'failed'].includes(await status()), await status());
check('climb live output panel fills', (await page.locator('.log-line').count()) > 0);
check('goal clears once the climb takes it', (await page.inputValue('#prompt')).trim() === '', await page.inputValue('#prompt'));
await shot(page, 'hillclimb.png');
await page.locator('.btn-row .btn', { hasText: 'stop' }).first().click().catch(() => {});
await page.waitForTimeout(1200);
check('climb stop returns to a settled state', ['stopped', 'completed', 'failed', 'idle'].includes(await status()));
const climbLog = await logText();
const climbCommand = (climbLog.match(/--model[=\s]+\S+/) ?? [''])[0];
const climbOnSurplus = surplusCommand(climbLog) && !openRouterCommand(climbLog);
check('climb runs on Surplus', climbOnSurplus, climbCommand);
const climbApproval = await approvalOffered();
check('climb never offers a fallback', !climbApproval);
if (!climbOnSurplus || climbApproval) {
  await refuse('The climb did not stay on Surplus.');
}

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
await shot(page, 'status.png');

await page.click('a.nav-link[href="/activity"]');
await page.waitForTimeout(1000);
// The page fetches its provider on load; wait for the report rather than a fixed delay.
await page.waitForSelector('text=Surplus is the primary provider', { timeout: 20000 }).catch(() => {});
check('/activity loads', page.url().endsWith('/activity'));
const activityTitles = (await page.locator('.panel-title').allInnerTexts()).map((t) => t.toLowerCase());
check('/activity shows the surplus section', activityTitles.some((t) => t.includes('surplus')));
check('/activity shows the surplus estimate', (await page.locator('body').innerText()).toLowerCase().includes('est. spent this session'));
check('/activity shows no other provider', !activityTitles.some((t) => t.includes('openrouter')));
check('/activity shows saved-with-skills honesty', (await page.locator('body').innerText()).includes('Not measured'));
await shot(page, 'activity.png');

// --- console cleanliness --------------------------------------------------
check('no console errors', consoleErrors.length === 0, JSON.stringify(consoleErrors));

console.log(`\n${results.length} checks, ${results.filter((r) => !r.ok).length} failures; screenshots in ${SHOTS}`);
await browser.close();
process.exitCode = results.some((r) => !r.ok) ? 1 : 0;
