// Free UI polish audit for ClimberWeb. Requires Playwright and a running server:
//
//   cd scripts/ui && npm install
//   node polish.mjs
//   BASE_URL=http://localhost:5179 node polish.mjs
//
// It visits 7 views at 4 viewports in both themes (56 cells), runs 10 deterministic
// DOM/computed-style checks per cell (560 checks), and reports a UI Polish Score (UPS).
// It never clicks start/stop, never starts a run and never spends tokens. If it ever sees
// a running climb it exits 3. Screenshots and latest.json are written under the temp
// directory (never committed).
//
// Exit codes: 0 completed audit, 1 harness error, 2 fewer than 56 cells, 3 run detected.

import { chromium } from 'playwright';
import { mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const BASE = process.env.BASE_URL || 'http://localhost:5179';
const PIN = process.env.PIN || '1234';
const ROOT = process.env.SHOTS_DIR || join(tmpdir(), 'climber-polish');
const RUN_ROOT = join(ROOT, 'latest');
mkdirSync(RUN_ROOT, { recursive: true });

const VIEWPORTS = [
  { width: 320, height: 640 },
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
  { width: 1280, height: 900 },
];
const THEMES = ['light', 'dark'];
const CHECKS = ['overflow', 'contrast', 'tap', 'focus', 'clip', 'console', 'name', 'rhythm', 'type', 'radius'];

const VIEWS = [
  { id: 'home-locked', path: '/', unlock: false, ready: async () => {} },
  { id: 'home-run', path: '/', unlock: true, ready: async () => {} },
  {
    id: 'home-hillclimb', path: '/', unlock: true,
    ready: async (page) => {
      await page.locator('.methodbar .method', { hasText: 'hillclimb' }).click();
      await page.waitForTimeout(250);
    },
  },
  {
    id: 'home-browser', path: '/', unlock: true,
    ready: async (page) => {
      await page.locator('.browse-btn').click();
      await page.locator('.browser').waitFor({ state: 'visible', timeout: 10_000 });
    },
  },
  { id: 'status', path: '/status', unlock: true, ready: async () => {} },
  { id: 'activity', path: '/activity', unlock: true, ready: async () => {} },
  { id: 'not-found', path: '/not-found', unlock: false, ready: async () => {} },
];

function auditPage({ consoleErrors }) {
  const problems = {};
  const fail = (check, message) => {
    if (!problems[check]) problems[check] = [];
    if (problems[check].length < 6) problems[check].push(message);
  };

  const vw = window.innerWidth;
  const all = [...document.querySelectorAll('body *')].filter((el) => el instanceof HTMLElement);
  const isHidden = (el) =>
    el.closest('[aria-hidden="true"]') !== null ||
    el.closest('[hidden]') !== null ||
    el.closest('#blazor-error-ui') !== null;

  const visible = (el) => {
    if (!el.isConnected) return false;
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || cs.visibility === 'collapse') return false;
    if (parseFloat(cs.opacity) === 0) return false;
    const r = el.getBoundingClientRect();
    return r.width > 0.5 && r.height > 0.5;
  };

  const hasOwnText = (el) => {
    for (const node of el.childNodes) {
      if (node.nodeType === Node.TEXT_NODE && node.textContent.trim().length > 0) return true;
    }
    return false;
  };

  const describe = (el) => {
    const id = el.id ? '#' + el.id : '';
    const cls = typeof el.className === 'string' && el.className.trim()
      ? '.' + el.className.trim().split(/\s+/).slice(0, 2).join('.')
      : '';
    return el.tagName.toLowerCase() + id + cls;
  };

  const parseColor = (value) => {
    if (!value) return null;
    const match = value.match(/rgba?\(([^)]+)\)/);
    if (!match) return null;
    const parts = match[1].split(/[,\s/]+/).filter(Boolean).map(Number);
    if (parts.length < 3 || parts.slice(0, 3).some((n) => Number.isNaN(n))) return null;
    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
  };

  const over = (fg, bg) => {
    const a = Math.max(0, Math.min(1, fg.a ?? 1));
    return {
      r: fg.r * a + bg.r * (1 - a),
      g: fg.g * a + bg.g * (1 - a),
      b: fg.b * a + bg.b * (1 - a),
      a: 1,
    };
  };

  const luminance = ({ r, g, b }) => {
    const channel = (value) => {
      const c = value / 255;
      return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
  };

  const ratio = (a, b) => {
    const l1 = luminance(a);
    const l2 = luminance(b);
    return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
  };

  // Effective background behind an element, compositing every translucent ancestor
  // background over the opaque body colour. Reports gradient backgrounds so callers
  // can skip them rather than guess.
  const backgroundOf = (el) => {
    const stack = [];
    let node = el;
    let gradient = false;
    while (node && node.nodeType === Node.ELEMENT_NODE) {
      const cs = getComputedStyle(node);
      if (cs.backgroundImage && cs.backgroundImage !== 'none') gradient = true;
      const colour = parseColor(cs.backgroundColor);
      if (colour && colour.a > 0) {
        stack.push(colour);
        if (colour.a >= 1) break;
      }
      node = node.parentElement;
    }

    let base = { r: 255, g: 255, b: 255, a: 1 };
    const body = parseColor(getComputedStyle(document.body).backgroundColor);
    if (body) base = body.a >= 1 ? body : over(body, base);
    for (let i = stack.length - 1; i >= 0; i--) base = over(stack[i], base);
    return { colour: base, gradient };
  };

  // ---- 1. overflow -------------------------------------------------------
  const inScroller = (el) => {
    let node = el.parentElement;
    while (node && node !== document.body) {
      const cs = getComputedStyle(node);
      if (/(auto|scroll)/.test(cs.overflowX)) return true;
      node = node.parentElement;
    }
    return false;
  };

  if (document.documentElement.scrollWidth > vw + 1 || document.body.scrollWidth > vw + 1) {
    fail('overflow', `document ${document.documentElement.scrollWidth}/${document.body.scrollWidth} > ${vw}`);
  }
  for (const el of all) {
    if (!visible(el) || isHidden(el)) continue;
    const cs = getComputedStyle(el);
    if (cs.position === 'fixed' || inScroller(el)) continue;
    const rect = el.getBoundingClientRect();
    if (rect.right > vw + 1 || rect.left < -1) {
      fail('overflow', `${describe(el)} [${rect.left.toFixed(0)}, ${rect.right.toFixed(0)}] vw ${vw}`);
    }
  }

  // ---- 2. contrast -------------------------------------------------------
  for (const el of all) {
    if (!visible(el) || !hasOwnText(el) || isHidden(el)) continue;
    const cs = getComputedStyle(el);
    const fontSize = parseFloat(cs.fontSize);
    if (fontSize === 0) continue;
    if (el.matches(':disabled') || el.closest('[aria-disabled="true"]')) continue;
    if (el.tagName === 'OPTION' || el.tagName === 'SCRIPT' || el.tagName === 'STYLE') continue;

    const { colour: bg, gradient } = backgroundOf(el);
    if (gradient) continue;
    const fg = parseColor(cs.color);
    if (!fg) continue;
    const effective = fg.a >= 1 ? fg : over(fg, bg);
    const value = ratio(effective, bg);
    const weight = parseInt(cs.fontWeight, 10) || 400;
    const large = fontSize >= 24 || (fontSize >= 18.66 && weight >= 700);
    const need = large ? 3 : 4.5;
    if (value + 0.01 < need) {
      fail('contrast', `${describe(el)} ${value.toFixed(2)}:1 < ${need}:1`);
    }
  }

  // ---- 3. tap targets ----------------------------------------------------
  const interactiveSelector =
    'button, a[href], input:not([type="hidden"]), select, textarea, summary, [role="tab"], [role="button"]';
  for (const el of all) {
    if (!el.matches(interactiveSelector) || !visible(el) || isHidden(el)) continue;
    if (el.matches(':disabled') || el.closest('[aria-disabled="true"]')) continue;
    const cs = getComputedStyle(el);
    if (el.tagName === 'A' && cs.display === 'inline') continue;
    const rect = el.getBoundingClientRect();
    if (rect.width < 23.5 || rect.height < 23.5) {
      fail('tap', `${describe(el)} ${rect.width.toFixed(1)}x${rect.height.toFixed(1)}`);
    }
  }

  // ---- 4. focus ring -----------------------------------------------------
  const focusables = all.filter((el) =>
    el.matches(interactiveSelector) &&
    visible(el) &&
    !isHidden(el) &&
    !el.matches(':disabled') &&
    el.tabIndex >= 0,
  );
  for (const el of focusables) {
    el.focus({ preventScroll: true });
    const cs = getComputedStyle(el);
    // An outline/outer box-shadow is painted outside the border box, so judge its
    // contrast against what sits behind the element, not the element's own fill.
    const bg = backgroundOf(el.parentElement ?? el).colour;
    let ok = false;
    const style = cs.outlineStyle;
    const width = parseFloat(cs.outlineWidth) || 0;

    if (style === 'auto' && width >= 1) ok = true;
    if (!ok && style !== 'none' && width >= 2) {
      const outline = parseColor(cs.outlineColor);
      if (outline) {
        const colour = outline.a >= 1 ? outline : over(outline, bg);
        if (ratio(colour, bg) >= 3) ok = true;
      }
    }
    if (!ok) {
      for (const match of cs.boxShadow.matchAll(/rgba?\([^)]+\)/g)) {
        const shadow = parseColor(match[0]);
        if (!shadow) continue;
        const colour = shadow.a >= 1 ? shadow : over(shadow, bg);
        if (ratio(colour, bg) >= 3) { ok = true; break; }
      }
    }
    if (!ok) {
      fail('focus', `${describe(el)} ${style}/${width.toFixed(0)}px, shadow: ${cs.boxShadow}`);
    }
  }

  // ---- 5. accidental clipping -------------------------------------------
  const scrollableAncestor = (el) => {
    let node = el.parentElement;
    while (node && node !== document.body) {
      const cs = getComputedStyle(node);
      if (/(auto|scroll)/.test(cs.overflowX)) return true;
      node = node.parentElement;
    }
    return false;
  };
  for (const el of all) {
    if (!visible(el) || !hasOwnText(el) || isHidden(el)) continue;
    const cs = getComputedStyle(el);
    if (cs.display === 'inline' || cs.textOverflow === 'ellipsis') continue;
    if (!['visible', 'hidden', 'clip'].includes(cs.overflowX)) continue;
    if (scrollableAncestor(el)) continue;
    if (el.clientWidth > 0 && el.scrollWidth > el.clientWidth + 1) {
      fail('clip', `${describe(el)} scroll ${el.scrollWidth} > client ${el.clientWidth}`);
    }
  }

  // ---- 6. console --------------------------------------------------------
  if (consoleErrors.length > 0) {
    fail('console', `${consoleErrors.length} error(s): ${consoleErrors[0]}`);
  }

  // ---- 7. accessible names ----------------------------------------------
  const accessibleName = (el) => {
    const label = (el.getAttribute('aria-label') || '').trim();
    if (label) return label;
    const labelledBy = (el.getAttribute('aria-labelledby') || '').trim();
    if (labelledBy) {
      const text = labelledBy
        .split(/\s+/)
        .map((id) => document.getElementById(id)?.textContent || '')
        .join(' ')
        .trim();
      if (text) return text;
    }
    if (el.id) {
      const forLabel = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
      if (forLabel && (forLabel.textContent || '').trim()) return forLabel.textContent.trim();
    }
    if (el.closest('label')) return 'wrapped in label';
    const title = (el.getAttribute('title') || '').trim();
    if (title) return title;
    if (el.tagName === 'IMG' && (el.getAttribute('alt') || '').trim()) return el.getAttribute('alt').trim();
    if (['INPUT', 'SELECT', 'TEXTAREA'].includes(el.tagName) && el.type !== 'password' && (el.value || '').trim()) {
      return el.value.trim();
    }
    return ((el.innerText || el.textContent || '').trim());
  };
  for (const el of all) {
    if (!el.matches(interactiveSelector) || !visible(el) || isHidden(el)) continue;
    if (!accessibleName(el)) fail('name', describe(el));
  }

  // ---- 8. rhythm (design) ------------------------------------------------
  const stage = document.querySelector('main.stage');
  if (stage) {
    const selector = '.panel, .telemetry, .statusline, .spendline, .methodbar, .providerbar';
    const items = [...stage.children].filter((el) => el.matches(selector) && visible(el));
    const gaps = items.map((el) => parseFloat(getComputedStyle(el).marginBottom) || 0);
    const offGrid = items.filter((el, i) => gaps[i] > 0 && Math.abs(gaps[i] / 4 - Math.round(gaps[i] / 4)) > 0.05);
    const nonzero = gaps.filter((gap) => gap > 0);
    if (offGrid.length > 0) {
      fail('rhythm', 'off 4px grid: ' + offGrid.map((el, i) => `${describe(el)} ${gaps[items.indexOf(el)].toFixed(1)}px`).join(', '));
    }
    if (nonzero.length > 1 && Math.max(...nonzero) - Math.min(...nonzero) > 0.5) {
      fail('rhythm', 'inconsistent gaps: ' + nonzero.map((v) => v.toFixed(1)).join(', '));
    }
  }

  // ---- 9. type floor (design) -------------------------------------------
  for (const el of all) {
    if (!visible(el) || !hasOwnText(el) || isHidden(el)) continue;
    if (el.closest('svg')) continue;
    const fontSize = parseFloat(getComputedStyle(el).fontSize);
    // 9.9px accepts the existing 0.62rem caption band (9.92px) but rejects 0.6rem (9.6px).
    if (fontSize > 0 && fontSize < 9.9) {
      fail('type', `${describe(el)} ${fontSize.toFixed(2)}px`);
    }
  }

  // ---- 10. radius discipline (design) -----------------------------------
  const radiusAllowed = (value, half) => {
    if (value <= 0.5) return true;
    if (Math.abs(value - 3) <= 0.5 || Math.abs(value - 4) <= 0.5 || Math.abs(value - 6) <= 0.5) return true;
    if (value >= 999) return true;
    return half > 0 && Math.abs(value - half) <= 0.5;
  };
  for (const el of all) {
    if (!visible(el) || isHidden(el)) continue;
    const cs = getComputedStyle(el);
    const values = [cs.borderTopLeftRadius, cs.borderTopRightRadius, cs.borderBottomRightRadius, cs.borderBottomLeftRadius];
    if (values.every((value) => parseFloat(value) === 0)) continue;
    const rect = el.getBoundingClientRect();
    const half = Math.min(rect.width, rect.height) / 2;
    const bad = values.filter((value) => {
      if (value.includes('%')) return parseFloat(value) < 49;
      return !radiusAllowed(parseFloat(value), half);
    });
    if (bad.length > 0) fail('radius', `${describe(el)} ${bad.join(', ')}`);
  }

  return { problems };
}

