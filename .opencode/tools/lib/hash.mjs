import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";

// Normalise content so a CRLF checkout and an LF checkout hash the same.
export function normalizeText(text) {
  return text.replace(/^\uFEFF/, "").replace(/\r\n/g, "\n");
}

export function sha256(text) {
  return (
    "sha256:" +
    createHash("sha256").update(normalizeText(text), "utf8").digest("hex")
  );
}

export function hashFile(file) {
  return sha256(readFileSync(file, "utf8"));
}