# ADR 0001: Coverage rating model

- **Status:** accepted
- **Date:** 2026-11-16
- **Context:** `/status` coverage panel, `scripts/status.ps1`, `status/coverage-rubric.json`

## Context

The coverage panel showed a stale artifact as if it were current (311 tests / 63.11% line
when the suite was 461 and green), and reduced "coverage" to a single line percentage. A
line percentage cannot answer the questions the page is for: are the hot paths tested, are
edge cases tested, and is the UI exercised as a running app?

The available signals are uneven:

- `dotnet test` + coverlet measures line and branch coverage per class, but **never executes
  Razor components**, so every UI component reports 0%.
- The free Playwright suites exercise the running UI but produce no Cobertura data.
- "Hot path" and "edge case" are judgements, not measurements.
- The paid `surfaces.mjs` harness is not part of the default checks.

## Decision

Use a **hybrid** rating:

1. `scripts/status.ps1` measures totals, per-hot-path line/branch coverage, and verifies
   named edge-case tests against the TRX run. `scripts/status-ui.ps1` records free browser
   suite results against a live host.
2. `status/coverage-rubric.json` owns the judgement: the hot-path class map, thresholds
   (line ≥ 80%, branch ≥ 60%), the named edge scenarios, which browser suites count, the
   dimension weights and the band edges.
3. `ClimberWeb/Services/CoverageRating.cs` contains one pure `CoverageRater` that combines
   the two into a 0–100 score, three weighted dimensions, named gaps and a band. The page
   renders the score **with its inputs and gaps**, never alone.

Weights: hot paths 40, edge cases 30, UI integration 30. Edge cases are 60% named
scenarios and 40% hot-path branch coverage against a 75% target. Bands: strong ≥ 85,
solid ≥ 70, thin ≥ 55, weak below. A red measured suite bands `failing` and caps the score
at 25, because coverage claims are meaningless when the suite is red.

Staleness is decided by a **fingerprint over the measured inputs**, not by HEAD: the
artifact is committed in the same commit it describes, so HEAD would always look one
commit behind. The page recomputes the fingerprint from the working tree, catches later
commits and uncommitted edits, and shows the last-known rating marked unverified when it
does not match. A fingerprint-version mismatch reports "freshness unknown" rather than
guessing.

## Consequences

- Editing the rubric changes the rating without rerunning tests; editing the class map or
  edge list requires regenerating the artifact.
- Browser evidence requires a live host and a second script; without it the UI dimension
  scores zero and the gap is named. Evidence recorded at a different commit than the tests
  is not counted either — the mismatch is named instead of being silently attributed.
- The paid harness and first-run UI states are declared gaps; they never contribute points.
- The score is deliberately not line coverage. Raw line/branch totals are shown separately,
  with the reason they are low (Razor components are not executed by `dotnet test`).

## Alternatives rejected

- **Measured only:** cannot name hot paths or UI flows, and would report the UI at 0%.
- **Curated only:** an unverified checklist drifts silently, which is the failure this ADR
  fixes.
- **Line percentage with a new label:** a single number with no breakdown, explicitly
  rejected by the requirement.