const browser = await chromium.launch({ headless: true });
const cells = [];
let detectedRun = false;

async function visit(view, viewport, theme) {
  const context = await browser.newContext({
    viewport,
    reducedMotion: 'reduce',
    deviceScaleFactor: 1,
  });
  await context.addInitScript((value) => {
    try { localStorage.setItem('hillclimber-theme', value); } catch { /* ignore */ }
  }, theme);

  const page = await context.newPage();
  const consoleErrors = [];
  page.on('console', (message) => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', (error) => consoleErrors.push('pageerror: ' + error.message));

  try {
    await page.goto(BASE + view.path, { waitUntil: 'networkidle', timeout: 30_000 });
    await page.waitForTimeout(300);

    if (view.unlock) {
      const pin = page.locator('#pin');
      await pin.waitFor({ state: 'visible', timeout: 15_000 });
      await pin.fill(PIN);
      await page.locator('button', { hasText: 'unlock' }).first().click();
      await pin.waitFor({ state: 'detached', timeout: 15_000 });
      await page.waitForTimeout(800);
    }

    await view.ready(page);
    await page.addStyleTag({
      content: '*, *::before, *::after { transition: none !important; animation: none !important; }',
    });
    await page.waitForTimeout(200);

    const running = await page.evaluate(() =>
      (document.querySelector('.status-word')?.textContent || '').trim().toLowerCase() === 'running');
    if (running) {
      detectedRun = true;
      throw new Error('a run is active; refusing to audit');
    }

    const shot = join(RUN_ROOT, `${view.id}-${viewport.width}x${viewport.height}-${theme}.png`);
    await page.screenshot({ path: shot, fullPage: true });

    // Establish keyboard modality so :focus-visible matches the programmatic focus in the audit.
    await page.keyboard.press('Tab');
    const { problems } = await page.evaluate(auditPage, { consoleErrors });
    const result = {};
    for (const check of CHECKS) result[check] = !problems[check];
    return { result, problems };
  } finally {
    await context.close();
  }
}

for (const viewport of VIEWPORTS) {
  for (const theme of THEMES) {
    for (const view of VIEWS) {
      const id = `${view.id}-${viewport.width}x${viewport.height}-${theme}`;
      try {
        const { result, problems } = await visit(view, viewport, theme);
        cells.push({ id, view: view.id, viewport, theme, result, problems });
        const failed = CHECKS.filter((check) => !result[check]);
        console.log(`  ${failed.length === 0 ? 'ok  ' : 'FAIL'} ${id}${failed.length ? ' — ' + failed.join(', ') : ''}`);
      } catch (error) {
        if (detectedRun) {
          console.error('A run is active; refusing to continue: ' + error.message);
          await browser.close();
          process.exit(3);
        }
        console.error(`  FAIL ${id} — ${error.message}`);
        cells.push({ id, view: view.id, viewport, theme, result: {}, problems: { error: [error.message] } });
      }
    }
  }
}

await browser.close();

if (cells.length < VIEWS.length * VIEWPORTS.length * THEMES.length) {
  console.error(`Only ${cells.length} cells visited; expected 56.`);
  process.exit(2);
}

const byCheck = {};
for (const check of CHECKS) {
  byCheck[check] = { pass: 0, fail: 0 };
  for (const cell of cells) {
    if (cell.result[check]) byCheck[check].pass += 1;
    else byCheck[check].fail += 1;
  }
}

const byView = {};
for (const view of VIEWS) {
  byView[view.id] = { pass: 0, fail: 0 };
  for (const cell of cells) {
    if (cell.view !== view.id) continue;
    for (const check of CHECKS) {
      if (cell.result[check]) byView[view.id].pass += 1;
      else byView[view.id].fail += 1;
    }
  }
}

const checks = cells.length * CHECKS.length;
const upd = CHECKS.reduce((sum, check) => sum + byCheck[check].fail, 0);
const ups = Math.round((100 * (checks - upd) / checks) * 10) / 10;

const summary = { cells: cells.length, checks, upd, ups, byCheck, byView };
writeFileSync(join(RUN_ROOT, 'latest.json'), JSON.stringify({ ...summary, cells: undefined, details: cells }, null, 2));

console.log('');
console.log(JSON.stringify(summary, null, 2));
console.log(`\nUI polish score: ${ups} (${upd} defects in ${checks} checks)`);
process.exit(0);