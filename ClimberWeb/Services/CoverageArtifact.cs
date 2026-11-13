using System.Globalization;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>
/// Reads the committed coverage artifact (<c>status/coverage.json</c>) produced by the
/// status script. Parsing is tolerant: unknown or missing fields degrade to defaults
/// rather than throwing, and a missing file is simply "not measured yet".
/// </summary>
public static class CoverageArtifact
{
    /// <summary>Repository-relative path of the artifact.</summary>
    public const string RelativePath = "status/coverage.json";

    /// <summary>Parses the artifact JSON, returning null when it is absent or unusable.</summary>
    public static CoverageSummary? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var generated = TryGetDateTime(root, "generated");

            var tests = TryGetObject(root, "tests") ?? root;
            var passed = TryGetInt(tests, "passed") ?? 0;
            var failed = TryGetInt(tests, "failed") ?? 0;
            var skipped = TryGetInt(tests, "skipped") ?? 0;

            double? lineRate = null;
            if (TryGetObject(root, "coverage") is { } coverage)
            {
                lineRate = TryGetDouble(coverage, "line") ?? TryGetDouble(coverage, "lineRate");
            }

            lineRate ??= TryGetDouble(root, "lineRate") ?? TryGetDouble(root, "line");

            return new CoverageSummary(generated, passed, failed, skipped, Normalize(lineRate));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads and parses the artifact at <paramref name="path"/>, or null if absent.</summary>
    public static CoverageSummary? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static double? Normalize(double? value)
    {
        if (value is not { } rate || double.IsNaN(rate) || double.IsInfinity(rate))
        {
            return null;
        }

        return Math.Clamp(rate, 0d, 1d);
    }

    private static JsonElement? TryGetObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static int? TryGetInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? TryGetDouble(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? TryGetDateTime(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(
            value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
}
