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

function currentRun() {
  if (!existsSync(logPath)) return '';
  const text = readFileSync(logPath, 'utf8');
  // Only the current run: the log is appended to across starts.
  const marker = text.lastIndexOf('=== ');
  return marker >= 0 ? text.slice(marker) : text;
}

function renderErrors() {
  return (currentRun().match(/Unhandled exception rendering component/g) ?? []).length;
}

function lastFailure() {
  const text = currentRun();
  const known = [
    /Failed to bind to address [^\r\n:]+[^\r\n]*/,
    /Exited with error code -?\d+/,
    /Unhandled exception\. [^\r\n]*/,
  ];

  for (const pattern of known) {
    const matches = text.match(pattern);
    if (matches?.length) return matches[matches.length - 1].trim();
  }
  return null;
}

function gloss(iso) {
  if (!iso) return 'unknown';
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return iso;

  const seconds = Math.max(0, Math.round((Date.now() - then) / 1000));
  const clock = new Date(then).toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });

  let age;
  if (seconds < 60) age = `${seconds} s`;
  else if (seconds < 3600) age = `${Math.round(seconds / 60)} min`;
  else age = `${(seconds / 3600).toFixed(1)} h`;

  return `${clock} (${age} ago)`;
}

const HELP = `Start, stop and inspect the live dev host on ${base}.

Usage: node scripts/dev/host.mjs <command> [options]

Commands:
  start           start the watched host, or report that it is already up
  stop            stop it and wait for the port to be free
  restart         stop, then start again (the recovery for a wedged watch)
  status          say what is actually serving, and what to do if it is not

Options:
  --force         with start: take the port from a process this script did not start

Files: log ${logPath}
       state ${statePath}`;

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
        // The app locates the repository from its own directory, which is the temp output here.
        // `/status` reads this key, so the dev host still shows the real branch and history.
        Climber__RepoRoot: repo,
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
  const watchPid = state?.watchPid ?? null;
  const watchUp = alive(watchPid);
  const failure = lastFailure();

  if (!current) {
    console.log(`${base}: down`);
    if (owners.length) {
      console.log(`port holder: pid ${owners.join(', ')} (answering nothing)`);
      console.log('next: node scripts/dev/host.mjs start --force');
    } else if (watchUp) {
      console.log(`watch pid: ${watchPid} (running, not answering yet)`);
      console.log('next: node scripts/dev/host.mjs status (again in a moment), or restart');
    } else {
      if (failure) console.log(`last failure: ${failure}`);
      console.log('next: node scripts/dev/host.mjs start');
    }
    return 1;
  }

  console.log(`${base}: up`);
  console.log(`watch: ${watchUp ? `pid ${watchPid}` : `pid ${watchPid ?? 'unknown'} — not running`}`);
  console.log(`app pid: ${current.pid}`);
  console.log(`build: ${gloss(current.build)}`);
  console.log(`source: ${gloss(current.source)}`);
  console.log(`started: ${gloss(current.startedAt)}`);
  console.log(`render errors this run: ${renderErrors()}`);
  if (failure) console.log(`last failure in this run: ${failure}`);
  console.log(`log: ${logPath}`);

  if (!watchUp) {
    // Something is serving, but this script can no longer restart it. Say so instead of calling
    // that "up": a save will not apply, and the next failure has no recovery.
    console.log('note: nothing is watching for changes; edits will not be applied');
    console.log('next: node scripts/dev/host.mjs restart');
    return 1;
  }

  return 0;
}

async function help() {
  console.log(HELP);
  return 0;
}

const [command = 'help', ...rest] = process.argv.slice(2);
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
    case 'help':
    case '--help':
    case '-h':
      return help();
    default:
      console.error(`unknown command "${command}".\n`);
      console.error(HELP);
      return 2;
  }
})();

process.exit(code);