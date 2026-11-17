import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

export const SEAM_REL = ".opencode/workflow.json";
export const BASE_NAME = "drax-ai-workflow";
export const WORKFLOWS = ["council", "hillclimb", "grilling", "handoff", "security"];
export const SURFACES = ["web", "cli", "api", "docs", "none"];
export const THEMES = ["light", "dark"];
export const DOC_KEYS = [
  "issues",
  "glossary",
  "adr",
  "rubricAddendum",
  "contract",
  "artDirection",
];

const isObject = (v) => v !== null && typeof v === "object" && !Array.isArray(v);
const isNonEmptyString = (v) => typeof v === "string" && v.trim().length > 0;

function checkKeys(obj, allowed, path, errors) {
  for (const key of Object.keys(obj)) {
    if (!allowed.includes(key)) {
      errors.push(`${path}.${key}: unknown key`);
    }
  }
}

function checkCommand(value, path, errors, { nullable = true } = {}) {
  if (value === null) {
    if (!nullable) errors.push(`${path}: must not be null`);
    return;
  }
  if (!isObject(value)) {
    errors.push(`${path}: expected an object with a command`);
    return;
  }
  checkKeys(value, ["command", "cwd"], path, errors);
  if (!isNonEmptyString(value.command)) {
    errors.push(`${path}.command: expected a non-empty string`);
  }
  if (value.cwd !== undefined && !isNonEmptyString(value.cwd)) {
    errors.push(`${path}.cwd: expected a non-empty string`);
  }
}

