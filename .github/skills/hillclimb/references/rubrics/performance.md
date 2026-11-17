# Performance rubric (`performance`)

Use when the target is measurable: latency, throughput, memory, bundle or image
size, build/test time, query count, cost per request. The score is derived from a
budget table, not from taste.

The loop and the floor are unchanged. The difference is that **the budget table is
declared before the first score and does not move during the run.** A metric that
drifts to meet the change is not evidence; it is the change arguing for itself.

## Declare the budget first

Write this before measuring. The `plan-architect` produces it; the
`quality-gatekeeper` checks it was declared first.

| Field | Meaning |
| --- | --- |
| Metric | One number, named precisely (`p95 latency`, `peak RSS`, `bundle gzip`, `wall time`) |
| Baseline | The current value, from the same method |
| Target | The value that earns an 8 |
| Method | Command or script, dataset size, warm/cold, concurrency |
| Environment | Machine or CI image, CPU, memory, network |

Rules:

- One primary budget and any secondary budgets. A primary miss cannot be offset by
  a secondary win.
- The target comes from a requirement, a comparable product, or a stated user
  need — not from whatever the change happened to achieve.
- If the budget changes mid-run, the run restarts; the previous scores are void.

## How to look

1. **Baseline, same method.** Measure the unmodified artifact with the exact method
   above. A baseline from a different dataset, machine or cache state is void.
2. **Typical and tail.** Record p50 and p95 (and p99 when the metric is latency).
   A mean alone hides the tail; an improved mean with a worse p95 is not an
   improvement.
3. **Cold and warm.** First call after start, and steady state. Report both when
   the path caches.
4. **Data size sweep.** Zero, one, typical and maximum supported size. Note where
   the curve stops being flat.
5. **Concurrency.** 1 and the expected concurrent load. Look for contention,
   queueing, lock waits and connection exhaustion.
6. **Adjacent budgets.** Does the change regress something else — startup time,
   memory, build time, another endpoint? A win that pays for itself with a
   regression elsewhere is not a win.
7. **Repeat.** Three runs; report the median and the spread. One lucky run is not
   evidence.

## Levels

Scores are against the declared budget.

### 10 — Comfortably inside, no regressions

- Every budget met with at least 20% margin, including the tail.
- No adjacent budget regresses beyond noise.
- The method, dataset and environment are recorded, and a second person could
  reproduce the number.
- Cost (CPU, memory, money) is documented for the expected load.

### 9 — Inside, with one named edge

- Primary and secondary budgets met; tail stable.
- Exactly one named, low-exposure variance remains: a rare data shape, a cold
  start, or a single noisy run explained and bounded.

### 8 — Meets the budget

- The primary budget is met on the declared method, including p95 when latency is
  the metric.
- No adjacent budget regresses beyond the stated noise band.
- The result is reproducible across three runs.

### 7 — Close

- The primary budget is met on the median but the tail is outside it, or one
  secondary budget is missed, or one adjacent budget regresses inside the noise
  band.
- The main path is otherwise sound.

### 6 — Partly met

- The primary budget is missed by less than 25%, or met only warm, or met only at
  the smallest data size.
- No functional breakage; the shape of the cost is understood and named.

### 5 — Missed, explained

- The primary budget is missed by 25–50%, with a named cause and a plan.
- The change is still an improvement over baseline, or the baseline was already
  outside budget.

### 4 — Missed, unexplained

- The primary budget is missed by more than 50%, or the cause is unknown.
- A secondary budget or an adjacent metric regresses enough to be noticed by a
  user.

### 3 — Regresses

- The change is slower or heavier than baseline on the primary metric.
- The method is reproducible, so the regression is real.

### 2 — Unusable at scale

- The artifact fails or degrades to unusable at the expected data size or
  concurrency (timeout, OOM, queue collapse).

### 1 — Cannot run

- The artifact cannot complete the measured path at all.

## Evidence a score must cite

A score without this evidence is treated as below 8.

- **Budget table:** the declared metric, baseline, target, method and environment,
  with the timestamp that proves it preceded the first score.
- **Numbers:** baseline and candidate, per run, with median and spread; p50/p95
  (p99 for latency).
- **Conditions:** dataset size, warm/cold, concurrency.
- **Adjacent budgets:** what else was measured and did not regress.
- **Floor:** the exact commands run and their observed result; anything not run is
  labelled "not run".

Scorecard shape:

```text
Rubric: performance
Score: 7/10 — Close
Target: 8
Budget: p95 250ms target, 400ms baseline; 10k rows; warm; 4 workers; CI image X
Measured: p95 210ms (runs 205/210/214); p50 90ms; no adjacent regression
Flaw holding it at 7: p95 at 100k rows is 900ms — tail, large dataset
Why not 8: <the level-8 trait that is missing>
Floor: <command> <result>; <not run>
```

## Anti-inflation rules

- "Faster" without a baseline from the same method is void.
- Report the median of at least three runs; a single run is not evidence.
- An improved mean with a worse p95 is not an improvement.
- A target chosen after the measurement is void; re-declare and re-run.
- Do not credit a budget met only at the smallest data size as a general pass.
- A performance score cannot hide a red floor: correctness still gates the ship.