# Spec: Default agents and a monthly spend figure

## Context

The main page now defaults to a general run with hillclimb as a mode. Two gaps remain:

- The spend line's `total` is the all-time account total. The useful figure is how much is
  being spent per month.
- The configure panel shows only a base-model chip ("opencode default" when the target has
  no `opencode.json`). The repo ships three agents — `task-orchestrator`, `plan-architect`,
  `quality-gatekeeper` — meant to own work end to end, and they should be the default for
  both a general run and a hillclimb, not opencode's bare default agent.

## Decisions (locked with the user before specing)

1. **Monthly figure = rolling 30-day spend** summed from OpenRouter `/activity`, shown as
   `$X.XX/pm`.
2. **The agents really drive the invocation** — `opencode run --agent <name>` (flag
   verified), not display-only.
3. **`task-orchestrator` is the default agent in both modes.** It delegates to
   `plan-architect` (when a plan is warranted) and `quality-gatekeeper` (before keeping a
   change) as it sees fit. The run | hillclimb method switch stays the UI distinction.
4. **Agents are read from the chosen target folder's `.opencode/agents`.** If none resolve,
   fall back to opencode's default agent and say so.
5. **No `/hillclimb`** — this is a product change, not a numeric objective.

## Goal

Make the dashboard run work through the project's own agents by default, in both modes, and
show a monthly spend figure.

## What it should do

### A. Monthly spend

1. The spend line's total becomes the rolling 30-day sum of `/activity`, rendered
   `$X.XX/pm` (e.g. `total $2.80/pm`).
2. When activity is unavailable (no management key, or a failure), it shows `—/pm` rather
   than a fabricated number.
3. `SpendSummary.Monthly(rows)` is pure and unit-tested; `SpendFormat.UsdPerMonth` formats
   the figure.

### B. Agents as the default

4. `WorkflowRole` gains the front-matter `mode`; `WorkflowCatalogReader` parses it.
5. A pure resolver picks the target's default agent: a configured `Climber:DefaultAgent`
   when present in the target, else `task-orchestrator`, else the first `mode: primary`
   agent, else none.
6. `RunOptions` / `ClimberOptions` carry an optional `Agent`; `OpenCodeRun.CommandLine` /
   `Request` add `--agent <name>` when set.
7. Both services pass the resolved agent, so a general run and a hillclimb share one
   default; a target without the agents falls back to opencode's default.
8. The three agent prompts are generalised so they serve ordinary work as well as a
   hillclimb — the orchestrator plans via `plan-architect` when a plan is warranted and
   gates changes via `quality-gatekeeper` before keeping them — keeping the hillclimb loop
   intact.

### C. UI distinction

9. The configure panel's model chip becomes an agent line:
   - **run:** the default agent (`task-orchestrator`) with a "delegates as needed" note;
   - **hillclimb:** the trio (`task-orchestrator`, `plan-architect`, `quality-gatekeeper`)
     as the gated loop.
10. When no agent resolves, the line reads `opencode default`.
11. The method switch and the per-mode telemetry/controls stay as the visible distinction.

## Out of scope

- Model selection (the model chip is replaced by the agent line).
- Persisting runs; session resume.
- Rewriting the skills or the `/hillclimb` command.
- Reflecting a general run on `/activity`.

## How we know it is done

- `dotnet test --nologo` stays green plus new tests for `SpendSummary.Monthly` /
  `SpendFormat.UsdPerMonth`, agent `mode` parsing, the default-agent resolver, and the
  `--agent` flag in the command.
- `node scripts/ui/surfaces.mjs` checks the agent line and the `/pm` total;
  `node scripts/ui/theme-nav.mjs` still passes.
- Visual check: a general run against this repo uses `task-orchestrator`.

## Evidence

- **Tests:** `dotnet test --nologo` → **160 passing** (was 148): the default-agent resolver
  (preferred, configured, first-primary, none), agent `mode` parsing, the `--agent` flag in
  `OpenCodeRun.Request`/`CommandLine`, agent pass-through in both services, and
  `SpendSummary.Monthly` / `SpendFormat.UsdPerMonth`.
- **Browser:** `node scripts/ui/surfaces.mjs` → **42 checks, 0 failures, 0 console errors**.
  Targeting this repo, run mode shows `task-orchestrator` with "delegates to plan-architect /
  quality-gatekeeper as needed"; hillclimb shows the trio with "gated loop"; the spend line
  carries a `/pm` figure.
- **Visual:** screenshots confirm both agent lines render.
- **Agents:** `task-orchestrator` now documents both a general and a hillclimb path;
  `plan-architect`/`quality-gatekeeper` are no longer worded as hillclimb-only.
- **Note:** on this account the rolling figure reads `$0.00/pm` because OpenRouter
  `/activity` only covers completed UTC days and all spend so far is today. The figure is
  the honest 30-day sum, not a fabricated number.
- **Coverage:** `powershell -File scripts/status.ps1` → 160/160, ~64% line.

## Fixed point for review

Session start HEAD for this change is `78f43ce`. `/code-review 78f43ce`.
