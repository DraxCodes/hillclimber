# Spec: paid test harnesses run through Surplus only

## Context

`scripts/ui/surfaces.mjs` is the only check in this repo that reaches a paid inference
API: it drives real `opencode` runs through the dashboard. It previously relied on the app's
default provider being Surplus and on never clicking the OpenRouter approval. Nothing
stopped it from starting when Surplus was unconfigured (the app silently showed the
"Start this run on OpenRouter instead?" offer and the run never happened) or from drifting
into an OpenRouter command if the model resolution changed. A reader could not tell from
the output whether a run had actually used Surplus.

## Decisions (locked with the user)

1. **Surplus is the only provider the paid checks may use.** The harness refuses to run
   when Surplus is not connected; it never approves the fallback.
2. **The guarantee is checked, not assumed.** Each run's echoed command line must contain
   `--model surplus/…` and must not contain `openrouter/`; no approval prompt may appear.
3. **A missing Surplus connection is a hard stop**, exit code 2 and a message naming the
   key to configure, before the first paid run — not a skipped check that still reports
   green.

## What it should do

- After unlock and the spend-line checks, wait for the Surplus connection state to settle
  and require exactly `connected`. Otherwise print the summary and exit 2 with the fix.
- After the run, assert its echoed command lines use a `surplus/` model, reference no
  `openrouter/` model, and that no `.approval-row` (OpenRouter approval) is present. If any
  of that fails, stop before the climb so the second paid run never starts.
- After the climb, run the same assertions and exit 2 if they fail.
- Only command lines are matched, so prompt text or model prose that mentions a provider
  cannot trip the check.
- Document the rule, the exit codes (0 pass, 1 a check failed, 2 refused to continue), and
  `npm run surfaces`.

## Out of scope

- The free checks (`theme-nav.mjs`, `security.mjs`) and the unit tests, which use stubs and
  never reach a paid API.
- Changing the app's provider selection or approval flow.

## How we know it is done

- With Surplus unreachable, `BASE_URL=http://localhost:5180 node surfaces.mjs` exits 2 after
  the spend-line checks and starts nothing.
- With Surplus connected, the same harness passes its Surplus-model and no-approval checks
  for both the run and the climb.

## Evidence

- **Guard run:** a second instance on `:5180` with `Climber__SurplusBaseUrl` pointed at a
  closed port → harness printed `Surplus is not connected (state: unreachable).`, the
  summary, `REFUSED — 7 checks, 0 failures. No further paid runs were started.` and exited
  **2** before any run; no `opencode` process was started.
- **Connected run:** `node surfaces.mjs` against the live server → **54 checks, 0 failures**,
  including `run runs on Surplus (--model surplus/deepseek-v4.1-flash-1017)` and
  `climb runs on Surplus` with the same model, and no approval prompt in either mode.
- **Review:** `/code-review f9e8f05` found the guard exited before printing the summary and
  that a failed model check still proceeded to the climb (a second paid run). Both are fixed
  above, and the assertions now read only echoed command lines so prompt text cannot trip
  them. The connected run was repeated after the fix with the same result.
- `dotnet test --nologo` → **230 passing**, 0 failed (no C# changed; run per AGENTS.md).

## Fixed point for review

Session start HEAD for this change is `f9e8f05`. `/code-review f9e8f05`.