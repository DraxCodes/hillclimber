namespace ClimberWeb.Services;

/// <summary>Aggregates a spend report into the headline figures the dashboard shows.</summary>
public static class SpendSummary
{
    /// <summary>
    /// Rolling 30-day spend: the sum of OpenRouter's activity rows (the API already returns
    /// the last 30 days). Empty input is zero, not an error.
    /// </summary>
    public static double Monthly(IReadOnlyList<OpenRouterActivityRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Sum(row => row.Usage);
    }
}
