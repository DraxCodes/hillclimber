import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import { BASE_NAME, SEAM_REL } from "./seam.mjs";
import { sha256 } from "./hash.mjs";

export const MANIFEST_REL = ".opencode/.workflow-manifest.json";
export const BLOCK_BEGIN = "<!-- workflow-base:begin -->";
export const BLOCK_END = "<!-- workflow-base:end -->";

export const toPosix = (p) => p.split(sep).join("/");

function walk(root, dir = root) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const abs = join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...walk(root, abs));
    } else if (entry.isFile()) {
      out.push(toPosix(relative(root, abs)));
    }
  }
  return out.sort();
}

function addTree(entries, baseRoot, sourceDir, destDir) {
  const abs = join(baseRoot, sourceDir);
  if (!existsSync(abs)) return;
  for (const rel of walk(abs)) {
    entries.push({ src: `${sourceDir}/${rel}`, dest: `${destDir}/${rel}` });
  }
}

/**
 * The files the base owns inside a project, for a given set of packs.
 * Paths are project-relative and posix.
 */
export function listBaseFiles(baseRoot, packs = []) {
  const entries = [];
  addTree(entries, baseRoot, "agents", ".opencode/agents");
  addTree(entries, baseRoot, "commands", ".opencode/commands");
  addTree(entries, baseRoot, "skills", ".opencode/skills");
  addTree(entries, baseRoot, "tools", ".opencode/tools");

  for (const pack of packs) {
    addTree(entries, baseRoot, `packs/${pack}`, `.opencode/packs/${pack}`);
    const packCommands = join(baseRoot, "packs", pack, "commands");
    if (existsSync(packCommands)) {
      for (const rel of walk(packCommands)) {
        entries.push({
          src: `packs/${pack}/commands/${rel}`,
          dest: `.opencode/commands/${rel}`,
        });
      }
    }
  }

  return entries.sort((a, b) => a.dest.localeCompare(b.dest));
}

export function readBaseFiles(baseRoot, entries) {
  return entries.map(({ src, dest }) => ({
    src,
    dest,
    content: readFileSync(join(baseRoot, src), "utf8"),
  }));
}

export function renderBlock(template, version) {
  return template.replaceAll("{{version}}", version).trim() + "\n";
}

function normalizeEol(text) {
  return text.replace(/\r\n/g, "\n");
}

function applyEol(text, eol) {
  return eol === "\r\n" ? text.replace(/\n/g, "\r\n") : text;
}

export function extractBlock(text) {
  const lf = normalizeEol(text);
  const start = lf.indexOf(BLOCK_BEGIN);
  if (start === -1) return null;
  const end = lf.indexOf(BLOCK_END, start);
  if (end === -1) return null;
  return lf.slice(start, end + BLOCK_END.length);
}

/**
 * Insert or replace the managed block, preserving the file's own text and
 * line endings. Returns the new whole-file content.
 */
export function upsertBlock(text, block) {
  const eol = text.includes("\r\n") ? "\r\n" : "\n";
  const lf = normalizeEol(text);
  const blockLf = normalizeEol(block).trim();
  const start = lf.indexOf(BLOCK_BEGIN);
  const end = lf.indexOf(BLOCK_END);

  let next;
  if (start === -1 && end === -1) {
    next = lf.trim().length === 0 ? `${blockLf}\n` : `${lf.trimEnd()}\n\n${blockLf}\n`;
  } else if (start === -1 || end === -1 || end < start) {
    throw new Error(
      "AGENTS.md has one workflow-base marker but not the other; repair or remove both before continuing",
    );
  } else {
    next =
      lf.slice(0, start) + blockLf + lf.slice(end + BLOCK_END.length);
    if (!next.endsWith("\n")) next += "\n";
  }

  return applyEol(next, eol);
}

export function removeBlock(text) {
  const eol = text.includes("\r\n") ? "\r\n" : "\n";
  const lf = normalizeEol(text);
  const start = lf.indexOf(BLOCK_BEGIN);
  const end = lf.indexOf(BLOCK_END);
  if (start === -1 || end === -1) return text;
  const before = lf.slice(0, start).replace(/\n{3,}$/g, "\n\n").trimEnd();
  const after = lf.slice(end + BLOCK_END.length).replace(/^\n{2,}/, "\n").trimStart();
  const joined = [before, after].filter((part) => part.length > 0).join("\n\n");
  return applyEol(joined.length > 0 ? `${joined}\n` : "", eol);
}

export function manifestFor(files, { version, commit, packs, block }) {
  const hashes = {};
  for (const file of files) {
    hashes[file.dest] = sha256(file.content);
  }
  return {
    schemaVersion: 1,
    base: BASE_NAME,
    version,
    commit: commit ?? "",
    installedAt: new Date().toISOString(),
    files: hashes,
    agentsBlockHash: sha256(block.trim()),
    packs: [...packs],
  };
}

export function readManifest(projectDir) {
  const file = join(projectDir, MANIFEST_REL);
  if (!existsSync(file)) return null;
  return JSON.parse(readFileSync(file, "utf8"));
}

export function manifestContent(manifest) {
  return `${JSON.stringify(manifest, null, 2)}\n`;
}

export function projectFile(projectDir, rel) {
  return join(projectDir, ...rel.split("/"));
}

export function dirOfProjectFile(projectDir, rel) {
  return dirname(projectFile(projectDir, rel));
}

export function seamFile(projectDir) {
  return projectFile(projectDir, SEAM_REL);
}