import { existsSync, readFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";

/** The machine-local defaults `base new --config` uses when present. */
export const DEFAULT_PROFILE = join(homedir(), ".config", "drax", "bootstrap.json");

const SECRET_REF = /^\{[a-z][a-z0-9]*:[^}]+\}$/i;
const PLACEHOLDER = /^<.*>$/;

function collectApiKeys(node, path, out) {
  if (!node || typeof node !== "object") return;
  for (const [key, value] of Object.entries(node)) {
    if (key === "apiKey" && typeof value === "string") {
      out.push({ path: [...path, key].join("."), value });
    } else {
      collectApiKeys(value, [...path, key], out);
    }
  }
}

/**
 * Load a bootstrap profile. The profile is machine-local and never shipped:
 * `opencode` is written as the project's config, `agentsMd` is appended to
 * AGENTS.md outside the managed block.
 */
export function loadProfile(file, { required = false } = {}) {
  if (!file) return { profile: null, errors: [] };
  if (!existsSync(file)) {
    return {
      profile: null,
      errors: required ? [`profile: ${file} does not exist`] : [],
    };
  }

  let value;
  try {
    value = JSON.parse(readFileSync(file, "utf8"));
  } catch (error) {
    return { profile: null, errors: [`profile: cannot parse ${file} (${error.message})`] };
  }
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    return { profile: null, errors: ["profile: expected a JSON object"] };
  }

  const errors = [];
  if (value.opencode !== undefined && (!value.opencode || typeof value.opencode !== "object" || Array.isArray(value.opencode))) {
    errors.push("profile.opencode: expected an object");
  }
  if (value.agentsMd !== undefined && typeof value.agentsMd !== "string") {
    errors.push("profile.agentsMd: expected a string");
  }

  const keys = [];
  collectApiKeys(value.opencode, ["opencode"], keys);
  for (const { path, value: keyValue } of keys) {
    if (!SECRET_REF.test(keyValue) && !PLACEHOLDER.test(keyValue)) {
      errors.push(
        `profile.${path}: use a {env:NAME} reference, not a literal API key`,
      );
    }
  }

  return errors.length > 0
    ? { profile: null, errors }
    : { profile: value, errors: [] };
}