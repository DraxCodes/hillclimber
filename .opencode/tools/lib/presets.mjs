// Presets for `base new`. A preset fills the common checks and the visual
// surface; flags still override every field, and the written seam is printed so
// the guess is visible.

export const PRESETS = {
  bare: {
    description: "No build or test yet; no visual surface.",
    build: null,
    test: null,
    surface: "none",
  },
  web: {
    description: "Node web app: npm build/test, 375/900, light and dark.",
    build: "npm run build",
    test: "npm test",
    surface: "web",
    widths: [375, 900],
    themes: ["light", "dark"],
  },
  cli: {
    description: "Node CLI: npm build/test, no visual surface.",
    build: "npm run build",
    test: "npm test",
    surface: "cli",
  },
  docs: {
    description: "Documentation only: no build or test, docs surface.",
    build: null,
    test: null,
    surface: "docs",
  },
};

export function applyPreset(seam, name) {
  const preset = PRESETS[name];
  if (!preset) return null;
  seam.checks.build = preset.build ? { command: preset.build, cwd: "." } : null;
  seam.checks.test = preset.test ? { command: preset.test, cwd: "." } : null;
  seam.verify.surface = preset.surface;
  if (preset.widths) seam.verify.widths = [...preset.widths];
  if (preset.themes) seam.verify.themes = [...preset.themes];
  return preset;
}