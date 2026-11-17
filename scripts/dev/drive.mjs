#!/usr/bin/env node
// One persistent, already-authenticated browser window over the live dev host.
//
//   node scripts/dev/drive.mjs start [--headless] [--route /]
//   node scripts/dev/drive.mjs status
//   node scripts/dev/drive.mjs goto /activity
//   node scripts/dev/drive.mjs text [--limit 4000]
//   node scripts/dev/drive.mjs errors [--keep]
//   node scripts/dev/drive.mjs shot [--full] [--width 900] [--height 1200] [--theme light|dark] [path]
//   node scripts/dev/drive.mjs reload
//   node scripts/dev/drive.mjs stop
//   node scripts/dev/drive.mjs help
//
// `start` detaches a small loopback control server; the window stays open and stays unlocked
// between commands. The PIN is typed like a person types it — this never weakens the gate, and
// `scripts/ui/security.mjs` keeps meaning what it meant.

import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import {
  appendFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { createServer } from 'node:http';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { randomBytes } from 'node:crypto';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..', '..');
const stateDir = join(tmpdir(), 'climb-ui', 'drive');
const statePath = join(stateDir, 'state.json');
const profileDir = join(stateDir, 'profile');
const logPath = join(stateDir, 'drive.log');
const shotsDir = join(tmpdir(), 'climb-ui-shots');
const hostBase = process.env.CLIMBER_BASE || 'http://localhost:5179';
const pin = process.env.PIN || '1234';

const sleep = (ms) => new Promise((done) => setTimeout(done, ms));

function readState() {
  try {
    return JSON.parse(readFileSync(statePath, 'utf8'));
  } catch {
    return null;
  }
}

function writeState(state) {
  mkdirSync(stateDir, { recursive: true });
  writeFileSync(statePath, JSON.stringify(state, null, 2));
}

function log(line) {
  try {
    mkdirSync(stateDir, { recursive: true });
    appendFileSync(logPath, `${new Date().toISOString()} ${line}\n`);
  } catch {
    // Logging must never take the driver down.
  }
}

async function probe(state, timeout = 1500) {
  if (!state?.port || !state?.token) return null;
  try {
    const response = await fetch(`http://127.0.0.1:${state.port}/status`, {
      headers: { 'x-climber-token': state.token },
      signal: AbortSignal.timeout(timeout),
    });
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

async function settle(page) {
  // `/` and the AuthGate pages both render "Establishing session…" before deciding, and that can
  // take a few seconds when the page also loads data. Wait for the decision, not a fixed delay.
  const deadline = Date.now() + 10000;
  while (Date.now() < deadline) {
    if ((await page.locator('#pin').count()) > 0) {
      await page.fill('#pin', pin);
      // `/` renders its own gate with a shadcn button; `/status` and `/activity` use AuthGate's.
      // The accessible name is the one thing both share.
      await page.getByRole('button', { name: /unlock/i }).first().click();
      await page.waitForSelector('#pin', { state: 'detached', timeout: 10000 }).catch(() => {});
      return (await page.locator('#pin').count()) === 0;
    }

    const text = await page.locator('body').innerText().catch(() => '');
    if (!/Establishing session/i.test(text)) return true; // resolved, and no gate was needed
    await sleep(150);
  }

  return (await page.locator('#pin').count()) === 0;
}

async function settleContent(page) {
  // A screenshot taken while the page is still fetching its first read shows a placeholder
  // instead of the page. Wait out the two known placeholders; other pages pass immediately.
  await page
    .waitForFunction(
      () => !/Checking providers|Reading the repository/i.test(document.body.innerText),
      { timeout: 8000 })
    .catch(() => {});
}

function attach(context) {
  const page = context.pages()[0] ?? (context.newPage());
  const errors = [];
  let loads = 0;

  page.on('load', () => { loads += 1; });
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(`console: ${message.text()}`);
  });
  page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));
  page.on('requestfailed', (request) => {
    const failure = request.failure();
    if (!failure) return;
    if (failure.errorText === 'net::ERR_ABORTED') return;
    errors.push(`requestfailed: ${request.url()} (${failure.errorText})`);
  });

  return { page, errors, loads: () => loads };
}

