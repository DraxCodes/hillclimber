using System.Globalization;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>Measured line and branch coverage for one named hot path.</summary>
/// <param name="Id">Rubric identifier.</param>
/// <param name="LineRate">Covered lines as a fraction in 0..1, or null when unmeasured.</param>
/// <param name="BranchRate">Covered branch conditions as a fraction in 0..1, or null.</param>
/// <param name="Lines">Total lines counted.</param>
/// <param name="Branches">Total branch conditions counted.</param>
public sealed record HotPathCoverage(string Id, double? LineRate, double? BranchRate, int Lines, int Branches);

/// <summary>Outcome of one named edge-case scenario in the measured test run.</summary>
/// <param name="Id">Rubric identifier.</param>
/// <param name="Test">TRX test name that was looked up.</param>
/// <param name="Passed">True when every matching test result passed.</param>
public sealed record EdgeCaseEvidence(string Id, string Test, bool Passed);

/// <summary>Result of one browser suite recorded against a live host.</summary>
/// <param name="Id">Rubric identifier.</param>
/// <param name="Passed">True when the suite exited cleanly with no failed checks.</param>
/// <param name="Checks">Checks or cells the suite reported.</param>
public sealed record UiSuiteEvidence(string Id, bool Passed, int Checks);

/// <summary>Browser evidence merged into the artifact by <c>scripts/status-ui.ps1</c>.</summary>
/// <param name="Generated">When the suites were run.</param>
/// <param name="Head">Commit the host was built from, when recorded.</param>
/// <param name="Suites">Recorded suite results.</param>
public sealed record UiEvidence(DateTimeOffset? Generated, string? Head, IReadOnlyList<UiSuiteEvidence> Suites)
{
    /// <summary>True when at least one suite result was recorded.</summary>
    public bool Ran => Suites.Count > 0;
}

/// <summary>
/// Reads the committed coverage artifact (<c>status/coverage.json</c>) produced by the
/// status script. Parsing is tolerant: unknown or missing fields degrade to defaults
/// rather than throwing, and a missing file is simply "not measured yet". Schema 1
/// artifacts (generated/tests/coverage only) still parse; they just carry no rating inputs.
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
            var head = TryGetString(root, "head");

            var tests = TryGetObject(root, "tests") ?? root;
            var passed = TryGetInt(tests, "passed") ?? 0;
            var failed = TryGetInt(tests, "failed") ?? 0;
            var skipped = TryGetInt(tests, "skipped") ?? 0;

            double? lineRate = null;
            double? branchRate = null;
            if (TryGetObject(root, "coverage") is { } coverage)
            {
                lineRate = TryGetDouble(coverage, "line") ?? TryGetDouble(coverage, "lineRate");
                branchRate = TryGetDouble(coverage, "branch") ?? TryGetDouble(coverage, "branchRate");
            }

            lineRate ??= TryGetDouble(root, "lineRate") ?? TryGetDouble(root, "line");
            branchRate ??= TryGetDouble(root, "branchRate") ?? TryGetDouble(root, "branch");

            return new CoverageSummary(
                generated,
                passed,
                failed,
                skipped,
                Normalize(lineRate),
                Normalize(branchRate),
                head,
                ParseFingerprint(root),
                ParseHotPaths(root),
                ParseEdgeCases(root),
                ParseUi(root));
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

    private static CoverageFingerprint? ParseFingerprint(JsonElement root)
    {
        if (TryGetObject(root, "fingerprint") is not { } fingerprint)
        {
            return null;
        }

        // A missing version is not the current rule: it is an artifact whose rule is
        // unknown, so it must fall through to "freshness unknown" rather than be trusted.
        return TryGetInt(fingerprint, "version") switch
        {
            { } version => new CoverageFingerprint(version, TryGetString(fingerprint, "inputs")),
            null => null,
        };
    }

    private static IReadOnlyList<HotPathCoverage> ParseHotPaths(JsonElement root)
    {
        var paths = new List<HotPathCoverage>();
        foreach (var item in Array(root, "hotPaths"))
        {
            var id = TryGetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            paths.Add(new HotPathCoverage(
                id,
                Normalize(TryGetDouble(item, "line")),
                Normalize(TryGetDouble(item, "branch")),
                TryGetInt(item, "lines") ?? 0,
                TryGetInt(item, "branches") ?? 0));
        }

        return paths;
    }

    private static IReadOnlyList<EdgeCaseEvidence> ParseEdgeCases(JsonElement root)
    {
        var cases = new List<EdgeCaseEvidence>();
        foreach (var item in Array(root, "edgeCases"))
        {
            var id = TryGetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            cases.Add(new EdgeCaseEvidence(
                id,
                TryGetString(item, "test") ?? string.Empty,
                TryGetBool(item, "passed") ?? false));
        }

        return cases;
    }

    private static UiEvidence? ParseUi(JsonElement root)
    {
        if (TryGetObject(root, "ui") is not { } ui)
        {
            return null;
        }

        var suites = new List<UiSuiteEvidence>();
        foreach (var item in Array(ui, "suites"))
        {
            var id = TryGetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            suites.Add(new UiSuiteEvidence(
                id,
                TryGetBool(item, "passed") ?? false,
                TryGetInt(item, "checks") ?? 0));
        }

        return new UiEvidence(TryGetDateTime(ui, "generated"), TryGetString(ui, "head"), suites);
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

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

    private static string? TryGetString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? TryGetBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
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
