// Presets for `base new`. A preset fills exactly five seam fields and nothing
// else: checks.build, checks.test, verify.surface, verify.widths and
// verify.themes. It never chooses a language, framework, folder layout,
// architecture or dependency — the project does that in /start-project.
//
// The old names (bare, web, cli, docs) remain aliases so existing prompt blocks
// keep working; the canonical names say what they actually touch.

export const PRESET_ALIASES = {
  bare: "checks-none",
  web: "checks-web",
  cli: "checks-cli",
  docs: "checks-docs",
};

export const PRESETS = {
  "checks-none": {
    description: "No build or test, no visual surface.",
    build: null,
    test: null,
    surface: "none",
  },
  "checks-web": {
    description: "npm build/test checks, a web surface, 375/900, light and dark.",
    build: "npm run build",
    test: "npm test",
    surface: "web",
    widths: [375, 900],
    themes: ["light", "dark"],
  },
  "checks-cli": {
    description: "npm build/test checks, a CLI surface, no widths or themes.",
    build: "npm run build",
    test: "npm test",
    surface: "cli",
  },
  "checks-docs": {
    description: "No build or test, a docs surface.",
    build: null,
    test: null,
    surface: "docs",
  },
};

/** The canonical preset name for `name`, or null when it is not a preset. */
export function resolvePresetName(name) {
  if (PRESETS[name]) return name;
  return PRESET_ALIASES[name] ?? null;
}

export function applyPreset(seam, name) {
  const canonical = resolvePresetName(name);
  if (!canonical) return null;
  const preset = PRESETS[canonical];
  seam.checks.build = preset.build ? { command: preset.build, cwd: "." } : null;
  seam.checks.test = preset.test ? { command: preset.test, cwd: "." } : null;
  seam.verify.surface = preset.surface;
  if (preset.widths) seam.verify.widths = [...preset.widths];
  if (preset.themes) seam.verify.themes = [...preset.themes];
  return { ...preset, name: canonical, alias: canonical === name ? null : name };
}