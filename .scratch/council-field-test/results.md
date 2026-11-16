# Council field test — results

Date: 2026-11-16. Purpose: settle the council's default seat mix on evidence rather than a
price guess, before finalising the council.

## Method

- Three briefs, identical for every model:
  - **A** — interactive questions (live feature, experience lens);
  - **B** — session persistence (shipped feature, experience lens, with an answer key that
    records what actually mattered: the delete ritual, focus management, ghost live rows,
    resume identity);
  - **C** — interactive questions again, as the **risk** lens: posture, ranked failure modes,
    the one thing to cut.
- Five models per brief, `#medium` effort, the `general` subagent, the same prompt and the
  same reading list. The only variable was the model.
- Outputs written to `out/<brief>-<model>.md`, then shuffled into `blind/` and scored without
  the labels. `_mapA.json`, `_mapB.json` and `_mapc.json` were opened only after the blind
  scores were written to `scores-a.md`, `scores-b.md` and `scores-c.md`.
- Signals fixed before scoring: unique valid concerns, precision, brief adherence, evidence
  quality; plus 1–5 on thesis concreteness, shippable copy, trade-off position strength, felt
  read (A, B), and posture clarity, ranking quality, constraint sharpness, grounding (C).

## Result

| Model | Brief B | Brief A | Brief C (risk) | Mean | Reference $/M in–out |
| --- | --- | --- | --- | --- | --- |
| `deepseek-v4.1-flash-1017` | **1** | 2 | **1** | **1.33** | 0.15–0.30 / 0.60–1.20 |
| `glm-5.3-flash` | 2 | **1** | 4 | 2.33 | 0.04 / 0.08 |
| `qwen3.8-flash` | 3 | 3 | 3 | 3.00 | 0.15 / 0.47 |
| `claude-sonnet-5` | 4 | 4 | 2 | 3.33 | 2 / 10 |
| `claude-haiku-4.5` | 5 | 5 | 5 | 5.00 | 1 / 5 |

Read it as three lenses, not one score:

- **DeepSeek** is first or second on every brief, and first on risk. It is the strongest
  generalist here and the chair is already on it.
- **GLM** wins experience (1st on A, 2nd on B) and loses it on risk (4th) — it reasons about
  how a thing feels, not about what breaks.
- **Sonnet 5** is the mirror: 4th on both experience briefs, 2nd on risk. Buying it for taste
  would be buying the wrong thing.
- **Qwen** placed third on all three. Consistent, never better than median — diversity without
  a demonstrated benefit.
- **Haiku 4.5** last on all three. Its one strong idea (strict schema validation) came
  attached to a speculative rank-1 and a premise that is wrong for a single-user app.

What the winners actually produced, from the blind read:

- GLM (A, 1st): the fold keeps the unchosen options reachable via `TraceCollapsible`, answer
  state must survive a re-render, and a Tab-count test (≤3) for default focus. Four unique
  valid concerns, zero precision failures.
- DeepSeek (A 2nd, B 1st, C 1st): the one-answer-path synthesis (the composer answers while a
  question is open), Enter-selects-without-committing, the restored fold state except the live
  turn, and on C the consent framing — a model-shaped "Allow access to `C:\Users\…`?" card
  turns a click into filesystem authority — plus resume-via-`--session` instead of holding a
  process open.
- Sonnet (C 2nd): the immutable question/answer pair for audit, and "do not ship the answer
  endpoint while the cleartext session finding is open". Both are genuinely new; both are also
  the kind of thing an auditor finds, not a designer.
- Sonnet (A 4th): the best structural call in the set (compose the question from
  `Trace`/`TraceLine` rather than inventing a fourth bordered surface), then undermined it by
  truncating option labels behind a tooltip.
- Haiku (A 5th): `aria-pressed` toggles for a choose-one group — the wrong semantic — and a
  hedged cut.

## Side finding: the `min80` floor

A model is only usable if some seller clears an 80% discount. Verified with one-token calls
against the configured base URL:

- routable: `deepseek-v4.1-flash-1017`, `glm-5.3-flash`, `qwen3.8-flash`,
  `claude-haiku-4.5`, `claude-sonnet-4.6`, `claude-sonnet-5`, `claude-opus-4.8`;
- **404**: `claude-sonnet-4.5` (the model previously configured), `claude-haiku-5.5`
  (best discount 45.0%), `claude-sonnet-5.5` (best 31.0%).

## Cost

Per seat, assuming ~40k input and ~3k output per round at the 20% floor:

| Seat model | Per seat | Per council (4 seats) |
| --- | --- | --- |
| `glm-5.3-flash` | ~0.02¢ | — |
| `qwen3.8-flash` | ~0.10¢ | — |
| `deepseek-v4.1-flash-1017` | ~0.25¢ | — |
| `claude-sonnet-5` | ~1.6¢ | — |

- All-cheap mix (DeepSeek ×3 + GLM): **~0.8¢** per round, ~1.5¢ with cross-critique.
- Three lineages (DeepSeek ×2 + GLM + Sonnet): **~2.3¢** per round, ~4.5¢ with cross-critique.

## Decision

- **Chair and shape:** `deepseek-v4.1-flash-1017#medium`.
- **Experience:** `glm-5.3-flash#medium` — first on the live experience brief, second on the
  known-answer brief, at roughly 1/50th the price of a premium seat.
- **Risk:** `claude-sonnet-5#medium` — second on the risk brief, and the only seat that raised
  the immutable question/answer audit pair and the "do not ship the answer endpoint while the
  cleartext session finding is open" gate. Both are unique high-value concerns, which is the
  pre-registered test a premium seat has to pass.
- **Not defaulted:** `qwen3.8-flash` (third on all three briefs — diversity without a
  demonstrated benefit), `claude-haiku-4.5` (last on all three), `claude-sonnet-4.6` (not
  tested; kept configured for escalation), `claude-opus-4.8` (on request only).
- **Cost:** ~2.3¢ per round, ~4.5¢ for a full council with cross-critique — once per large
  feature.

The governing principle: **a seat sits on the lens it won, not on its mean.** A model that is
mediocre at taste and good at risk is a risk seat, not a compromise.

## Limits, stated plainly

- **One judge, shared lineage.** The chair that scored is DeepSeek, one of the contestants. A
  blind shuffle removes the label, not the stylistic bias. DeepSeek scored 2nd on A, 1st on B
  and 1st on C, so the bias did not simply favour itself — but a second judge on a different
  lineage would strengthen the result.
- **Three briefs, one run each.** Enough to separate a 50× cost difference, not enough to
  resolve a one-place gap.
- **Home-field advantage.** Brief B's answer key was written in a repo whose docs were
  authored by DeepSeek-lineage agents; every seat was asked to match house style.
- **Latency is not comparable.** The probes ran in waves, so wall-clock order is confounded.
- **Cost was not metered per child.** Input volume is near-identical by construction (same
  brief, same docs), so relative cost follows the published price table rather than a
  measurement. Treat the cents above as ceilings at the `min80` floor.
- **Re-probe before swapping a seat.** The marketplace moves; a route that works today can
  404 tomorrow.