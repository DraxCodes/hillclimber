# Spec: truthful coverage & tests panel with a coverage rating

## Context

`/status` renders `status/coverage.json`, a committed artifact last regenerated on
2026-11-15 (311 tests, 63.11% line). The suite is now 461 tests and green, so the panel
shows stale numbers as if they were current. It also reduces "coverage" to one line
percentage, which says nothing about hot paths, edge cases, or the UI.

This change makes the panel show the **last measured** test status with its freshness
made explicit, and adds a **coverage rating** built from measured inputs plus a committed
judgement rubric. The rating always ships with its inputs and gaps; a bare number is not
acceptable.

Fixed point for review: `bdbf4ab`. Baseline `dotnet test`: 461 passed, 0 failed, 0 skipped.

## Decisions (locked with the user before coding)

1. **Rating model — hybrid.** Measured metrics + a committed rubric + one pure scoring
   function.
2. **Scale — 0–100 with a band** (`strong` ≥ 85, `solid` 70–84, `thin` 55–69, `weak` < 55).
3. **Hot paths — the 10 below, threshold line ≥ 80% and branch ≥ 60%.**
4. **Staleness — show the last-known rating, loudly marked unverified.** Never hide it.
5. **No hillclimb trio.** The rating's own rubric would be circular as a hillclimb
   objective; the visual pass plus the floor is the gate.

Rejected alternatives:

- **Measured only.** Cobertura has no notion of a hot path or of which browser flow a
  Playwright suite covers, and it reports Razor components at 0% because `dotnet test`
  never renders them. It would lie about the UI.
- **Curated only.** A hand-written checklist is an unverified claim and drifts silently —
  the same failure mode as the stale artifact.

## What the panel shows

1. **Current status, honestly labelled.** Tests passing / failing / line %, with the
   artifact's generation time, and a freshness line:
   - fresh → "measured `<head>`; matches the current sources";
   - stale → a warning: "sources changed since measurement (measured at `<head>`, N commits
     back); regenerate with `scripts/status.ps1`";
   - unverifiable (old artifact / unknown fingerprint version) → "freshness unknown".
2. **Rating header** — `72/100 · solid`, a one-line why, and an `unverified` chip while
   the artifact is stale.
3. **Three dimensions** with weight, score, evidence, and a meter:
   - hot paths (40) — satisfied paths / total at the threshold;
   - edge cases (30) — 60% named scenarios passing + 40% hot-path branch coverage against
     a 75% target;
   - UI integration (30) — free browser suites green / free suites expected.
4. **Inputs and gaps** in a `<details>`: every hot path with line/branch and met/below-bar,
   every named edge scenario with its test outcome, every UI suite with run state, plus the
   rubric's standing known gaps.

Test health is a **gate**, not a weight: a red suite means coverage claims are meaningless,
so the rating bands as `failing` and is capped at 25.

## Hot paths (rubric-owned)

| id | label | classes |
|---|---|---|
| run-lifecycle | Run lifecycle | `ClimberWeb.Services.RunService` |
| climb-loop | Climb loop | `ClimberWeb.Services.HillClimberService` |
| provider-registry | Provider registry & fallback | `ClimberWeb.Services.ProviderRegistry` |
| log-format | Log formatting & grouping | `ClimberWeb.Services.LogFormatter`, `ClimberWeb.Services.LogGrouper` |
| output-presentation | Output presentation | `ClimberWeb.Services.ToolPresentation`, `ClimberWeb.Services.TurnPresentation`, `ClimberWeb.Services.TestResultParser` |
| scroll-math | Scroll math | `ClimberWeb.Services.StepScrollMath` |
| status-report | Status report & artifact parsing | `ClimberWeb.Services.ProjectStatusService`, `ClimberWeb.Services.CoverageArtifact`, `ClimberWeb.Services.GitHistory` |
| class-merge | Class merge | `ClimberWeb.Components.Ui.UiClass`, `ClimberWeb.Components.Ui.TailwindConflict` |
| pin-gate | PIN gate | `ClimberWeb.Services.ClimberAuth` |
| binding-preflight | Binding & preflight | `ClimberWeb.Services.ClimberBinding`, `ClimberWeb.Services.ClimbPreflight` |

Class aggregation sums line hits and branch conditions across the type and its nested
compiler-generated types (`Type/...`), so async state machines are included.

## Edge cases (rubric-owned, verified against TRX)

Each entry names one exact test prefix; a renamed or deleted test turns the item into a
visible gap. Initial list (10): non-zero exit fails the run; stop during a run; fallback
approval retries on the fallback; exit 127 never offers a fallback; missing `opencode` is a
clear failure; climb stops mid-iteration; corrupt/missing coverage artifact; git failure
degrades to a warning; wrong PIN does not unlock; reverse scroll clamps at the live edge.

