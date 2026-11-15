# The polish audit has no score floor and blind spots it cannot see

Status: open
Type: task

`scripts/ui/polish.mjs` measures 560 deterministic checks and prints a UI Polish
Score, but it exits 0 whatever the score is (only a missing cell set or a detected
run makes it fail). So the 100/0 result from the polish hillclimb is a measurement,
not a regression gate — the next CSS change can silently drop it.

While hardening the harness, close these gaps found during the run:

1. Add a `--min-score` flag (or a commit-time threshold) so CI can fail on a
   regression instead of relying on a human reading the number.
2. The header brand/nav collision that the run fixed by hand was caught only by
   the `overflow` check. Add an explicit bounding-box overlap assertion for the
   brand, nav and theme toggle.
3. Only 320, 375, 768 and 1280px viewports are audited. The 341–480px band, where
   the header previously broke, is untested.
4. Hover and active states are never measured; only resting and focus states are.
5. `ReconnectModal` is outside the audit entirely — its contrast fix was verified
   by hand with sample data.

Paid `scripts/ui/surfaces.mjs` stays out of the loop; anything added here must run
against an already-running dashboard (`polish.mjs` exits 3 if a run is active).