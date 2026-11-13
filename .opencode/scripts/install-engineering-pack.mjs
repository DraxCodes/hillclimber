#!/usr/bin/env node
// Install the optional engineering skill pack described in
// .opencode/engineering-pack.json, using the skills CLI (https://skills.sh).
//
// Project scope: the skills land in this project (.agents/skills/, which
// OpenCode reads), so they travel with the repo and never touch ~/.config.
//
// Usage:  node .opencode/scripts/install-engineering-pack.mjs

import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const here = dirname(fileURLToPath(import.meta.url));
const manifestPath = join(here, "..", "engineering-pack.json");

let manifest;
try {
  manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
} catch (error) {
  console.error(`Could not read ${manifestPath}: ${error.message}`);
  process.exit(1);
}

const agent = manifest.agent ?? "opencode";
const isWindows = process.platform === "win32";
let failed = false;

for (const { repo, skills = [] } of manifest.sources ?? []) {
  const args = ["--yes", "skills@latest", "add", repo];
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
