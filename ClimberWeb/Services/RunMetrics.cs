namespace ClimberWeb.Services;

/// <summary>
/// Cost and tokens accumulated for a run, summed from opencode's own <c>step_finish</c>
/// events. Shared so the general run and the hillclimb loop agree on what a step costs
/// and never double-count or drift.
/// </summary>
/// <param name="SpendUsd">Total cost in USD, as reported by the provider.</param>
/// <param name="InputTokens">Prompt tokens.</param>
/// <param name="OutputTokens">Completion tokens.</param>
/// <param name="CacheReadTokens">Prompt-cache read tokens.</param>
/// <param name="CacheWriteTokens">Prompt-cache write tokens.</param>
/// <param name="EstimatedSpendUsd">Token-based estimate for providers that report no cost.</param>
public readonly record struct RunMetrics(
    decimal SpendUsd,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    decimal EstimatedSpendUsd = 0m)
{
    /// <summary>No spend and no tokens yet.</summary>
    public static readonly RunMetrics Zero = default;

    /// <summary>Total tokens (input + output).</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Returns these metrics with the <c>step_finish</c> usage of <paramref name="line"/>
    /// added, or the same metrics when the line is not a step event. Cost is rounded after
    /// converting to decimal so summing doubles does not accumulate noise. When
    /// <paramref name="pricing"/> is supplied, the tokens are also priced for a Surplus
    /// estimate; opencode reports <c>0</c> cost for that provider.
    /// </summary>
    public RunMetrics Add(string? line, SurplusPricing? pricing = null)
    {
        if (OpenCodeEvents.TryReadStepUsage(line) is not { } usage)
        {
            return this;
        }

        var cost = Math.Round((decimal)usage.Cost, 8);
        var estimate = pricing?.Estimate(usage.Input, usage.Output + usage.Reasoning, usage.CacheRead, usage.CacheWrite) ?? 0m;
        return this with
        {
            SpendUsd = SpendUsd + cost,
            InputTokens = InputTokens + usage.Input,
            OutputTokens = OutputTokens + usage.Output,
            CacheReadTokens = CacheReadTokens + usage.CacheRead,
            CacheWriteTokens = CacheWriteTokens + usage.CacheWrite,
            EstimatedSpendUsd = EstimatedSpendUsd + estimate,
        };
    }
}
