namespace ClimberWeb.Services;

/// <summary>
/// Resolves the listen URL and enforces the local-only default: the shipped PIN must never
/// be exposed beyond loopback. A real PIN (in <c>appsettings.local.json</c> or an environment
/// variable) is required to bind the dashboard to the LAN.
/// </summary>
public static class ClimberBinding
{
    /// <summary>The PIN used when none is configured. Only safe on loopback.</summary>
    public const string DefaultPin = "1234";

    /// <summary>The default listen URL when none is configured.</summary>
    public const string DefaultUrl = "http://0.0.0.0:5179";

    /// <summary>The loopback-only URL used when the default PIN would otherwise be exposed.</summary>
    public const string LoopbackUrl = "http://localhost:5179";

    /// <summary>The resolved listen URL and whether the default PIN forced a loopback bind.</summary>
    public sealed record Result(string Urls, bool UsedDefaultPin, bool DowngradedToLoopback);

    /// <summary>
    /// Resolves the effective listen URL. A missing/empty PIN falls back to
    /// <see cref="DefaultPin"/>; if that default would be reachable beyond loopback, the URL
    /// is downgraded to <see cref="LoopbackUrl"/>.
    /// </summary>
    public static Result Resolve(string? configuredUrls, string? configuredPin)
    {
        var pin = ResolvePin(configuredPin);
        var usedDefault = string.Equals(pin, DefaultPin, StringComparison.Ordinal);
        var urls = string.IsNullOrWhiteSpace(configuredUrls) ? DefaultUrl : configuredUrls.Trim();

        if (usedDefault && !IsLoopbackOnly(urls))
        {
            return new Result(LoopbackUrl, UsedDefaultPin: true, DowngradedToLoopback: true);
        }

        return new Result(urls, usedDefault, DowngradedToLoopback: false);
    }

    /// <summary>
    /// Returns the configured PIN, trimmed, or <see cref="DefaultPin"/> when none is set.
    /// Trimming matters: <c>"1234 "</c> must still be treated as the default and kept off
    /// the LAN.
    /// </summary>
    public static string ResolvePin(string? configuredPin) =>
        string.IsNullOrWhiteSpace(configuredPin) ? DefaultPin : configuredPin.Trim();

    /// <summary>
    /// Whether every URL in a semicolon-separated listen list is loopback. Anything that
    /// cannot be parsed is treated as non-loopback so the caller fails closed.
    /// </summary>
    public static bool IsLoopbackOnly(string urls)
    {
        var any = false;

        foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            any = true;

            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || !IsLoopbackHost(uri.Host))
            {
                return false;
            }
        }

        return any;
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host == "127.0.0.1"
        || host == "::1"
        || host == "[::1]";
}
