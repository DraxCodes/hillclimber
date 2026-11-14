namespace ClimberWeb.Services;

/// <summary>Aggregates a spend report into the headline figures the dashboard shows.</summary>
public static class SpendSummary
{
    /// <summary>
    /// Rolling 30-day spend: the sum of the provider's activity rows (the API already returns
    /// the last 30 days). Empty input is zero, not an error.
    /// </summary>
    public static double Monthly(IReadOnlyList<ProviderActivityRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Sum(row => row.Usage);
    }

    /// <summary>
    /// The main page's total: the rolling 30-day figure (rendered <c>$/pm</c>) once activity
    /// exists, else the all-time account total, else an em dash. The fallback keeps a fresh
    /// account — whose spend is all inside today and so excluded from activity — from reading
    /// as <c>$0.00/pm</c>.
    /// </summary>
    public static string Headline(SpendReport? report)
    {
        if (report is null)
        {
            return "—";
        }

        return report.Activity.Count > 0
            ? SpendFormat.UsdPerMonth(Monthly(report.Activity))
            : report.Account is { } account ? SpendFormat.Usd(account.TotalUsage) : "—";
    }
}
