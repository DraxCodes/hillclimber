#!/usr/bin/env node
// One persistent, already-authenticated browser window over the live dev host.
//
//   node scripts/dev/drive.mjs start [--headless] [--route /]
//   node scripts/dev/drive.mjs status
//   node scripts/dev/drive.mjs goto /activity
//   node scripts/dev/drive.mjs text [--limit 4000]
//   node scripts/dev/drive.mjs errors [--keep]
//   node scripts/dev/drive.mjs shot [--full] [path]
//   node scripts/dev/drive.mjs reload
//   node scripts/dev/drive.mjs stop
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
          const path = body.path
            ? resolve(String(body.path))
            : join(shotsDir, `drive-${new Date().toISOString().replace(/[:.]/g, '-')}.png`);
          mkdirSync(dirname(path), { recursive: true });
          await page.screenshot({ path, fullPage: Boolean(body.full) });
          send(200, { path, url: page.url() });
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

  if (state) rmSync(statePath, { force: true }); // a state file without a server is stale

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
    if (payload.errors.length === 0) console.log('no console errors');
    else for (const line of payload.errors) console.log(line);
  } else {
    console.log(JSON.stringify(payload, null, 2));
  }

  return 0;
}

const [command = 'status', ...rest] = process.argv.slice(2);
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
      const path = rest.find((value) => !value.startsWith('--'));
      const full = rest.includes('--full');
      return call('POST', '/shot', { path, full });
    }
    case 'reload':
      return call('POST', '/reload');
    default:
      console.error(`unknown command "${command}". See the header of scripts/dev/drive.mjs.`);
      return 2;
  }
})();

process.exit(code);