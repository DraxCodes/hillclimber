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
    /// The dashboard's spend figure. An <see cref="SpendMode.Estimated"/> provider keeps the
    /// <c>est.</c> label even before pricing produces a number; a reported provider keeps its
    /// exact cost. A mixed climb shows both; a provider with neither shows an em dash.
    /// </summary>
    public static string Spend(double reported, double estimated, SpendMode mode)
    {
        if (estimated > 0 && reported > 0)
        {
            return $"est. {Usd(estimated)} + {Usd(reported)}";
        }

        if (estimated > 0 || (mode == SpendMode.Estimated && reported <= 0))
        {
            return $"est. {Usd(estimated)}";
        }

        return reported > 0 ? Usd(reported) : "—";
    }

    /// <summary>The line both services append after a run, using <see cref="Spend"/>.</summary>
    /// <param name="reported">Exact cost reported by opencode/provider.</param>
    /// <param name="estimated">Token-based estimate from catalogue pricing.</param>
    /// <param name="mode">How the provider's spend is known.</param>
    /// <param name="provider">Display name used in the pricing suffix, when known.</param>
    public static string SpendLine(decimal reported, decimal estimated, SpendMode mode, string? provider = null) =>
        (estimated, reported) switch
        {
            (> 0, > 0) => $"Spend this run: est. ${Amount((double)estimated)} + ${Amount((double)reported)}.",
            (> 0, _) => $"Spend this run: est. ${Amount((double)estimated)}{PricingSource(provider)}.",
            _ when mode == SpendMode.Estimated => $"Spend this run: est. $0{PricingUnavailable(provider)}.",
            (_, > 0) => $"Spend this run: ${Amount((double)reported)}.",
            _ => "Spend this run: no cost reported.",
        };

    private static string PricingSource(string? provider) =>
        provider is { Length: > 0 } ? $" ({provider} pricing)" : " (estimated)";

    private static string PricingUnavailable(string? provider) =>
        provider is { Length: > 0 } ? $" ({provider} pricing unavailable)" : " (pricing unavailable)";
}