async function serve({ headless, route }) {
  const require = createRequire(import.meta.url);
  let chromium;
  try {
    ({ chromium } = require(join(repo, 'scripts', 'ui', 'node_modules', 'playwright')));
  } catch {
    console.error('playwright is not installed. Run: cd scripts/ui && npm install');
    process.exit(2);
  }

  mkdirSync(shotsDir, { recursive: true });
  log('launching browser');
  const context = await chromium.launchPersistentContext(profileDir, {
    headless,
    viewport: { width: 1440, height: 1000 },
    args: ['--no-first-run', '--no-default-browser-check'],
  });
  log('browser ready');
  const { page, errors, loads } = attach(context);
  page.on('close', () => log('page closed'));

  const go = async (target) => {
    await page.goto(`${hostBase}${target}`, { waitUntil: 'domcontentloaded', timeout: 20000 });
    const unlocked = await settle(page);
    return { url: page.url(), title: await page.title(), unlocked };
  };

  log(`initial route ${route}`);
  await go(route);
  log(`initial route ready: ${page.url()}`);

  const token = randomBytes(24).toString('hex');
  const server = createServer(async (request, response) => {
    const send = (code, payload) => {
      response.writeHead(code, { 'content-type': 'application/json' });
      response.end(JSON.stringify(payload));
    };

    if (request.headers['x-climber-token'] !== token) {
      send(401, { error: 'missing or wrong token' });
      return;
    }

    const url = new URL(request.url, 'http://127.0.0.1');
    const body = await new Promise((done) => {
      let text = '';
      request.on('data', (chunk) => { text += chunk; });
      request.on('end', () => {
        try { done(text ? JSON.parse(text) : {}); } catch { done({}); }
      });
    });

    try {
      switch (`${request.method} ${url.pathname}`) {
        case 'GET /status':
          send(200, {
            url: page.url(),
            title: await page.title(),
            unlocked: (await page.locator('#pin').count()) === 0,
            errors: errors.length,
            loads: loads(),
            pid: process.pid,
            headless,
          });
          return;

        case 'POST /goto':
          send(200, await go(String(body.route ?? '/')));
          return;

        case 'GET /text': {
          const limit = Number(url.searchParams.get('limit') || 20000);
          const text = await page.locator('body').innerText();
          send(200, { text: text.length > limit ? `${text.slice(0, limit)}\n…[cut]` : text });
          return;
        }

        case 'GET /errors': {
          const take = url.searchParams.get('keep') === '1' ? errors.slice() : errors.splice(0);
          send(200, { errors: take });
          return;
        }

        case 'POST /shot': {
          if (body.theme === 'light' || body.theme === 'dark') {
            await page.evaluate((value) => localStorage.setItem('hillclimber-theme', value), body.theme);
            await page.reload({ waitUntil: 'domcontentloaded' });
            await settle(page);
          }

          const current = page.viewportSize() ?? { width: 1440, height: 1000 };
          const width = Number(body.width) || current.width;
          const height = Number(body.height) || current.height;
          if (width !== current.width || height !== current.height) {
            await page.setViewportSize({ width, height });
          }

          const path = body.path
            ? resolve(String(body.path))
            : join(shotsDir, `drive-${width}x${height}-${new Date().toISOString().replace(/[:.]/g, '-')}.png`);
          mkdirSync(dirname(path), { recursive: true });
          await settleContent(page);
          await page.screenshot({ path, fullPage: Boolean(body.full) });
          send(200, {
            path,
            url: page.url(),
            viewport: `${width}x${height}`,
            theme: await page.evaluate(() => document.documentElement.getAttribute('data-theme')),
          });
          return;
        }

        case 'POST /reload': {
          await page.reload({ waitUntil: 'domcontentloaded' });
          const unlocked = await settle(page);
          send(200, { url: page.url(), unlocked });
          return;
        }

        case 'POST /stop':
          send(200, { stopping: true });
          setTimeout(async () => {
            await context.close().catch(() => {});
            server.close();
            rmSync(statePath, { force: true });
            process.exit(0);
          }, 100);
          return;

        default:
          send(404, { error: 'unknown route' });
      }
    } catch (error) {
      log(`error: ${error.message}`);
      send(500, { error: error.message });
    }
  });

  await new Promise((done) => server.listen(0, '127.0.0.1', done));
  const port = server.address().port;

  // If the window goes away, so should the control server: a driver whose page is gone cannot
  // answer anything, and leaving a state file behind only makes `status` lie about it.
  context.on('close', () => {
    log('browser closed; stopping');
    server.close();
    rmSync(statePath, { force: true });
    process.exit(0);
  });

  process.on('exit', (code) => log(`exiting with code ${code}`));

  writeState({
    pid: process.pid,
    port,
    token,
    base: hostBase,
    profile: profileDir,
    startedAt: new Date().toISOString(),
  });

  log(`listening on 127.0.0.1:${port} (headless=${headless})`);

  // The control server is the process's whole reason to exist: hold it open.
  await new Promise(() => { });

  const close = async () => {
    await context.close().catch(() => {});
    server.close();
    rmSync(statePath, { force: true });
    process.exit(0);
  };
  process.on('SIGINT', close);
  process.on('SIGTERM', close);
}

async function start({ headless, route }) {
  const state = readState();
  if (state && await probe(state)) {
    const status = await probe(state);
    console.log(`already running: ${status.url} (pid ${status.pid})`);
    return 0;
  }

  if (state) {
    // A state file with no server behind it means the last driver died without saying why.
    console.error(`the previous driver was not answering; opening a new one. Log tail:\n${tail(5)}`);
    rmSync(statePath, { force: true }); // stale
  }

  const child = spawn(
    process.execPath,
    [
      fileURLToPath(import.meta.url),
      '__serve',
      headless ? '--headless' : '--headed',
      '--route',
      route,
    ],
    { detached: true, stdio: 'ignore', windowsHide: true });

  child.unref();

  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    await sleep(400);
    const fresh = readState();
    const status = fresh && await probe(fresh);
    if (status) {
      console.log(`open on ${status.url} (pid ${status.pid}, ${headless ? 'headless' : 'headed'})`);
      return 0;
    }
  }

  console.error(`the driver did not answer. Log tail:\n${tail(10)}`);
  return 1;
}

