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
4. **Agents are read from the chosen target folder's `.opencode/agents`; when it ships none,
   the global opencode config (`~/.config/opencode/agents`) is used** — the same sources
   opencode itself consults, so a name resolved here is one `--agent` accepts. Only opencode's
   own default is used when neither has the agent.
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

- **Tests:** `dotnet test --nologo` → **164 passing** (was 148): the default-agent resolver
  (preferred, configured, first-primary, none, fallback folder, target-wins), agent `mode`
  parsing, the `--agent` flag in `OpenCodeRun.Request`/`CommandLine`, agent pass-through in
  both services, `SpendSummary.Monthly` / `Headline` / `SpendFormat.UsdPerMonth`, and the
  subagent log parse.
- **Browser:** `node scripts/ui/surfaces.mjs` → **0 failures, 0 console errors**, including
  the repo default agent and the fallback to this repo's trio from a folder with no agents.
- **Spend:** the total reads the rolling per-month figure once 30-day activity exists, else
  the all-time account total; on this account it shows `total $3.15` (activity is all inside
  today, which OpenRouter's `/activity` excludes).
- **Agent source:** the target folder's own agents win; when it ships none, the global
  opencode config (`~/.config/opencode/agents`) is used, so a run works through
  `task-orchestrator` in any folder that (locally or globally) provides it — labelled
  `orchestrated`, with `plan-architect`/`quality-gatekeeper` as delegates. opencode resolves
  agents per project, so the dashboard repo's own agents only apply when it is the target or
  the trio is installed globally.
- **Output tabs:** a subagent call is a single `subagent` tool event carrying the agent name
  and its final result (there is no live subagent stream), so the panel gains a `main` tab
  plus one tab per subagent used; main shows a marker, each tab shows that subagent's prompt
  and result. Verified with a real delegated run (`main`, `plan-architect` tabs).
- **Agents:** `task-orchestrator` now documents both a general and a hillclimb path;
  `plan-architect`/`quality-gatekeeper` are no longer worded as hillclimb-only.
- **Coverage:** `powershell -File scripts/status.ps1` → 164/164, ~64% line.

## Fixed point for review

Session start HEAD for this change is `78f43ce`. `/code-review 78f43ce`.
