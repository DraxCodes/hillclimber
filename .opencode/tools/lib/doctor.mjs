import { existsSync, readFileSync, readdirSync } from "node:fs";
import { homedir } from "node:os";
import { dirname, join } from "node:path";
import { sha256 } from "./hash.mjs";
import {
  MANIFEST_REL,
  extractBlock,
  projectFile,
  readManifest,
  toPosix,
} from "./manifest.mjs";
import { loadSeam } from "./seam.mjs";

const READ_ONLY_AGENTS = ["plan-architect", "quality-gatekeeper", "security-auditor"];
const ALL_AGENTS = ["task-orchestrator", ...READ_ONLY_AGENTS];

function walk(dir) {
  const out = [];
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const abs = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(abs));
    else if (entry.isFile()) out.push(abs);
  }
  return out;
}

function readJson(file) {
  try {
    return { value: JSON.parse(readFileSync(file, "utf8")), error: null };
  } catch (error) {
    return { value: null, error };
  }
}

function checkDocs(seam, projectDir, errors, warnings, notes) {
  if (!seam.docs) return;
  const optional = ["glossary", "rubricAddendum", "contract", "artDirection"];
  for (const [key, rel] of Object.entries(seam.docs)) {
    const abs = join(projectDir, rel);
    if (existsSync(abs)) {
      notes.push(`docs.${key}: ${rel}`);
    } else if (optional.includes(key)) {
      warnings.push(`docs.${key}: ${rel} does not exist (optional)`);
    } else {
      warnings.push(`docs.${key}: ${rel} does not exist yet`);
    }
  }
}

function checkConfig(projectDir, agentIds, errors, warnings, notes) {
  const file = join(projectDir, "opencode.json");
  if (!existsSync(file)) {
    warnings.push("opencode.json: not found; the project model and permissions are unset");
    return;
  }
  const { value, error } = readJson(file);
  if (error) {
    warnings.push(`opencode.json: could not parse (${error.message}); skipped config checks`);
    return;
  }
  const rootModel = value.model;
  const agents = value.agents ?? {};
  let anyModel = Boolean(rootModel);
  for (const id of agentIds) {
    if (agents[id]?.model) anyModel = true;
  }
  if (!anyModel) {
    warnings.push(
      "opencode.json: no model; set a root `model` or per-agent `agents.<id>.model` (base agents inherit)",
    );
  }

  for (const id of READ_ONLY_AGENTS) {
    const rules = agents[id]?.permissions ?? [];
    const denied = rules.some(
      (rule) =>
        rule.action === "edit" &&
        rule.effect === "deny" &&
        (rule.resource === "*" || rule.resource === "**"),
    );
    if (!denied) {
      warnings.push(
        `opencode.json: agents.${id} has no edit-deny; the read-only guarantee relies on it`,
      );
    }
  }

  if (value.permissions) notes.push("opencode.json: project permissions present");
}

function checkGlobalShadowing(home, agentIds, errors, notes) {
  const agentsDir = join(home, ".config", "opencode", "agents");
  for (const id of agentIds) {
    const file = join(agentsDir, `${id}.md`);
    if (existsSync(file)) {
      errors.push(
        `global agent ${id} at ${toPosix(file)} shadows the vendored agent (move it aside)`,
      );
    }
  }
  if (errors.length === 0) notes.push("global agents: no same-ID shadowing");
}

