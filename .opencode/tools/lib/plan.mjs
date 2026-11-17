import { existsSync, readFileSync, readdirSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import {
  MANIFEST_REL,
  manifestContent,
  manifestFor,
  projectFile,
  removeBlock,
  renderBlock,
  seamFile,
  toPosix,
  upsertBlock,
} from "./manifest.mjs";
import { sha256 } from "./hash.mjs";
import { SEAM_REL } from "./seam.mjs";

const BASE_DIRS = ["agents", "commands", "skills", "tools", "packs"];

function walkFiles(root, dir = root) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const abs = join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walkFiles(root, abs));
    else if (entry.isFile()) out.push(toPosix(relative(root, abs)));
  }
  return out.sort();
}

/** Files under the base-owned directories that the manifest does not own. */
export function listExtraFiles(projectDir, manifest) {
  const owned = new Set(Object.keys(manifest.files ?? {}));
  const extra = [];
  for (const dir of BASE_DIRS) {
    const abs = join(projectDir, ".opencode", dir);
    if (!existsSync(abs)) continue;
    for (const rel of walkFiles(abs)) {
      const dest = `.opencode/${dir}/${rel}`;
      if (!owned.has(dest)) extra.push(dest);
    }
  }
  return extra;
}

function sameText(a, b) {
  return sha256(a) === sha256(b);
}

function rewriteJson(text, value) {
  const eol = text.includes("\r\n") ? "\r\n" : "\n";
  const next = `${JSON.stringify(value, null, 2)}\n`;
  return eol === "\r\n" ? next.replace(/\n/g, "\r\n") : next;
}

function readIfExists(abs) {
  return existsSync(abs) ? readFileSync(abs, "utf8") : null;
}

function planAgentsBlock(projectDir, block) {
  const abs = projectFile(projectDir, "AGENTS.md");
  const current = readIfExists(abs) ?? "";
  const next = upsertBlock(current, block);
  if (next === current) return null;
  return { path: "AGENTS.md", content: next, kind: "agents" };
}

export function planInit({ projectDir, files, seamContent, block, force }) {
  const writes = [];
  const conflicts = [];
  const unchanged = [];

  for (const file of files) {
    const abs = projectFile(projectDir, file.dest);
    const current = readIfExists(abs);
    if (current !== null) {
      if (sameText(current, file.content)) {
        unchanged.push(file.dest);
        continue;
      }
      if (!force) {
        conflicts.push(`${file.dest} already exists and differs`);
        continue;
      }
    }
    writes.push({ path: file.dest, content: file.content, kind: "file" });
  }

  const seamAbs = seamFile(projectDir);
  const existingSeam = readIfExists(seamAbs);
  if (existingSeam !== null && !force) {
    conflicts.push(`${SEAM_REL} already exists (use --force to replace)`);
  } else {
    writes.push({ path: SEAM_REL, content: seamContent, kind: "seam" });
  }

  const blockWrite = planAgentsBlock(projectDir, block);
  if (blockWrite) writes.push(blockWrite);

  return { writes, deletes: [], conflicts, unchanged };
}

