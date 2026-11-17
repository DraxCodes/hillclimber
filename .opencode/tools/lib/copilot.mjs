import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { parseDocument, renderDocument } from "./frontmatter.mjs";

/**
 * The Copilot bridge is generated from the same source as the OpenCode tree:
 * `commands/` become `.github/skills/<id>/SKILL.md`, `skills/` are copied with a
 * compliant `name`, and the three read-only lenses become custom agents. Copilot
 * reads `AGENTS.md` directly, so no second instruction file is generated.
 */
export const COPILOT_LENSES = [
  "plan-architect",
  "quality-gatekeeper",
  "security-auditor",
];

/**
 * Copilot built-in tool aliases (`read`, `search`, `edit`, `execute`, `web`,
 * `agent`, `todo`). The plan lens only reads; the two gates run the project's
 * checks, which needs the terminal.
 */
const LENS_TOOLS = {
  "plan-architect": "['read', 'search']",
  "quality-gatekeeper": "['read', 'search', 'execute']",
  "security-auditor": "['read', 'search', 'execute', 'web']",
};

const ARGUMENT_HINTS = {
  "start-project": "[one-paragraph idea]",
  council: "[proposal]",
  hillclimb: "[artifact or objective]",
  "grill-me": "[plan, decision or idea]",
  handoff: "[what the next session is for]",
};

function walk(root, dir = root) {
  const out = [];
  if (!existsSync(dir)) return out;
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const abs = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(root, abs));
    else if (entry.isFile()) out.push(relative(root, abs).split("\\").join("/"));
  }
  return out.sort();
}

function commandIds(baseRoot) {
  const dir = join(baseRoot, "commands");
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .filter((name) => name.endsWith(".md"))
    .map((name) => name.slice(0, -3))
    .sort();
}

function skillIds(baseRoot) {
  const dir = join(baseRoot, "skills");
  if (!existsSync(dir)) return [];
  return readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .filter((id) => existsSync(join(dir, id, "SKILL.md")))
    .sort();
}

function readText(file) {
  return readFileSync(file, "utf8");
}

function commandSkill(baseRoot, id, skillIdsOnDisk) {
  const { data, body } = parseDocument(readText(join(baseRoot, "commands", `${id}.md`)));
  const canonical = skillIdsOnDisk.includes(id)
    ? parseDocument(readText(join(baseRoot, "skills", id, "SKILL.md")))
    : null;
  const canonicalData = canonical?.data ?? {};

  const preamble = [
    "> Copilot Chat: act as the `task-orchestrator` described in",
    "> `.opencode/agents/task-orchestrator.md`. The read-only lenses are the",
    "> `plan-architect`, `quality-gatekeeper` and `security-auditor` custom",
    "> agents in `.github/agents/`. When a step names a skill, read that",
    "> skill's `SKILL.md` under `.opencode/skills/<skill>/` and follow it. The",
    "> project rules are in `AGENTS.md`; the seam is `.opencode/workflow.json`.",
    "",
  ].join("\n");

  const content = body
    .replaceAll("$ARGUMENTS", `the text the user typed after \`/${id}\``)
    .trim();

  // Copilot keeps one skill per directory, so a command that shares its id
  // with a skill carries the canonical skill body as a reference section
  // instead of silently dropping it.
  const suffix = canonical
    ? `\n\n---\n\n## Reference: the \`${id}\` skill\n\n${canonical.body.trim()}\n`
    : "";

  const front = {
    name: id,
    description: data.description ?? `Run the ${id} workflow.`,
    "argument-hint":
      data["argument-hint"] ?? canonicalData["argument-hint"] ?? ARGUMENT_HINTS[id],
  };
  return renderDocument(front, `${preamble}\n${content}${suffix}\n`);
}

/**
 * Every file the base owns in `.github/`, ready to write. Destinations are
 * project-relative and posix.
 */
export function copilotFiles(baseRoot) {
  const commands = commandIds(baseRoot);
  const commandSet = new Set(commands);
  const skills = skillIds(baseRoot);
  const files = new Map();

  for (const id of skills) {
    const root = join(baseRoot, "skills", id);
    for (const rel of walk(root)) {
      if (rel === "SKILL.md" && commandSet.has(id)) continue;
      const dest = `.github/skills/${id}/${rel}`;
      if (rel !== "SKILL.md") {
        files.set(dest, readText(join(root, rel)));
        continue;
      }
      const { data, body } = parseDocument(readText(join(root, rel)));
      const front = {
        name: id,
        description: data.description ?? `The ${id} workflow.`,
        "argument-hint": data["argument-hint"],
        license: data.license,
      };
      files.set(dest, renderDocument(front, body));
    }
  }

  for (const id of commands) {
    files.set(`.github/skills/${id}/SKILL.md`, commandSkill(baseRoot, id, skills));
  }

  for (const id of COPILOT_LENSES) {
    const file = join(baseRoot, "agents", `${id}.md`);
    if (!existsSync(file)) continue;
    const { data } = parseDocument(readText(file));
    const front = {
      name: id,
      description: data.description ?? `The ${id} lens.`,
      tools: LENS_TOOLS[id] ?? "['read', 'search']",
      "user-invocable": "true",
    };
    const body = [
      `You are the \`${id}\` lens. Read \`.opencode/agents/${id}.md\` and follow`,
      "it exactly. The project rules are in `AGENTS.md`; the seam is",
      "`.opencode/workflow.json`.",
      "",
      "You are read-only: do not edit files or stage anything.",
    ].join("\n");
    files.set(`.github/agents/${id}.agent.md`, renderDocument(front, body));
  }

  return [...files.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([dest, content]) => ({ dest, content }));
}