## UI evidence

Free suites recorded with pass/fail and check counts: `theme-nav.mjs`, `security.mjs`,
`providers.mjs`, `polish.mjs`. `surfaces.mjs` is **paid and never counted**; it is listed
among the UI suites with its state (`not run — paid harness (scripts/ui/surfaces.mjs)`) so
every declared suite is visible, but it never earns a point. Known standing gaps also include
the first-run empty/loading/disconnected states, which no free suite covers, and the seeded
transcript surface, which is only reachable through the paid run or unit-level playback
fixtures.

Browser evidence records the commit it was recorded at. If that is not the commit the tests
were measured at, the suites describe different code, so the UI dimension is not counted and
the mismatch is named as a gap.

Because `dotnet test` and a live dev host contend for the same build output, UI evidence is
produced by a second script: `scripts/status-ui.ps1 -BaseUrl …`, which runs the free suites
against a live host and merges a `ui` section into the artifact. It is optional; the rating
treats missing UI evidence as not measured and names the gap.

## Artifact (`status/coverage.json`, schema 2)

```json
{
  "schema": 2,
  "generated": "…",
  "head": "bdbf4ab",
  "fingerprint": { "version": 1, "inputs": "<sha256>" },
  "tests": { "passed": 461, "failed": 0, "skipped": 0 },
  "coverage": { "line": 0.64, "branch": 0.54 },
  "hotPaths": [ { "id": "run-lifecycle", "line": 0.98, "branch": 0.87, "lines": 185, "branches": 56 } ],
  "edgeCases": [ { "id": "run-nonzero-exit", "test": "ClimberWeb.Tests.RunServiceTests.Run_NonZeroExit_IsFailed_WithReason", "passed": true } ],
  "ui": { "generated": "…", "head": "bdbf4ab", "suites": [ { "id": "theme-nav", "passed": true, "checks": 22 } ] }
}
```

Parsing stays tolerant and backward compatible: schema 1 (generated/tests/coverage only)
still parses; unknown/missing fields degrade to empty. A missing artifact remains "not
measured yet".

**Fingerprint instead of head-only staleness.** The artifact must be committed in the same
commit it describes, so a HEAD comparison would report "one commit behind" immediately
after committing. Instead the script records a SHA-256 over the measured inputs (source,
tests, harnesses, styling, rubric; excluding `bin`, `obj`, `node_modules`). The page
recomputes it from the working tree, so it catches both later commits and uncommitted
edits. `head` is kept for human context and "N commits back". A missing or mismatched
fingerprint version reports "freshness unknown" rather than guessing that the old rule still
applies.

## Code shape

- `ClimberWeb/Services/CoverageRating.cs` — rubric records + parsing, `Evaluate` (pure),
  band thresholds, staleness description. No rendering logic.
- `ClimberWeb/Services/CoverageInputs.cs` — fingerprint rule + hashing, mirrored in
  `scripts/status.ps1`.
- `ClimberWeb/Services/CoverageArtifact.cs` — tolerant schema-2 parsing, schema-1 fallback.
- `ClimberWeb/Services/ProjectStatusService.cs` — reads artifact + rubric, computes rating
  and freshness once per page load; still never throws.
- `ClimberWeb/Components/Pages/Status.razor` (+ scoped CSS) — renders the panel.
- `status/coverage-rubric.json` — hot paths, thresholds, edge scenarios, UI suites, weights,
  known gaps. Read by both the script and the page.
- `scripts/status.ps1` — tests + coverage + hot paths + TRX edge matches + fingerprint.
- `scripts/status-ui.ps1` — free browser suites against a live host, merged into `ui`.

## Verification / done

- `dotnet test` green with new unit tests for schema-2 parsing, rubric parsing, every
  scoring dimension, the failing gate, band boundaries, staleness states, and the
  fingerprint.
- `scripts/status.ps1` regenerated and committed; README badges/paragraph updated to match.
- Free harnesses `theme-nav`, `security`, `providers`, `polish` run against an unseeded
  host and recorded; `surfaces.mjs` and seeded `opencode` runs **not run** and named as
  such.
- Panel checked visually at 900 and 375, light and dark, including the stale state; keyboard
  reachability and accessible names checked for the rating, meters and summary.
- `/code-review` since `bdbf4ab`; then one logical commit.

## Out of scope

- Running tests or browser suites on page load; the page stays cheap and read-only.
- Counting the paid harness or seeded runs; only their absence is recorded.
- Per-file coverage heat maps, historical trends, or a CI producer.
