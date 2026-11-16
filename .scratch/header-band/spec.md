# Spec: the header band (F6) — two levels, not six groups

Status: **proposed 2026-11-16; implementation approach not yet chosen.** F6 is the
first of the three blockers the Phase 2b gate ordered for the 9/10 push (then F4
answer measure, then F7/R1 feed rhythm). It is a hillclimb-sized visual change:
`docs/agents/hillclimb-rubric.md` scores it, and `docs/ui-art-direction.md` is the
expected answer.

## The flaw, as the gate stated it

> Header band (F6) — at 900 with a run message, `TOKENS … ELAPSED` strands alone on
> a third line with an empty left half; six chrome groups compete.

Art direction already names the intended arrangement:

> **Header.** One primary line: status, method, model. Secondary data (spend,
> tokens, platform) on the second line and visually demoted. Six groups competing
> in one band is the current flaw.

## What the code does today (verified, with pointers)

The band is one wrapping flex row, not two tiers: `Home.razor:68-176`, the outer
`flex flex-wrap items-center gap-x-3 gap-y-1.5 px-2.5 py-1.5 sm:px-3` at
`Home.razor:70`. Six groups share it:

1. status dot + word + optional message — `Home.razor:71-78`. The message is
   `hidden … sm:inline`, so it is absent at 375.
2. method toggle (`role="tablist"`, debt C4) — `Home.razor:80-87`.
3. spend (`spent — this run`) plus screen-reader connection text — `Home.razor:90-106`.
4. platform picker — `Home.razor:108-123`; its active state is the only accent fill
   in the band (`tw.input.css:557-563`).
5. run telemetry (`TOKENS`/`ELAPSED`) — `Home.razor:126-138`.
6. climb telemetry (`ITERATION`/`BEST SCORE`/`LAST RUN`) — `Home.razor:139-173`.

Measured on `CLIMBER_SEED=live` (2026-11-16, `%TEMP%\climb-ui\f6-baseline.mjs`):

| View | Strip height | Rows |
|---|---|---|
| 900 light/dark | 59px | one: status+message, method, spend, platform, telemetry |
| 375 light/dark | 109px | 1) status+method 2) spend+platform 3) telemetry alone |
| 900, 40ch message | 99px | 1) status+message, method, spend, platform 2) telemetry alone, right-aligned at x=653 |

The 900 long-message row is the gate's "empty left half": `sm:ml-auto` on the
telemetry (`Home.razor:128`, `:141`) pushes a wrapped line to the right edge, so
line two contains only `TOKENS 0 ELAPSED 11m 10s` and 650 empty pixels.

Two smaller facts the fix must handle:

- **The idle band is all zeroes.** `TOKENS 0`, `ELAPSED —`, `spent —` are rendered
  before any run exists, so the first-run screen carries metadata that says
  nothing (`%TEMP%\climb-ui-shots\infra-fix\home-run-900-light.png`).
- **The accent has a second job.** The selected platform is accent-bordered and
  accent-filled (`tw.input.css:557`), while the live status word and dot are also
  accent (`tw.input.css:565-566`). Art direction's accent budget is "live dot and
  the primary button. Nothing else."

## Target and scope

- **Target: 9** for the screen (the gate's order: F6, then F4, then F7). This spec
  covers F6 only; it cannot take the screen to 9 alone, and it does not claim to.
- Touch only the context strip, its `.method`/`.providerbar` styling, and the
  contract bullet that records the arrangement. No feed, no composer, no F4/F7.
- Keep `data-slot="context-strip"`, `.methodbar`, `.method`, `.providerbar`,
  `.statusline`, `.telemetry`, `.status-word`, `.status-msg` — the free harnesses
  (and `polish.mjs`'s `.methodbar .method` click) key off them.

## Options

### A. Two-tier band (recommended)

- **Primary row:** status (dot, word, truncated message), method toggle, platform
  picker. Platform moves to the right edge at `sm` and wraps left below it.
- **Secondary row:** spend, tokens, elapsed (or iteration/best/last), left-aligned
  at the status's edge, one type step smaller, muted labels, tabular numbers. No
  `ml-auto`, so a wrapped line never strands.
- **Only when a run has started:** `ActiveStartedAt is not null`. The idle band is
  status + method + platform, with no zeroes.
- **Platform's active state leaves the accent** and matches the method toggle's
  ink-on-surface treatment; the connection dot and the live status keep the accent.
- **Model:** the exact model string stays in the composer chip (EXCEPTION 2); the
  platform picker *is* the model affordance here because there is one model per
  provider. Record that deviation from the art-direction sentence instead of
  duplicating the string at both ends of the screen.

Outcome: 900 running = ~42px primary + ~20px secondary; 375 idle = two rows
(status+method, then platform); 375 running = three deliberate rows (controls,
platform, metadata). No line is right-aligned and alone.

Trade-off: the 900 band gains ~8px over today's one-row 59px; the 375 band loses
~40px when idle. The platform control is the only interactive element on the
secondary tier at 375.

### B. Minimal: keep one band, fix the orphan

Remove `sm:ml-auto` from the wrapped telemetry (left-align the wrap), hide the
idle zeroes, and demote the metadata type/colour. Platform stays where it is.

Trade-off: smallest diff, no new structure, and the 900 long-message case stops
stranding. But the six groups still share one tier, so "six groups competing" is
softened, not fixed. Likely stalls at 8+; the gate named this the loudest flaw.

### C. Single compact status bar

One 32px line with fixed slots and separators:
`● running · working   [run|hillclimb]   ●surplus ●openrouter   10m 01s · 0 tok · $0.00`.
Hierarchy comes from weight and colour, not a second row; at 375 it wraps to two
lines (state+method, then data).

Trade-off: most on-brand for a dense instrument and costs no vertical space. But
it keeps every group in one band and depends on tighter labels (`tok`), which is
the opposite of the gate's "second line, visually demoted".

## Recommended: A, with these recorded sub-decisions

1. Platform on the primary row, not the secondary one (the art-direction list puts
   platform on line two; here it is the only model affordance, and demoting a
   control users need is worse than demoting spend).
2. No duplicate model string in the header; the composer chip remains the exact
   name (EXCEPTION 2). Recorded as a named deviation from art direction.
3. Idle telemetry is absent, not placeholdered.
4. Platform's selected state drops the accent; the band keeps one accent job.
5. 375 accepts three rows while a run is live; every row is deliberate, and the
   idle first-run state drops to two.

## Evidence plan (after approval)

- **Seed:** `CLIMBER_SEED=live` for the running band, `CLIMBER_SEED=1` for the
  completed one, unseeded for idle/first-run. No throwaway seed file needed.
- **Shots:** 900/375 × light/dark, idle, running, completed; plus a 40ch-message
  layout probe to prove no row strands. Stored in `%TEMP%\climb-ui-shots\f6\`.
- **Read-through:** keyboard pass across status → method → platform → telemetry
  (not focusable) at both widths; check `aria-live` still announces the status.
- **Floor:** `dotnet test` and the free harnesses (`theme-nav`, `security`,
  `providers`, `polish`) against an **unseeded** host (`polish` refuses a running
  status). `scripts/ui/surfaces.mjs` and paid seeded runs: **not run** without
  approval.
- **Score:** `quality-gatekeeper` against the rubric, then `/code-review` before
  the final commit.

## Open questions for the user

1. Which option: A (two-tier, recommended), B (minimal orphan fix) or C (compact
   status bar)?
2. If A: platform on the primary row (recommended) or the secondary row as the art
   direction literally lists? And drop the platform accent (recommended) or keep it?