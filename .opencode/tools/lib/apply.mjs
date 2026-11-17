import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  rmdirSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, relative } from "node:path";
import { projectFile } from "./manifest.mjs";

function stamp() {
  return new Date().toISOString().replace(/[:.]/g, "-");
}

/**
 * Remove the emptied ancestor directories of deleted files, stopping at the
 * project root. This keeps a `--no-copilot` update from leaving a skeleton
 * `.github/skills/` tree behind.
 */
function pruneEmptyParents(projectDir, deleted) {
  for (const rel of deleted) {
    let dir = dirname(projectFile(projectDir, rel));
    while (true) {
      const inside = relative(projectDir, dir);
      if (!inside || inside.startsWith("..")) break;
      try {
        // A directory that is empty now may gain a sibling's deletion later, so
        // this is not memoized: stop only when the directory is non-empty.
        if (readdirSync(dir).length > 0) break;
        rmdirSync(dir);
      } catch {
        break;
      }
      dir = dirname(dir);
    }
  }
}

/**
 * Apply a plan with a backup and a full rollback on any failure. Writes are
 * staged first, so a plan that cannot be staged cannot touch the project.
 */
export function applyPlan(projectDir, plan, { dryRun = false, log = console.log } = {}) {
  if (plan.conflicts?.length) {
    const error = new Error("refusing to write: unresolved conflicts");
    error.conflicts = plan.conflicts;
    throw error;
  }

  if (dryRun) {
    for (const write of plan.writes) log(`  would write ${write.path}`);
    for (const del of plan.deletes) log(`  would delete ${del}`);
    return { written: [], deleted: [], backupDir: null, dryRun: true };
  }

  const id = stamp();
  const backupDir = join(projectDir, ".opencode", ".workflow-backup", id);
  const stageDir = join(projectDir, ".opencode", ".workflow-stage", id);

  const backups = new Map();
  for (const write of plan.writes) {
    const abs = projectFile(projectDir, write.path);
    if (existsSync(abs) && statSync(abs).isFile()) backups.set(write.path, readFileSync(abs));
  }
  for (const del of plan.deletes) {
    const abs = projectFile(projectDir, del);
    if (existsSync(abs) && statSync(abs).isFile() && !backups.has(del)) {
      backups.set(del, readFileSync(abs));
    }
  }

  const written = [];
  const deleted = [];
  const created = [];

  try {
    // Stage every write first: a staging error touches nothing.
    for (const write of plan.writes) {
      const staged = join(stageDir, ...write.path.split("/"));
      mkdirSync(dirname(staged), { recursive: true });
      writeFileSync(staged, write.content, "utf8");
    }

    for (const write of plan.writes) {
      const dest = projectFile(projectDir, write.path);
      mkdirSync(dirname(dest), { recursive: true });
      if (!existsSync(dest)) created.push(write.path);
      copyFileSync(join(stageDir, ...write.path.split("/")), dest);
      written.push(write.path);
    }

    for (const del of plan.deletes) {
      const dest = projectFile(projectDir, del);
      if (!existsSync(dest)) continue;
      rmSync(dest, { force: true });
      deleted.push(del);
    }
  } catch (error) {
    for (const [rel, content] of backups) {
      const dest = projectFile(projectDir, rel);
      mkdirSync(dirname(dest), { recursive: true });
      writeFileSync(dest, content, "utf8");
    }
    for (const rel of created) {
      if (!backups.has(rel)) rmSync(projectFile(projectDir, rel), { force: true });
    }
    throw error;
  } finally {
    rmSync(stageDir, { recursive: true, force: true });
  }

  // Keep the backup only when something was actually replaced or deleted.
  pruneEmptyParents(projectDir, deleted);

  if (backups.size > 0) {
    for (const [rel, content] of backups) {
      const dest = join(backupDir, ...rel.split("/"));
      mkdirSync(dirname(dest), { recursive: true });
      writeFileSync(dest, content, "utf8");
    }
  } else {
    rmSync(backupDir, { recursive: true, force: true });
  }

  return { written, deleted, backupDir: backups.size > 0 ? backupDir : null, dryRun: false };
}