# Surplus primary provider + approved OpenRouter fallback

## Problem

The dashboard still reports on OpenRouter even though inference moved to Surplus
Intelligence. Surplus exposes only `GET /models` (open, no auth) with per-token pricing;
it has no key/credits/activity/usage endpoint. If Surplus is down or rejects the model,
the app currently has no way to continue on OpenRouter, and no way to ask the user first.

## Decisions

1. **Surplus is the primary provider.** The header shows `surplus: <state>` and owns the
   per-run/per-climb spend figure. OpenRouter is the fallback provider and keeps its
   account/credits/30-day activity on `/activity`.
2. **Fallback always needs explicit approval.** Nothing switches silently. Approval is
   offered in three places: before a run when the Surplus pre-flight fails, after a failed
   Surplus run ("retry on OpenRouter"), and through a manual provider switch that asks for
   confirmation.
3. **Surplus spend is an estimate.** No usage API exists, so the app computes
   `tokens × pricing` from `step_finish` tokens and the configured model's `/models`
   pricing. It is labelled `est.`; OpenRouter runs keep their exact reported cost.
4. **Both methods are covered.** A single run retries after approval. A hillclimb pauses
   at `AwaitingApproval`, then retries that iteration on OpenRouter and continues the loop
   on OpenRouter for the remaining iterations.
5. **Configuration.** `Climber:SurplusApiKey` (or `SURPLUS_API_KEY`),
   `Climber:SurplusBaseUrl`, `Climber:SurplusModel`, `Climber:FallbackModel`. Defaults:
   `https://api.surplusintelligence.ai/min80/v1`,
   `surplus/deepseek-v4.1-flash-1017`,
   `openrouter/deepseek/deepseek-v4.1-flash`.
6. **`/activity` shows Surplus above OpenRouter.** Surplus gets connection, model,
   pricing, and the current run/climb estimate + tokens; OpenRouter stays below labelled
   "fallback provider".

## Non-goals

- No persisted spend ledger or hypothetical 30-day Surplus graph (there is no API for it).
- No automatic or silent fallback.
- No provider switching for the opencode server itself; only the model passed to
  `opencode run --model`.

## Acceptance

- A normal run logs `--model surplus/deepseek-v4.1-flash-1017`, completes, and the header
  reads `surplus: connected` with an `est.` spend.
- A failed Surplus run exposes `FallbackAvailable`; approving restarts with the fallback
  model and exact OpenRouter spend.
- A failed Surplus hillclimb iteration enters `AwaitingApproval`; approving retries that
  iteration on OpenRouter and finishes the loop.
- An unreachable Surplus at start asks before launching on OpenRouter.
- `dotnet test` passes; `/status` and `/activity` still render behind the PIN gate.