function tail(lines) {
  try {
    return readFileSync(logPath, 'utf8').trimEnd().split(/\r?\n/).slice(-lines).join('\n');
  } catch {
    return '';
  }
}

const HELP = `Keep one browser window on the live dev host, already unlocked.

Usage: node scripts/dev/drive.mjs <command> [options]

Commands:
  start           open the window, or report the one already open
  status          the current page, unlock state, console errors and load count
  goto <route>    navigate, unlocking if the gate appears
  text            print the page's visible text (--limit N)
  errors          print and clear the console errors seen so far (--keep to leave them)
  shot            save a PNG (--full, --width N, --height N, --theme light|dark, then an optional path)
  reload          reload the page and unlock it again
  stop            close the window
  help            this text

Options:
  --headless      with start: no visible window, for scripted runs
  --route <path>  with start: the first page (default /)
  --limit <n>     with text: characters to print (default 20000)

Shots land in ${shotsDir} when no path is given.
Control state lives in ${stateDir}.`;

async function call(method, path, body) {
  const state = readState();
  if (!state) {
    console.error('the driver is not running. Start it with: node scripts/dev/drive.mjs start');
    return 1;
  }

  let response;
  try {
    response = await fetch(`http://127.0.0.1:${state.port}${path}`, {
      method,
      headers: { 'x-climber-token': state.token, 'content-type': 'application/json' },
      body: body ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(30000),
    });
  } catch {
    console.error('the driver is not answering. Restart it with: node scripts/dev/drive.mjs start');
    return 1;
  }

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    console.error(`${response.status}: ${payload.error ?? 'request failed'}`);
    return 1;
  }

  if ('text' in payload) {
    console.log(payload.text);
  } else if ('errors' in payload) {
    if (payload.errors.length === 0) {
      console.log('no console errors');
    } else {
      // A reconnect storm can produce hundreds of identical lines; show the shape, not the volume.
      for (const line of payload.errors.slice(0, 15)) console.log(line);
      if (payload.errors.length > 15) {
        console.log(`…and ${payload.errors.length - 15} more`);
      }
    }
  } else {
    console.log(JSON.stringify(payload, null, 2));
  }

  return 0;
}

const [command = 'help', ...rest] = process.argv.slice(2);
const headless = rest.includes('--headless');
const routeAt = rest.indexOf('--route');
const route = routeAt >= 0 ? rest[routeAt + 1] : '/';

const code = await (async () => {
  switch (command) {
    case '__serve':
      await serve({ headless, route });
      return 0;
    case 'start':
      return start({ headless, route });
    case 'stop': {
      const state = readState();
      if (!state) {
        console.log('not running');
        return 0;
      }

      await call('POST', '/stop');

      // The server answers before it closes, so wait for it to actually go away: a start that
      // races the shutdown would find a live state file and refuse to open a window.
      const deadline = Date.now() + 8000;
      while (Date.now() < deadline) {
        if (!(await probe(state, 500))) {
          rmSync(statePath, { force: true });
          console.log('stopped');
          return 0;
        }
        await sleep(150);
      }

      console.error('the driver did not stop');
      return 1;
    }
    case 'status': {
      const state = readState();
      if (!state) {
        console.log('not running');
        return 1;
      }
      const status = await probe(state);
      if (!status) {
        console.log('not answering; start it again');
        return 1;
      }
      console.log(JSON.stringify(status, null, 2));
      return 0;
    }
    case 'goto':
      return call('POST', '/goto', { route: rest[0] || '/' });
    case 'text': {
      const limitAt = rest.indexOf('--limit');
      const limit = limitAt >= 0 ? rest[limitAt + 1] : undefined;
      return call('GET', `/text${limit ? `?limit=${encodeURIComponent(limit)}` : ''}`);
    }
    case 'errors':
      return call('GET', `/errors${rest.includes('--keep') ? '?keep=1' : ''}`);
    case 'shot': {
      const value = (name) => {
        const at = rest.indexOf(name);
        return at >= 0 ? rest[at + 1] : undefined;
      };
      const path = rest.find(
        (item, index) => !item.startsWith('--') && !['--width', '--height', '--theme'].includes(rest[index - 1]));
      const full = rest.includes('--full');
      return call('POST', '/shot', {
        path,
        full,
        width: value('--width'),
        height: value('--height'),
        theme: value('--theme'),
      });
    }
    case 'reload':
      return call('POST', '/reload');
    case 'help':
    case '--help':
    case '-h':
      console.log(HELP);
      return 0;
    default:
      console.error(`unknown command "${command}".\n`);
      console.error(HELP);
      return 2;
  }
})();

process.exit(code);