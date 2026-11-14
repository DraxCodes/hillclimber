# Spec: Surplus estimate edge cases

## Context

Surplus reports no cost, so the dashboard prices `step_finish` tokens against the model's
`/models` pricing. That path has three untested edges, and two of them currently misreport:

- A model listed without a `pricing` object is treated as **not found**: `ModelAvailable`
  is false, the state reads "model unavailable", and `Home.razor` offers the OpenRouter
  fallback for a model that works.
- A `pricing` object with only some keys zero-fills the rest through `ReadNumber`, so a
  model priced on `prompt` alone reports output, cache read and cache write as free — an
  understated estimate shown as fact.
- Reasoning tokens and a climb that switches provider mid-run are priced by the code
  (`output + reasoning`, estimate + reported) but nothing asserts either.

## Decisions (locked with the user)

1. **A listed but unpriced model is connected.** `ModelAvailable` stays true, `Pricing` is
   null, and the note says Surplus lists the model but publishes no pricing. No fallback is
   offered; the run proceeds and the existing `est. $0` surfaces take over.
2. **`prompt` and `completion` are required.** Missing or unparseable either one makes the
   model unpriced. A present `0` is a real price. Cache read/write stay optional, defaulting
   to 0, because a free cache tier is legitimate. A non-finite price (`"NaN"`,
   `"Infinity"`) counts as unparseable: one would poison every later estimate.
3. **Per-step rounding stays.** `SurplusPricing.Estimate` keeps `Math.Round(…, 8)`; a test
   pins the drift bound at 5e-9 USD per step rather than moving rounding to the end.

## What it should do

- `SurplusParser.Lookup(json, model)` returns a `SurplusCatalogueEntry(Listed, Pricing)`;
  the pricing-only `ParsePricing` wrapper is gone, so the two meanings cannot collapse again.
- `SurplusClient` distinguishes the three outcomes: HTTP failure / unreachable, model not
  listed, model listed but unpriced, model listed and priced.
- Tests cover: listed-without-pricing, pricing that is not an object, partial pricing,
  non-finite prices, present-zero prompt and cache prices, absent cache prices defaulting to
  zero, reasoning priced at the completion rate while the displayed output count stays
  output-only, a hillclimb that estimates on Surplus then reports exact cost on the approved
  OpenRouter retry, the `est. X + $Y` spend line, and per-step rounding.
- The rounding test uses prices whose per-step value needs a ninth decimal, so it fails if
  rounding moves to the end of the run: 10,000 steps must total `0.01`, not `0.00999999`.

## Out of scope

- Any Surplus usage/billing API (none exists).
- Changing the fallback approval flow, the estimate's label, or the rounding rule.
- UI changes: `ActivityPage` already renders the report message when pricing is absent.

## How we know it is done

- `dotnet test --nologo` green, with the new parser, client, estimate and mixed-climb tests.
- The unpriced-model client test asserts `connected`, `ModelAvailable`, null pricing, and a
  message naming the model.
- The mixed-climb test asserts both `EstimatedSpendUsd` and `SpendUsd` land, and that the
  OpenRouter step adds no estimate.

## Evidence

- **Tests:** `dotnet test --nologo` → **230 passing**, 0 failed (216 before the change).
- **Coverage:** `powershell -File scripts/status.ps1` → 230/230, line coverage 62.3%.
- **Review:** `/code-review 7b4b6a9` found the first rounding test vacuous (the fixture's
  per-step value was already eight decimals, so it could not tell per-step from end-of-run
  rounding) and `"NaN"`/`"Infinity"` prices passing as priced. Both are fixed above; the
  unused `ParsePricing` wrapper is gone.
- **Live check:** **not run**. Exercising the unpriced branch needs a real catalogue answer
  without pricing for the configured model, which Surplus does not currently return.

## Fixed point for review

Session start HEAD for this change is `7b4b6a9`. `/code-review 7b4b6a9`.