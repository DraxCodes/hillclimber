import {
  copyFileSync,
  existsSync,
  mkdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { dirname, join } from "node:path";
import { projectFile } from "./manifest.mjs";

function stamp() {
  return new Date().toISOString().replace(/[:.]/g, "-");
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