namespace ClimberWeb.Services;

/// <summary>
/// The one truncation rule for display text: keep the head, mark the cut. The live formatter
/// and the stored transcript share it, so a restored row can never disagree with the row it
/// was captured from.
/// </summary>
internal static class Truncation
{
    /// <summary>Returns <paramref name="value"/> when it fits, otherwise its head and an ellipsis.</summary>
    public static string Cut(string value, int maxLength) =>
        value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength), "…");
}