export function validateSeam(seam) {
  const errors = [];
  const warnings = [];

  if (!isObject(seam)) {
    return { errors: ["seam: expected a JSON object"], warnings };
  }

  checkKeys(
    seam,
    [
      "schemaVersion",
      "base",
      "project",
      "workflows",
      "checks",
      "verify",
      "docs",
      "neverCommit",
      "packs",
    ],
    "seam",
    errors,
  );

  if (seam.schemaVersion !== 1) {
    errors.push("schemaVersion: expected 1");
  }

  if (!isObject(seam.base)) {
    errors.push("base: expected an object with name and version");
  } else {
    checkKeys(seam.base, ["name", "version"], "base", errors);
    if (seam.base.name !== BASE_NAME) {
      errors.push(`base.name: expected "${BASE_NAME}"`);
    }
    if (!isNonEmptyString(seam.base.version)) {
      errors.push("base.version: expected a non-empty string");
    }
  }

  if (!isNonEmptyString(seam.project)) {
    errors.push("project: expected a non-empty string");
  }

  if (seam.workflows !== undefined) {
    if (!Array.isArray(seam.workflows)) {
      errors.push("workflows: expected an array");
    } else {
      for (const [i, name] of seam.workflows.entries()) {
        if (!WORKFLOWS.includes(name)) {
          errors.push(`workflows[${i}]: unknown workflow "${name}"`);
        }
      }
    }
  }

  if (!isObject(seam.checks)) {
    errors.push("checks: expected an object with build, test, free and paid");
  } else {
    checkKeys(seam.checks, ["build", "test", "free", "paid"], "checks", errors);
    checkCommand(seam.checks.build, "checks.build", errors);
    checkCommand(seam.checks.test, "checks.test", errors);
    if (seam.checks.build === null && seam.checks.test === null) {
      warnings.push("checks: both build and test are null; the floor cannot run");
    }
    for (const key of ["free", "paid"]) {
      if (!Array.isArray(seam.checks[key])) {
        errors.push(`checks.${key}: expected an array`);
      } else {
        for (const [i, entry] of seam.checks[key].entries()) {
          checkCommand(entry, `checks.${key}[${i}]`, errors, { nullable: false });
        }
      }
    }
  }

  if (seam.verify !== undefined) {
    const v = seam.verify;
    if (!isObject(v)) {
      errors.push("verify: expected an object");
    } else {
      checkKeys(
        v,
        ["surface", "seed", "capture", "captureCwd", "widths", "themes", "notes"],
        "verify",
        errors,
      );
      if (!SURFACES.includes(v.surface)) {
        errors.push(`verify.surface: expected one of ${SURFACES.join(", ")}`);
      }
      if (v.seed !== undefined && typeof v.seed !== "string") {
        errors.push("verify.seed: expected a string");
      }
      if (v.capture !== undefined && typeof v.capture !== "string") {
        errors.push("verify.capture: expected a string");
      }
      if (v.captureCwd !== undefined && typeof v.captureCwd !== "string") {
        errors.push("verify.captureCwd: expected a string");
      }
      if (v.notes !== undefined && typeof v.notes !== "string") {
        errors.push("verify.notes: expected a string");
      }
      if (v.widths !== undefined) {
        if (!Array.isArray(v.widths) || v.widths.some((n) => typeof n !== "number")) {
          errors.push("verify.widths: expected an array of numbers");
        }
      }
      if (v.themes !== undefined) {
        if (
          !Array.isArray(v.themes) ||
          v.themes.some((t) => !THEMES.includes(t))
        ) {
          errors.push(`verify.themes: expected an array of ${THEMES.join(", ")}`);
        }
      }
      if (v.surface === "web") {
        if (!Array.isArray(v.widths) || v.widths.length === 0) {
          errors.push("verify.widths: required when surface is web");
        }
        if (!Array.isArray(v.themes) || v.themes.length === 0) {
          errors.push("verify.themes: required when surface is web");
        }
      }
    }
  }

  if (seam.docs !== undefined) {
    if (!isObject(seam.docs)) {
      errors.push("docs: expected an object");
    } else {
      checkKeys(seam.docs, DOC_KEYS, "docs", errors);
      for (const [key, value] of Object.entries(seam.docs)) {
        if (typeof value !== "string" || value.trim().length === 0) {
          errors.push(`docs.${key}: expected a non-empty string`);
        }
      }
    }
  }

  if (seam.neverCommit !== undefined) {
    if (
      !Array.isArray(seam.neverCommit) ||
      seam.neverCommit.some((p) => typeof p !== "string" || p.trim().length === 0)
    ) {
      errors.push("neverCommit: expected an array of non-empty strings");
    }
  }

  if (seam.packs !== undefined) {
    if (
      !Array.isArray(seam.packs) ||
      seam.packs.some((p) => typeof p !== "string" || p.trim().length === 0)
    ) {
      errors.push("packs: expected an array of non-empty strings");
    }
  }

  return { errors, warnings };
}

export function defaultSeam({ project, baseVersion }) {
  return {
    schemaVersion: 1,
    base: { name: BASE_NAME, version: baseVersion },
    project,
    workflows: [...WORKFLOWS],
    checks: {
      build: null,
      test: null,
      free: [],
      paid: [],
    },
    verify: {
      surface: "none",
      seed: "",
      capture: "",
      widths: [],
      themes: [],
      notes: "",
    },
    docs: {
      issues: ".scratch",
      glossary: "GLOSSARY.md",
      adr: "docs/adr",
    },
    neverCommit: [".env", "*.pem", "*.key", "*.local.json"],
    packs: [],
  };
}

export function loadSeam(projectDir) {
  const file = join(projectDir, SEAM_REL);
  if (!existsSync(file)) {
    return {
      seam: null,
      errors: [`${SEAM_REL}: missing (the workflow is not installed here)`],
      warnings: [],
      file,
    };
  }
  let seam;
  try {
    seam = JSON.parse(readFileSync(file, "utf8"));
  } catch (error) {
    return {
      seam: null,
      errors: [`${SEAM_REL}: ${error.message}`],
      warnings: [],
      file,
    };
  }
  const { errors, warnings } = validateSeam(seam);
  return { seam, errors, warnings, file };
}