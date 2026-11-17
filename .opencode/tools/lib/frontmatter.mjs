/**
 * Minimal YAML frontmatter for the Markdown files the base ships. The base
 * frontmatter is flat (`key: value`), so a full YAML parser would be weight
 * without benefit. Values are unquoted in the source; a quoted value is
 * unquoted on read and re-quoted on write.
 */
export function parseDocument(text) {
  const lf = text.replace(/\r\n/g, "\n");
  if (!lf.startsWith("---\n")) return { data: {}, body: lf };

  const end = lf.indexOf("\n---\n", 4);
  if (end === -1) return { data: {}, body: lf };

  const data = {};
  for (const line of lf.slice(4, end).split("\n")) {
    const match = /^([A-Za-z0-9_-]+):\s?(.*)$/.exec(line);
    if (!match) continue;
    data[match[1]] = unquote(match[2].trim());
  }
  return { data, body: lf.slice(end + 5) };
}

function unquote(value) {
  if (value.length < 2) return value;
  if (value.startsWith('"') && value.endsWith('"')) {
    try {
      return JSON.parse(value);
    } catch {
      return value.slice(1, -1);
    }
  }
  if (value.startsWith("'") && value.endsWith("'")) return value.slice(1, -1);
  return value;
}

const RAW_KEYS = new Set([
  "tools",
  "user-invocable",
  "disable-model-invocation",
  "context",
]);

function formatValue(key, value) {
  if (RAW_KEYS.has(key)) return String(value);
  if (key === "name" && /^[a-z0-9-]+$/.test(String(value))) return String(value);
  return JSON.stringify(String(value));
}

export function renderDocument(fields, body) {
  const lines = Object.entries(fields)
    .filter(([, value]) => value !== undefined && value !== null && value !== "")
    .map(([key, value]) => `${key}: ${formatValue(key, value)}`);
  return `---\n${lines.join("\n")}\n---\n\n${body.replace(/^\n+/, "")}`;
}