import { spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { applyPlan } from "./apply.mjs";
import { doctor } from "./doctor.mjs";
import { sha256 } from "./hash.mjs";
import {
  MANIFEST_REL,
  listBaseFiles,
  manifestContent,
  manifestFor,
  projectFile,
  readBaseFiles,
  readManifest,
  renderBlock,
} from "./manifest.mjs";
import { planCheck, planEject, planInit, planUpdate } from "./plan.mjs";
import { SEAM_REL, defaultSeam, loadSeam, validateSeam } from "./seam.mjs";

const REPEATABLE = new Set(["free", "paid", "pack"]);

function parseFlags(argv) {
  const flags = { _: [] };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) {
      flags._.push(arg);
      continue;
    }
    const body = arg.slice(2);
    const eq = body.indexOf("=");
    const key = eq === -1 ? body : body.slice(0, eq);
    const camel = key.replace(/-([a-z])/g, (_, c) => c.toUpperCase());
    let value;
    if (eq !== -1) {
      value = body.slice(eq + 1);
    } else if (i + 1 < argv.length && !argv[i + 1].startsWith("--")) {
      value = argv[i + 1];
      i += 1;
    } else {
      value = true;
    }
    if (REPEATABLE.has(key)) {
      flags[camel] = [...(flags[camel] ?? []), value];
    } else {
      flags[camel] = value;
    }
  }
  return flags;
}

const toArray = (value) => {
  if (value === undefined) return [];
  return Array.isArray(value) ? value : [value];
};

function readVersion(root) {
  const file = join(root, "VERSION");
  return existsSync(file) ? readFileSync(file, "utf8").trim() : null;
}

function gitCommit(root) {
  const result = spawnSync("git", ["-C", root, "rev-parse", "--short", "HEAD"], {
    encoding: "utf8",
  });
  return result.status === 0 ? result.stdout.trim() : "";
}

function isBaseRoot(dir) {
  return (
    Boolean(dir) &&
    existsSync(join(dir, "VERSION")) &&
    existsSync(join(dir, "AGENTS.base.md"))
  );
}

function sourceRoot(flags) {
  const candidates = [];
  if (typeof flags.from === "string") candidates.push(resolve(flags.from));
  if (process.env.WORKFLOW_BASE) candidates.push(resolve(process.env.WORKFLOW_BASE));
  candidates.push(resolve(dirname(fileURLToPath(import.meta.url)), "..", ".."));
  candidates.push(process.cwd());
  for (const dir of candidates) {
    if (isBaseRoot(dir)) return dir;
  }
  return null;
}

function requireSource(flags) {
  const root = sourceRoot(flags);
  if (!root) {
    throw new Error(
      "no base checkout found; run from the opencode-workflows repo, set WORKFLOW_BASE, or pass --from <dir>",
    );
  }
  return root;
}

function renderBaseBlock(baseRoot, version) {
  const template = readFileSync(join(baseRoot, "AGENTS.base.md"), "utf8");
  return renderBlock(template, version);
}

function printReport(title, { errors = [], warnings = [], notes = [] }) {
  console.log(title);
  for (const note of notes) console.log(`  · ${note}`);
  for (const warning of warnings) console.log(`  ! ${warning}`);
  for (const error of errors) console.log(`  ✗ ${error}`);
}

function initCommand(flags) {
  const baseRoot = requireSource(flags);
  const target = resolve(flags._[0] ?? ".");
  const version = readVersion(baseRoot);
  const packs = toArray(flags.pack);
  const project = flags.project ?? basename(target);

  const seam = defaultSeam({ project, baseVersion: version });
  if (flags.build) seam.checks.build = { command: flags.build, cwd: flags.buildCwd ?? "." };
  if (flags.test) seam.checks.test = { command: flags.test, cwd: flags.testCwd ?? "." };
  seam.checks.free = toArray(flags.free).map((command) => ({
    command,
    cwd: flags.freeCwd ?? ".",
  }));
  seam.checks.paid = toArray(flags.paid).map((command) => ({
    command,
    cwd: flags.paidCwd ?? ".",
  }));
  if (flags.surface) seam.verify.surface = flags.surface;
  if (flags.seed) seam.verify.seed = flags.seed;
  if (flags.capture) seam.verify.capture = flags.capture;
  if (flags.captureCwd) seam.verify.captureCwd = flags.captureCwd;
  if (flags.issues) seam.docs.issues = flags.issues;
  if (flags.glossary) seam.docs.glossary = flags.glossary;
  if (flags.adr) seam.docs.adr = flags.adr;
  if (flags.rubricAddendum) seam.docs.rubricAddendum = flags.rubricAddendum;
  if (flags.contract) seam.docs.contract = flags.contract;
  if (flags.artDirection) seam.docs.artDirection = flags.artDirection;
  seam.packs = packs;

  const { errors } = validateSeam(seam);
  if (errors.length > 0) {
    printReport("The seam would be invalid:", { errors });
    return 2;
  }

  const files = readBaseFiles(baseRoot, listBaseFiles(baseRoot, packs));
  const block = renderBaseBlock(baseRoot, version);
  const plan = planInit({
    projectDir: target,
    files,
    seamContent: `${JSON.stringify(seam, null, 2)}\n`,
    block,
    force: Boolean(flags.force),
  });

  if (plan.conflicts.length > 0) {
    printReport("Refusing to initialize:", { errors: plan.conflicts });
    console.log("\nUse --force to replace the differing files.");
    return 1;
  }

  const manifest = manifestFor(files, {
    version,
    commit: gitCommit(baseRoot),
    packs,
    block,
  });
  plan.writes.push({
    path: MANIFEST_REL,
    content: manifestContent(manifest),
    kind: "manifest",
  });

  const result = applyPlan(target, plan, { dryRun: Boolean(flags.dryRun) });
  if (flags.dryRun) {
    console.log(`\nDry run: would write ${plan.writes.length} files into ${target}`);
    return 0;
  }

  console.log(
    `Initialized ${project} at ${target} with opencode-workflows ${version}.`,
  );
  console.log(`  files: ${result.written.length} written, ${result.deleted.length} removed`);
  if (result.backupDir) console.log(`  backup: ${result.backupDir}`);
  console.log("\nNext:");
  console.log(`  1. Fill .opencode/workflow.json (checks, verify, docs).`);
  console.log(`  2. Run: node .opencode/tools/base.mjs doctor .`);
  console.log(`  3. Merge the fragment from: node .opencode/tools/base.mjs print-config .`);
  return 0;
}

function updateCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const manifest = readManifest(target);
  if (!manifest) {
    console.error(`${MANIFEST_REL}: missing; run \`base init\` first`);
    return 1;
  }
  const source = requireSource(flags);
  const version = readVersion(source);
  const { seam } = loadSeam(target);
  const packs = flags.pack ? toArray(flags.pack) : (manifest.packs ?? []);
  const files = readBaseFiles(source, listBaseFiles(source, packs));
  const block = renderBaseBlock(source, version);

  const plan = planUpdate({
    projectDir: target,
    files,
    oldManifest: manifest,
    newVersion: version,
    commit: gitCommit(source),
    block,
    packs,
    force: Boolean(flags.force),
  });

  const checkOnly = Boolean(flags.check) || Boolean(flags.dryRun);
  if (checkOnly) {
    console.log(`${manifest.version} -> ${version} (${packs.length} pack${packs.length === 1 ? "" : "s"})`);
    for (const write of plan.writes) console.log(`  write ${write.path}`);
    for (const del of plan.deletes) console.log(`  delete ${del}`);
    for (const conflict of plan.conflicts) console.log(`  ✗ ${conflict}`);
    if (plan.conflicts.length > 0) return 1;
    return plan.writes.length > 0 || plan.deletes.length > 0 ? 1 : 0;
  }

  if (plan.conflicts.length > 0) {
    printReport("Refusing to update:", { errors: plan.conflicts });
    return 1;
  }

  const result = applyPlan(target, plan);
  console.log(`Updated ${manifest.version} -> ${version}.`);
  console.log(`  files: ${result.written.length} written, ${result.deleted.length} removed`);
  if (result.backupDir) console.log(`  backup: ${result.backupDir}`);
  return 0;
}

function checkCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const manifest = readManifest(target);
  if (!manifest) {
    console.error(`${MANIFEST_REL}: missing; run \`base init\` first`);
    return 1;
  }
  const { seam, errors } = loadSeam(target);
  const result = planCheck({
    projectDir: target,
    oldManifest: manifest,
    seamVersion: seam?.base?.version,
  });
  const problems = [...errors, ...result.problems];
  console.log(`opencode-workflows ${manifest.version} at ${target}`);
  if (result.clean.length > 0) console.log(`  clean: ${result.clean.length} files`);
  for (const file of result.changed) console.log(`  changed: ${file}`);
  for (const file of result.missing) console.log(`  missing: ${file}`);
  for (const problem of problems) console.log(`  ✗ ${problem}`);

  const bad =
    result.changed.length + result.missing.length + problems.length > 0;
  console.log(bad ? "\nDrift found." : "\nClean.");
  return bad ? 1 : 0;
}

function doctorCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const result = doctor(target, flags.home ? { home: resolve(flags.home) } : {});
  printReport(`Doctor: ${target}`, result);
  const ok = result.errors.length === 0;
  console.log(ok ? "\nNo errors." : "\nErrors found.");
  return ok ? 0 : 1;
}

function statusCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const manifest = readManifest(target);
  if (!manifest) {
    console.error(`${MANIFEST_REL}: missing; run \`base init\` first`);
    return 1;
  }
  const { seam } = loadSeam(target);
  console.log(`opencode-workflows ${manifest.version}`);
  console.log(`  commit: ${manifest.commit || "(unknown)"}`);
  console.log(`  installed: ${manifest.installedAt}`);
  console.log(`  packs: ${(manifest.packs ?? []).join(", ") || "(none)"}`);
  if (seam) {
    console.log(`  project: ${seam.project}`);
    console.log(`  workflows: ${(seam.workflows ?? []).join(", ") || "(none)"}`);
    console.log(`  surface: ${seam.verify?.surface ?? "(unset)"}`);
  }
  let clean = 0;
  let modified = 0;
  let missing = 0;
  for (const [dest, hash] of Object.entries(manifest.files)) {
    const abs = projectFile(target, dest);
    if (!existsSync(abs)) {
      console.log(`  missing: ${dest}`);
      missing += 1;
    } else if (sha256(readFileSync(abs, "utf8")) !== hash) {
      console.log(`  modified: ${dest}`);
      modified += 1;
    } else {
      clean += 1;
    }
  }
  console.log(`  files: ${clean} clean, ${modified} modified, ${missing} missing`);
  return modified + missing > 0 ? 1 : 0;
}

function ejectCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const manifest = readManifest(target);
  if (!manifest) {
    console.error(`${MANIFEST_REL}: missing; nothing to eject`);
    return 1;
  }
  const plan = planEject({ projectDir: target, manifest, force: Boolean(flags.force) });
  if (plan.conflicts.length > 0) {
    printReport("Refusing to eject:", { errors: plan.conflicts });
    return 1;
  }
  const result = applyPlan(target, plan, { dryRun: Boolean(flags.dryRun) });
  if (flags.dryRun) return 0;
  console.log(`Ejected opencode-workflows ${manifest.version}.`);
  console.log(`  removed: ${result.deleted.length} files`);
  console.log(`  left in place: ${SEAM_REL} (the project seam)`);
  return 0;
}

function printConfigCommand(flags) {
  const target = resolve(flags._[0] ?? ".");
  const { seam, errors } = loadSeam(target);
  if (!seam && errors.length > 0) {
    printReport("No valid seam:", { errors });
    return 1;
  }
  const deny = [
    { action: "edit", resource: "*", effect: "deny" },
  ];
  const fragment = {
    model: "<provider/model>",
    agents: {
      "task-orchestrator": { model: "<provider/model#low>" },
      "plan-architect": { model: "<provider/model#medium>", permissions: deny },
      "quality-gatekeeper": { model: "<provider/model#medium>", permissions: deny },
      "security-auditor": { model: "<provider/model#medium>", permissions: deny },
    },
  };
  console.log(JSON.stringify(fragment, null, 2));
  console.error(
    "\nMerge this into opencode.json. The base never writes opencode.json; the placeholders are yours to replace.",
  );
  return 0;
}

function packCommand(flags) {
  const [sub, name, targetArg] = flags._;
  const target = resolve(targetArg ?? ".");
  if (sub !== "install" || !name) {
    console.error("usage: base pack install <name> [target]");
    return 2;
  }
  const script = join(target, ".opencode", "packs", name, "install.mjs");
  if (!existsSync(script)) {
    console.error(`pack ${name} is not vendored in ${target}`);
    return 1;
  }
  const result = spawnSync(process.execPath, [script], { cwd: target, stdio: "inherit" });
  return result.status ?? 1;
}

const HELP = `opencode-workflows base

Usage:
  base init [target] [flags]       install a pinned base into a project
  base update [target] [--check]   bump the base to the current source version
  base check [target]              report drift against the manifest
  base doctor [target]             validate the seam, config and global shadowing
  base status [target]             show the version, files and seam summary
  base eject [target]              remove the base-owned files and the AGENTS block
  base print-config [target]       print the opencode.json fragment to merge
  base pack install <name> [target]
  base version

Flags:
  --from <dir>         source checkout (or WORKFLOW_BASE)
  --force              overwrite modified or differing files
  --dry-run            print the plan, write nothing
  --project <name>     seam project name
  --build/--test       seam check commands
  --free/--paid        repeatable seam check commands
  --surface <kind>     web | cli | api | docs | none
  --seed/--capture     visual verification hints
  --pack <name>        repeatable pack selection
`;

export function run(argv = process.argv.slice(2)) {
  const [command, ...rest] = argv;
  const flags = parseFlags(rest);

  try {
    switch (command) {
      case "init":
        return initCommand(flags);
      case "update":
        return updateCommand(flags);
      case "check":
        return checkCommand(flags);
      case "doctor":
        return doctorCommand(flags);
      case "status":
        return statusCommand(flags);
      case "eject":
        return ejectCommand(flags);
      case "print-config":
        return printConfigCommand(flags);
      case "pack":
        return packCommand(flags);
      case "version":
      case "--version":
      case "-v": {
        const root = sourceRoot(flags);
        console.log(root ? readVersion(root) : "unknown (vendored)");
        return 0;
      }
      case undefined:
      case "help":
      case "--help":
      case "-h":
        console.log(HELP);
        return 0;
      default:
        console.error(`unknown command: ${command}\n`);
        console.log(HELP);
        return 2;
    }
  } catch (error) {
    if (error.conflicts) {
      printReport("Refusing to write:", { errors: error.conflicts });
    } else {
      console.error(error.message);
    }
    return 1;
  }
}