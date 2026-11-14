namespace ClimberWeb.Services;

/// <summary>Formatting shared by the spend surfaces so they never drift apart.</summary>
public static class SpendFormat
{
    /// <summary>
    /// Formats a USD amount: sub-dollar costs keep up to six decimals so tiny step costs are
    /// visible, while larger totals use two. Null renders an em dash.
    /// </summary>
    public static string Usd(double? value) =>
        value is not { } amount ? "—" : "$" + Amount(amount);

    /// <summary>Formats a non-null USD amount (see <see cref="Usd"/>).</summary>
    public static string Amount(double amount) =>
        amount is > 0 and < 1
            ? amount.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            : amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Formats a rolling-month USD figure, e.g. <c>$2.80/pm</c>. Null renders <c>—/pm</c>.</summary>
    public static string UsdPerMonth(double? value) =>
        value is not { } amount ? "—/pm" : "$" + Amount(amount) + "/pm";

    /// <summary>Formats a token count compactly, e.g. <c>12.3k</c>.</summary>
    public static string Tokens(long value) =>
        value >= 1000
            ? $"{(value / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}k"
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The dashboard's spend figure. Surplus has no reported cost, so its token estimate is
    /// always labelled <c>est.</c> even before pricing produces a number; OpenRouter keeps its
    /// exact reported cost. A mixed climb shows both.
    /// </summary>
    public static string Spend(double reported, double estimated, AiProvider? provider)
    {
        if (estimated > 0 && reported > 0)
        {
            return $"est. {Usd(estimated)} + {Usd(reported)}";
        }

        if (estimated > 0 || provider == AiProvider.Surplus)
        {
            return $"est. {Usd(estimated)}";
        }

        return Usd(reported);
    }

    /// <summary>The line both services append after a run, using <see cref="Spend"/>.</summary>
    public static string SpendLine(decimal reported, decimal estimated, AiProvider? provider) =>
        (estimated, reported, provider) switch
        {
            (> 0, > 0, _) => $"Spend this run: est. ${Amount((double)estimated)} + ${Amount((double)reported)}.",
            (> 0, _, _) => $"Spend this run: est. ${Amount((double)estimated)} (Surplus pricing).",
            (_, _, AiProvider.Surplus) => "Spend this run: est. $0 (Surplus pricing unavailable).",
            _ => $"Spend this run: ${Amount((double)reported)}.",
        };
}