export function planUpdate({
  projectDir,
  files,
  oldManifest,
  newVersion,
  commit,
  block,
  packs,
  force,
}) {
  const writes = [];
  const deletes = [];
  const conflicts = [];
  const modified = [];
  const unchanged = [];

  const newDests = new Set(files.map((file) => file.dest));

  for (const file of files) {
    const abs = projectFile(projectDir, file.dest);
    const current = readIfExists(abs);
    const oldHash = oldManifest.files[file.dest];

    if (current === null) {
      writes.push({ path: file.dest, content: file.content, kind: "file" });
      continue;
    }

    const currentHash = sha256(current);
    if (oldHash && currentHash === oldHash) {
      if (sameText(current, file.content)) unchanged.push(file.dest);
      else writes.push({ path: file.dest, content: file.content, kind: "file" });
      continue;
    }

    if (oldHash && currentHash !== oldHash) {
      modified.push(file.dest);
      if (force) {
        writes.push({ path: file.dest, content: file.content, kind: "file" });
      } else {
        conflicts.push(`${file.dest} was modified locally (use --force to overwrite)`);
      }
      continue;
    }

    if (sameText(current, file.content)) {
      unchanged.push(file.dest);
    } else {
      conflicts.push(`${file.dest} exists but is not owned by the workflow base`);
    }
  }

  for (const oldDest of Object.keys(oldManifest.files)) {
    if (newDests.has(oldDest)) continue;
    const abs = projectFile(projectDir, oldDest);
    const current = readIfExists(abs);
    if (current === null) {
      deletes.push(oldDest);
      continue;
    }
    if (sha256(current) === oldManifest.files[oldDest]) {
      deletes.push(oldDest);
    } else {
      modified.push(oldDest);
      if (force) deletes.push(oldDest);
      else conflicts.push(`${oldDest} was modified locally (use --force to remove)`);
    }
  }

  const seamAbs = seamFile(projectDir);
  const seamText = readIfExists(seamAbs);
  if (seamText !== null) {
    let seam;
    try {
      seam = JSON.parse(seamText);
    } catch (error) {
      conflicts.push(`${".opencode/workflow.json"}: ${error.message}`);
      seam = null;
    }
    if (seam) {
      seam.base = { ...(seam.base ?? {}), version: newVersion };
      const next = rewriteJson(seamText, seam);
      if (next !== seamText) {
        writes.push({ path: ".opencode/workflow.json", content: next, kind: "seam" });
      }
    }
  }

  const blockWrite = planAgentsBlock(projectDir, block);
  if (blockWrite) writes.push(blockWrite);

  const manifest = manifestFor(files, { version: newVersion, commit, packs, block });
  const manifestAbs = projectFile(projectDir, MANIFEST_REL);
  const currentManifest = readIfExists(manifestAbs);
  const nextManifest = manifestContent(manifest);
  if (currentManifest !== nextManifest) {
    writes.push({ path: MANIFEST_REL, content: nextManifest, kind: "manifest" });
  }

  return { writes, deletes, conflicts, modified, unchanged, manifest };
}

export function planCheck({ projectDir, oldManifest, seamVersion }) {
  const changed = [];
  const missing = [];
  const clean = [];

  for (const [dest, hash] of Object.entries(oldManifest.files)) {
    const abs = projectFile(projectDir, dest);
    if (!existsSync(abs)) {
      missing.push(dest);
      continue;
    }
    if (sha256(readFileSync(abs, "utf8")) === hash) clean.push(dest);
    else changed.push(dest);
  }

  const extra = listExtraFiles(projectDir, oldManifest);
  const problems = [];
  if (seamVersion && oldManifest.version !== seamVersion) {
    problems.push(
      `seam pins ${seamVersion} but the manifest records ${oldManifest.version}`,
    );
  }

  return { changed, missing, extra, clean, problems };
}

export function planEject({ projectDir, manifest, force }) {
  const deletes = [];
  const conflicts = [];

  for (const [dest, hash] of Object.entries(manifest.files)) {
    const abs = projectFile(projectDir, dest);
    if (!existsSync(abs)) continue;
    if (sha256(readFileSync(abs, "utf8")) === hash) {
      deletes.push(dest);
    } else if (force) {
      deletes.push(dest);
    } else {
      conflicts.push(`${dest} was modified locally (use --force to remove)`);
    }
  }

  const writes = [];
  const agentsAbs = projectFile(projectDir, "AGENTS.md");
  if (existsSync(agentsAbs)) {
    const current = readFileSync(agentsAbs, "utf8");
    const next = removeBlock(current);
    if (next !== current) writes.push({ path: "AGENTS.md", content: next, kind: "agents" });
  }

  deletes.push(MANIFEST_REL);
  return { writes, deletes, conflicts };
}

export { renderBlock };