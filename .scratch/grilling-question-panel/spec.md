# Spec: Grilling asks through the question panel

## Context

The `grilling` skill (`.opencode/skills/grilling/SKILL.md`, locally owned, not in
`skills-lock.json`) tells the agent to render each round as `❓ Q1 … ➡️ …` prose. That
prints a wall of text into the chat, and the answers have to be typed back by hand. The
client exposes a `question` tool that renders a round as an interactive panel: each entry
has a short `header`, a full `question` body, selectable `options`, and an automatically
appended free-form answer.

## Decisions (locked with the user)

1. **Panel-first, with a text fallback.** The panel is the instructed path; the `❓/➡️`
   block stays only for clients where the tool is unavailable.
2. **A round is one `question` call** — the whole frontier in a single panel, matching the
   skill's existing "ask the whole frontier at once" rule.
3. **The recommendation is the first option**, labelled `<answer> (Recommended)`, so
   picking it is the easy path and "yes" is implicit.

## What it should do

- The skill names the `question` tool and maps the round onto its fields: `header` (numbered
  title, ≤30 chars), `question` (body), `options` (2–4 realistic answers), `multiple` only
  when answers can combine.
- Open-ended questions still go in the panel: offer the live positions, let the free-form
  field carry the rest.
- The agent asks once through the panel and does not restate the round as chat prose.
- The text format is retained as an explicitly conditional fallback.

## Out of scope

- Rewriting the design-tree / frontier / sub-agent-fact-finding rules.
- Any other skill or agent prompt.
- Changing the question tool itself.

## How we know it is done

- The edited skill states the panel path first, the field mapping, the recommendation
  encoding, and the fallback condition.
- A live grilling round renders as a panel (verified by invoking the skill; skipped if the
  user does not want to spend a round on it).

## Evidence

- **Tests:** `dotnet test --nologo` → **216 passing**, 0 failed (no code changed; run anyway
  per AGENTS.md).
- **Review:** `/code-review ae7fbe8` run across Standards and Spec; its findings — missing
  `header` cap, an unqualified "never as prose" rule, a surviving "yes" line that only
  applies to the fallback, and the fallback losing its numbering — are fixed in the skill.
  `.opencode/commands/grill-me.md` no longer restates the round format, so the skill is the
  single source.
- **Live panel check:** **not run**. Invoking the skill means a real grilling round with
  the user; the next grilling session is the check.

## Fixed point for review

Session start HEAD for this change is `ae7fbe8`. `/code-review ae7fbe8`.