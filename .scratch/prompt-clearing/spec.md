# Spec: Clear the prompt box after a start

## Context

Starting a run or a climb leaves the instruction in the `#prompt` textarea. The box is
disabled while a run is active, so the text just sits there and is easy to fire a second
time once the run settles — the exact prompt is still there to click again.

The clear has two traps, recorded in the handoff:

- The provider-fallback offer returns early from `Start()` and `StartOnFallback()` re-calls
  `Start()`, which re-reads `_prompt`. Clearing in `Start()` would break that flow.
- `Launch()` catches concurrent-run / invalid-input throws and shows `_error`. A failed
  launch must keep the prompt so the user can fix and retry.

So the clear belongs in `Launch()`, only after the `StartAsync` call returns without
throwing.

## Decisions (locked with the user before specing)

1. **Clear in both modes.** The goal is handed to the climb service at launch, so climb
   iterations don't need it in the box. One rule, no mode-dependent surprise.
2. **The last prompt stays recoverable in memory** — a small "use last prompt" affordance,
   no storage, no history list.
3. **Focus the emptied box, nothing else** — no "started" banner. Note the box is disabled
   while a run is active, so focus is applied at the next render where it is usable
   again (typically when the run settles).

## Goal

After a start is accepted, the prompt box is empty and the instruction that was sent can be
put back with one click, without reintroducing a double-fire path.

## What it should do

1. `Launch()`, after the `StartAsync` call returns, records the trimmed prompt it sent and
   clears the box.
2. A launch that throws keeps the prompt exactly as it was (only `_error` changes).
3. The fallback-offer path is untouched: the offer returns before `Launch()`, the prompt
   stays in the box, and `StartOnFallback()` re-runs `Start()` with the same text.
4. The cleared box takes focus once it is no longer disabled — the next instruction can be
   typed immediately after the run settles.
5. "use last prompt" is offered only when there is a remembered prompt, the box is empty,
   and no run is active; clicking it restores the remembered text and the affordance
   disappears.
6. The remembered prompt lives only as long as the page session — no localStorage, no
   history.

## Out of scope

- Changing the "disabled while running" rule for the controls.
- Persisting prompts across a reload, or a picker over several past prompts.
- A new test surface (bUnit or a component test project) for `Home.razor`.
- Any extra paid run beyond the verification described below.

## How we know it is done

- `dotnet test` stays green at 253 tests — this is a component-only change and no test
  covers `Home.razor`; assert by inspection plus the browser check below, and say plainly
  that no automated test covers it.
- Browser check of one short run through Surplus only, against the throwaway fixture
  target, stopped immediately: the box is empty while the run streams, and after the run
  settles "use last prompt" appears and restores exactly the text that was sent.
- The failed-launch path is confirmed to leave the prompt intact (the existing empty-prompt
  validation in `scripts/ui/surfaces.mjs` covers the throw; a manual check is enough).
- Assertions for 4 and 5 are added to the existing paid `scripts/ui/surfaces.mjs`, which
  adds no runs of its own; whether that harness was executed is reported plainly.

## Evidence

- **Tests:** `dotnet test --nologo` → **253 passed, 0 failed, 0 skipped** (385 ms), a fresh
  build of both projects. No test covers `Home.razor`; this change adds none, so the count is
  unchanged by design.
- **Free browser harnesses** (no tokens): `theme-nav.mjs` → 0 failures, no console errors;
  `providers.mjs` → 15 checks, 0 failures; `security.mjs` → 0 failures.
- **Browser:** a temporary Playwright check (`%TEMP%\climb-ui\prompt-clearing-check.mjs`,
  outside the repo) → **10 checks, 0 failures, 0 console errors**, run twice (before and after
  the review fixes): a rejected start shows `A prompt is required.` and keeps the text; the
  box is empty while the run streams; the emptied box takes focus once idle; a settled run
  offers the last prompt back; recall restores the exact text; the affordance hides again.
- **Spend:** each of those two checks started **one** short run and stopped it immediately —
  `est. $0.00`, 0 tokens, echoed as `--model surplus/deepseek-v4.1-flash-1017` with no
  `openrouter/`.
- **Surplus-only caveat:** the running app has **two** providers configured (Surplus primary,
  OpenRouter secondary), so `surfaces.mjs`'s stricter single-provider refusal (exit 2) cannot
  pass here. The temporary check instead required Surplus to be primary, aborted on any
  fallback offer (never approving one), and verified the echoed command line. An attempt to
  blank the second entry with `Climber__Providers__1__Id=` had no effect.
- **`surfaces.mjs` was NOT run** (paid). Its new assertions — the box clears 150 ms after the
  start click, the climb goal clears, the emptied box takes focus, and recall restores the
  text — were written but never executed. They add no runs of their own.
- **Review:** `/code-review 699c3cc` ran both axes. The substantive finding on both — a stale
  `ElementReference` could outlive the box when the session re-locks mid-run — is fixed by
  requiring `_unlocked` and catching `JSException` as well as `JSDisconnectedException`. The
  harness assertions were tightened in the same pass (launch-time rather than settle-time,
  focus, diagnostics); `RecallLastPrompt` lost an unreachable null-coalesce; README gained the
  handoff bullet.

## Fixed point for review

Session start HEAD is `699c3cc`. `/code-review 699c3cc`.