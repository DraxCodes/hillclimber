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

    /// <summary>Formats a token count compactly, e.g. <c>12.3k</c>.</summary>
    public static string Tokens(long value) =>
        value >= 1000
            ? $"{(value / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}k"
            : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
