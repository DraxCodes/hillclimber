import { existsSync, readFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";

/** The machine-local defaults `base new --config` uses when present. */
export const DEFAULT_PROFILE = join(homedir(), ".config", "drax", "bootstrap.json");

/** OpenCode substitutes `{env:NAME}` and `{file:path}`; nothing else is a reference. */
const SECRET_REF = /^\{(?:env|file):[^}]+\}$/i;
const PLACEHOLDER = /^<.*>$/;
const CREDENTIAL_KEY =
  /(?:api[-_]?key|token|secret|password|credential|authorization)/i;

function looksLikeReference(value) {
  return SECRET_REF.test(value) || PLACEHOLDER.test(value);
}

function collectCredentials(node, path, out) {
  if (!node || typeof node !== "object") return;
  if (Array.isArray(node)) {
    node.forEach((item, index) => collectCredentials(item, [...path, index], out));
    return;
  }
  for (const [key, value] of Object.entries(node)) {
    const next = [...path, key];
    if (typeof value === "string" && CREDENTIAL_KEY.test(key)) {
      out.push({ path: next.join("."), value });
    } else {
      collectCredentials(value, next, out);
    }
  }
}

/**
 * Load a bootstrap profile. The profile is machine-local and never shipped:
 * `opencode` is written as the project's config, `agentsMd` is appended to
 * AGENTS.md outside the managed block. Any credential-named field must be a
 * `{env:NAME}`/`{file:path}` reference or a `<placeholder>`; a literal is
 * rejected before anything is written.
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

  const credentials = [];
  collectCredentials(value.opencode, ["opencode"], credentials);
  for (const { path, value: credential } of credentials) {
    if (!looksLikeReference(credential)) {
      errors.push(
        `profile.${path}: use a {env:NAME} or {file:path} reference, not a literal secret`,
      );
    }
  }

  return errors.length > 0
    ? { profile: null, errors }
    : { profile: value, errors: [] };
}