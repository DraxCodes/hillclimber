#!/usr/bin/env node
// Install the optional engineering skill pack described in pack.json beside this
// file, using the skills CLI (https://skills.sh).
//
// Project scope: the skills land in this project (.agents/skills/, which
// OpenCode reads) and never touch ~/.config. Commit them or ignore them; that
// is the project's choice.
//
// Usage:  node .opencode/tools/base.mjs pack install engineering .
//   or:   node .opencode/packs/engineering/install.mjs

import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const manifestPath = join(here, "pack.json");

let manifest;
try {
  manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
} catch (error) {
  console.error(`Could not read ${manifestPath}: ${error.message}`);
  process.exit(1);
}

const agent = manifest.agent ?? "opencode";
const cli = manifest.cli ?? "skills@1.9.0";
const isWindows = process.platform === "win32";
let failed = false;

const SAFE_NAME = /^[A-Za-z0-9_.-]+$/;
const SAFE_REPO = /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/;

function fail(message) {
  console.error(message);
  process.exit(1);
}

for (const { repo, skills = [] } of manifest.sources ?? []) {
  if (!SAFE_REPO.test(repo)) fail(`Refusing unsafe repo name: ${repo}`);
  for (const skill of skills) {
    if (!SAFE_NAME.test(skill)) fail(`Refusing unsafe skill name: ${skill}`);
  }
  const args = ["--yes", cli, "add", repo];
  for (const skill of skills) args.push("--skill", skill);
  args.push("--agent", agent, "--yes");

  console.log(`\n> npx ${args.join(" ")}`);
  const result = spawnSync("npx", args, { stdio: "inherit", shell: isWindows });

  if (result.status !== 0) {
    failed = true;
    console.error(`\nFailed to install from ${repo} (exit code ${result.status ?? "unknown"}).`);
  }
}

if (failed) {
  process.exit(1);
}

console.log("\nEngineering pack installed (project scope).");
console.log("Run /setup-matt-pocock-skills once to set up your issue tracker and labels.");
