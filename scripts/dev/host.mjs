#!/usr/bin/env node
// Start, stop and inspect the live dev host.
//
// The host is one long-lived `dotnet watch` on 5179 whose output goes to a temp directory, so
// `dotnet test`, a plain build and the UI harnesses can use the repo's own bin while the app
// keeps running. Hot reload is an optimisation here, not a guarantee: watch has silently
// dropped a change and left a page broken while reporting success, so the loop asks the host
// for its stamp instead of assuming.
//
//   node scripts/dev/host.mjs start [--force]
//   node scripts/dev/host.mjs stop
//   node scripts/dev/host.mjs restart
//   node scripts/dev/host.mjs status

import { spawn, spawnSync } from 'node:child_process';
import {
  appendFileSync,
  closeSync,
  existsSync,
  mkdirSync,
  openSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, '..', '..');
const port = Number(process.env.CLIMBER_PORT || 5179);
const base = `http://localhost:${port}`;
const stateDir = join(tmpdir(), 'climb-ui', 'host');
const statePath = join(stateDir, 'state.json');
const logPath = join(stateDir, 'watch.log');
const devOutput = join(tmpdir(), 'climb-ui', 'dev', 'bin') + sep;
const msbuildPath = devOutput.replaceAll('\\', '/');

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

function alive(pid) {
  if (!pid) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

async function stamp() {
  try {
    const response = await fetch(`${base}/__dev/stamp`, {
      signal: AbortSignal.timeout(1500),
    });
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

function portOwners() {
  // Listeners only. Parsing `netstat` blindly also matches established client connections,
  // whose owner is this very script — a `--force` would then kill the caller.
  if (process.platform === 'win32') {
    const result = spawnSync(
      'powershell',
      [
        '-NoProfile',
        '-Command',
        `Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess`,
      ],
      { encoding: 'utf8', windowsHide: true });

    return [...new Set(
      (result.stdout ?? '')
        .split(/\s+/)
        .map(Number)
        .filter((pid) => Number.isInteger(pid) && pid > 0 && pid !== process.pid))];
  }

  const result = spawnSync('lsof', ['-ti', `:${port}`], { encoding: 'utf8' });
  return [...new Set(
    (result.stdout ?? '')
      .split(/\s+/)
      .map(Number)
      .filter((pid) => Number.isInteger(pid) && pid > 0 && pid !== process.pid))];
}

function killTree(pid) {
  if (!pid) return;
  spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true });
}

async function waitForUp(ms) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (await stamp()) return true;
    await sleep(400);
  }
  return false;
}

async function waitForDown(ms) {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (!(await stamp()) && portOwners().length === 0) return true;
    await sleep(400);
  }
  return false;
}

function tail(lines) {
  if (!existsSync(logPath)) return '';
  const text = readFileSync(logPath, 'utf8').trimEnd().split(/\r?\n/);
  return text.slice(-lines).join('\n');
}

function renderErrors() {
  if (!existsSync(logPath)) return 0;
  const text = readFileSync(logPath, 'utf8');
  return (text.match(/Unhandled exception rendering component/g) ?? []).length;
}

async function start({ force = false } = {}) {
  const state = readState();
  if (await stamp()) {
    if (state?.watchPid && alive(state.watchPid)) {
      console.log(`already up on ${base} (watch pid ${state.watchPid}, log ${logPath})`);
      return 0;
    }

    if (!force) {
      console.error(
        `${base} is already answering, but this script did not start it` +
          `${portOwners().length ? ` (pid ${portOwners().join(', ')})` : ''}.` +
          '\nPass --force to stop it and take the port.');
      return 2;
    }

    for (const pid of portOwners()) killTree(pid);
    if (!(await waitForDown(15000))) {
      console.error(`${base} is still in use after --force.`);
      return 1;
    }
  } else if (state?.watchPid && alive(state.watchPid)) {
    // A previous watch survived but stopped answering: drop it before starting another.
    killTree(state.watchPid);
    await waitForDown(10000);
  }

  mkdirSync(stateDir, { recursive: true });
  appendFileSync(logPath, `\n=== ${new Date().toISOString()} start ===\n`);
  const log = openSync(logPath, 'a');

  const child = spawn(
    'dotnet',
    [
      'watch',
      '--project',
      'ClimberWeb',
      '--no-launch-profile',
      '--non-interactive',
      `--property:BaseOutputPath=${msbuildPath}`,
    ],
    {
      cwd: repo,
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: `http://localhost:${port}`,
      },
      detached: true,
      stdio: ['ignore', log, log],
      windowsHide: true,
    });

  child.unref();
  closeSync(log);

  writeState({
    watchPid: child.pid,
    startedAt: new Date().toISOString(),
    port,
    base,
    log: logPath,
  });

  if (!(await waitForUp(90000))) {
    console.error(`the host did not answer on ${base}. Last log lines:\n${tail(20)}`);
    killTree(child.pid);
    rmSync(statePath, { force: true });
    return 1;
  }

  console.log(`up on ${base} (watch pid ${child.pid})`);
  console.log(`log: ${logPath}`);
  return 0;
}

async function stop() {
  const state = readState();
  const owners = portOwners();

  if (!state?.watchPid && owners.length === 0) {
    console.log(`not running (${base} is free)`);
    rmSync(statePath, { force: true });
    return 0;
  }

  if (state?.watchPid) killTree(state.watchPid);
  for (const pid of owners) killTree(pid);

  if (!(await waitForDown(20000))) {
    console.error(`something still holds ${base} after stop.`);
    return 1;
  }

  rmSync(statePath, { force: true });
  console.log(`stopped (${base} is free)`);
  return 0;
}

async function status() {
  const state = readState();
  const current = await stamp();
  const owners = portOwners();

  if (!current) {
    console.log(`${base}: down${owners.length ? ` (held by pid ${owners.join(', ')})` : ''}`);
    return 1;
  }

  console.log(`${base}: up`);
  console.log(`watch pid: ${state?.watchPid ?? 'unknown'}${alive(state?.watchPid) ? '' : ' (not running)'}`);
  console.log(`app pid: ${current.pid}`);
  console.log(`build: ${current.build ?? 'unknown'}`);
  console.log(`source: ${current.source ?? 'unknown'}`);
  console.log(`started: ${current.startedAt}`);
  console.log(`render errors in this log: ${renderErrors()}`);
  console.log(`log: ${logPath}`);
  return 0;
}

const [command = 'status', ...rest] = process.argv.slice(2);
const force = rest.includes('--force');

const code = await (async () => {
  switch (command) {
    case 'start':
      return start({ force });
    case 'stop':
      return stop();
    case 'restart':
      return (await stop()) === 0 ? start({ force: true }) : 1;
    case 'status':
      return status();
    default:
      console.error(`unknown command "${command}". Use start, stop, restart or status.`);
      return 2;
  }
})();

process.exit(code);