const SECRET_PATTERNS = [
  { re: /-----BEGIN [A-Z ]*PRIVATE KEY-----/, kind: "error", label: "private key" },
  { re: /sk-[A-Za-z0-9_-]{20,}/, kind: "error", label: "API key" },
  { re: /AKIA[0-9A-Z]{16}/, kind: "error", label: "AWS access key" },
  { re: /apiKey\s*:\s*"(?!\{env:)/, kind: "warning", label: "inline apiKey" },
  { re: /password\s*[:=]\s*"[^"]+"/i, kind: "warning", label: "inline password" },
];

function checkSecrets(projectDir, errors, warnings, notes) {
  const roots = ["agents", "commands", "skills", "packs"].map((dir) =>
    join(projectDir, ".opencode", dir),
  );
  let scanned = 0;
  for (const root of roots) {
    for (const abs of walk(root)) {
      if (!/\.(md|json|ya?ml|txt)$/i.test(abs)) continue;
      scanned += 1;
      const text = readFileSync(abs, "utf8");
      for (const { re, kind, label } of SECRET_PATTERNS) {
        if (re.test(text)) {
          const rel = toPosix(abs.slice(projectDir.length + 1));
          if (kind === "error") errors.push(`${rel}: looks like a ${label}`);
          else warnings.push(`${rel}: possible ${label} (verify it is an example)`);
        }
      }
    }
  }
  notes.push(`secrets: scanned ${scanned} vendored text files`);
}

function checkPacks(seam, projectDir, errors, warnings, notes) {
  for (const pack of seam.packs ?? []) {
    const manifest = join(projectDir, ".opencode", "packs", pack, "pack.json");
    if (!existsSync(manifest)) {
      errors.push(`packs.${pack}: not vendored (expected ${toPosix(manifest)})`);
      continue;
    }
    const lock = join(projectDir, "skills-lock.json");
    const installed = existsSync(join(projectDir, ".agents", "skills"));
    if (installed) notes.push(`pack ${pack}: skills installed in .agents/skills`);
    else warnings.push(`pack ${pack}: not installed yet; run \`base pack install ${pack}\``);
    if (existsSync(lock)) notes.push(`pack ${pack}: skills-lock.json present`);
  }
}

export function doctor(projectDir, { home = homedir() } = {}) {
  const errors = [];
  const warnings = [];
  const notes = [];

  const manifest = readManifest(projectDir);
  if (!manifest) {
    errors.push(`${MANIFEST_REL}: missing (run \`base init\`)`);
    return { errors, warnings, notes };
  }

  const { seam, errors: seamErrors, warnings: seamWarnings } = loadSeam(projectDir);
  errors.push(...seamErrors);
  warnings.push(...seamWarnings);

  if (seam && manifest.version !== seam.base.version) {
    errors.push(
      `version: seam pins ${seam.base.version} but the manifest records ${manifest.version}`,
    );
  }

  for (const [dest, hash] of Object.entries(manifest.files)) {
    const abs = projectFile(projectDir, dest);
    if (!existsSync(abs)) {
      errors.push(`${dest}: missing (run \`base update --check\`)`);
    } else if (sha256(readFileSync(abs, "utf8")) !== hash) {
      errors.push(`${dest}: modified (run \`base check\` for the diff)`);
    }
  }

  const agentsFile = projectFile(projectDir, "AGENTS.md");
  if (!existsSync(agentsFile)) {
    errors.push("AGENTS.md: missing (the workflow triggers are not loaded)");
  } else {
    const block = extractBlock(readFileSync(agentsFile, "utf8"));
    if (!block) {
      errors.push("AGENTS.md: the workflow-base block is missing");
    } else if (sha256(block.trim()) !== manifest.agentsBlockHash) {
      errors.push("AGENTS.md: the workflow-base block was edited or is stale");
    } else {
      notes.push("AGENTS.md: managed block matches the manifest");
    }
  }

  if (seam) {
    checkDocs(seam, projectDir, errors, warnings, notes);
    checkPacks(seam, projectDir, errors, warnings, notes);
  }
  const agentIds = Object.keys(manifest.files)
    .filter((dest) => dest.startsWith(".opencode/agents/"))
    .map((dest) => dest.slice(".opencode/agents/".length))
    .filter((id) => !id.includes("/") && id.endsWith(".md"))
    .map((id) => id.slice(0, -3));
  const ids = agentIds.length > 0 ? agentIds : ALL_AGENTS;
  checkConfig(projectDir, ids, errors, warnings, notes);
  checkGlobalShadowing(home, ids, errors, notes);
  checkSecrets(projectDir, errors, warnings, notes);

  const owned = new Set(Object.keys(manifest.files));
  for (const dir of ["agents", "commands", "skills"]) {
    for (const abs of walk(join(projectDir, ".opencode", dir))) {
      const rel = toPosix(abs.slice(projectDir.length + 1));
      if (!owned.has(rel)) warnings.push(`${rel}: not owned by the workflow base`);
    }
  }

  return { errors, warnings, notes };
}