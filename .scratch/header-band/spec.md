# Spec: the header band (F6) — two levels, not six groups

Status: **implemented 2026-11-16 (option A, two tiers); code-review fixes applied;
gatekeeper score pending.** F6 is the first of the three blockers the Phase 2b gate
ordered for the 9/10 push (then F4 answer measure, then F7/R1 feed rhythm). It is a
hillclimb-sized visual change: `docs/agents/hillclimb-rubric.md` scores it, and
`docs/ui-art-direction.md` is the expected answer.

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

## Decisions (answered by the user, 2026-11-16)

1. **Option A**, the two-tier band.
2. **Platform on the primary row**, with the accent dropped; the exact model string
   stays in the composer chip (EXCEPTION 2). Recorded as a named deviation in
   `docs/ui-contract.md` Phase 2d, with its width cost.
3. **C4 first**: the method picker became `role="group"` + `aria-pressed` in commit
   `a237c99`, before F6.

## Evidence (after the code-review fixes)

- **Shots:** `%TEMP%\climb-ui-shots\f6\after-{live,done,idle}-*.png` and
  `after-header-*` at 900/375, light/dark. Strip heights: 900 live/done 66px, 375
  live/done 100px, 900 idle 43px, 375 idle 77px. No horizontal overflow, no console
  errors.
- **Wrap probe:** the 40ch message no longer strands a right-aligned line; the
  platform group wraps to the left edge at narrow widths and stays on line one from
  ~700px up.
- **Climb branch probe (synthetic):** the fixture seeds a run, not a climb, so the
  three climb cells were cloned into the live `dl` with the same classes. At 900
  they sit on one line (x=135/282/412, strip 66px); at 375 and 320 the third
  (`last run`) wraps to a second line starting at x=10, not the right edge, with
  `overflowX: 0`. Worst case is a taller metadata tier, never a stranded line.
- **Keyboard:** Tab order is `run → hillclimb → surplus → openrouter`; every stop is
  `:focus-visible` with a 2px solid ring; Space on `hillclimb` flips
  `aria-pressed` from `false` to `true`. Telemetry is not focusable.
- **ARIA:** `.spendline` is `role="group" aria-label="Spend"`; the platform buttons
  carry string `aria-pressed` (`"true"`/`"false"`, not a minimized boolean) and
  `data-provider`; the duplicated sr-only `.conn` spans are gone.
- **Floor:** `dotnet test` 461/0/0; `theme-nav` 0; `security` 0; `providers` 15/0;
  `polish` 100 (0 defects in 560 checks). `surfaces.mjs` and seeded opencode runs
  were **not run**.

## Post-score refinements (8 → 7 → 8; shipped at 8)

The first score named four edges; all four are implemented in this pass:

1. **Accent budget:** the output tab's active underline is ink, not accent. The
   accent now marks the live status and the primary button only.
2. **Status-copy duplication:** completion and stop set no message at the producer
   (`RunService`, `PlaybackRunService`, `HillClimberService`), so `completed` /
   `stopped` is not printed twice in either branch. Failures, approval prompts and
   fallback offers keep their text. The view no longer matches producer copy.
3. **Mixed spend grammar:** spend is a key/value cell inside the telemetry `<dl>`
   (`SPENT $0.0124 this run`), not a separate sentence beside it, in both branches.
4. **Platform group semantics:** `role="group" aria-label="Platform"`, with the
   visible label `aria-hidden`.

The code review of this pass found the first version of item 2 was run-branch-only
(the climb's "Completed N iteration(s)…" still doubled the status word) and lived
in the view; the producer change above replaced it.

Accepted, not fixed: the live "spent —" placeholder before a cost arrives is
honest and transient; seeded inline-link tap targets stay 20–22px (pre-existing,
rhythm-neutral).

Evidence after the producer change: done state `completed` with 0 message nodes;
spend cell `SPENT $0.0124 this run` at 900 and `SPENT $0.0124` at 375; platform
`role=group`/`Platform`; active tab border `rgb(32,29,29)` light /
`rgb(253,252,252)` dark; `overflowX 0` at both widths. Floor: `dotnet test`
461/0/0; `theme-nav` 0; `security` 0; `providers` 15/0; `polish` 100 (0/560)
unseeded. Seeded `polish` 97.1, rhythm 56/0, 16 tap (pre-existing). Screenshots:
`%TEMP%\climb-ui-shots\final\fixes-*`. Paid `surfaces.mjs` and seeded opencode
runs: **not run**.

Second pass, after the re-score dropped to 7: the telemetry `<dd>` now carries
`m-0`, so the six cells pair at 4px and the 375 tier is one row again (band 100px,
not 120; measured gap 44px → 4px). The Inline Citation card is nudged inside the
viewport on hover/focus: at 375 the last chip's card measured right=367 against
375 (was 389, 14px clipped), with an 8px margin; at 900 it is unchanged
(left=334, right=590). The seeded green `completed` beside the red terminal row is
recorded as EXCEPTION 4 (closed-tail playback; README rule), not changed. Evidence:
`%TEMP%\climb-ui-shots\final3\fix3-*`.

Third pass, after the 7 held on a pre-existing 375 a11y defect and two citation
edges: the header's `activity`/`status` links now carry `aria-label` + `title` at
`<sm` (the tree read two unnamed links); Escape hides the open citation card and
leaves focus on the link; a resize re-places a card still open (verified 375 → 900
→ 375: right 367 → 590 → 367). Inline Citation is now named in the surface map.
Evidence: `%TEMP%\climb-ui-shots\final4\fix4-*`.

## Score

**8/10, shipped.** Drift 7 → 8: the three defects holding the 7 were reproduced as
fixed. Contract: no violations. The gate stopped at 8 per the rubric (a 9 is earned
by R7, not the floor); the named gap is art direction, not a defect on the primary
path:

- **Type scale:** nine rendered sizes, six of them micro; metadata labels at 9.92px
  against the documented 11px, and tool rows are not one step smaller than prose.
  Fixing it is a typed scale pass across `tw.input.css`, not a band change.
- **Shadow weights:** `shadow-sm/md/lg/xl` are used beyond `--shadow` (citation card,
  Card, Tooltip, Popover, mobile rail); unnamed deviation, caps R7.
- **Accepted, recorded:** EXCEPTION 4 (closed-tail playback), inline-link tap
  targets (pre-existing), icon-only nav names fixed for AT but visually icon-only.

A 10 would also need a clean-tail seed/exit-code fixture, a deliberate anchor policy
for the nudged citation card, and the 375 platform row folded into the primary line.