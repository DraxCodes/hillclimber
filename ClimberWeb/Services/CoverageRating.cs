using System.Globalization;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>One hot path: a named area of the product mapped to classes in the report.</summary>
/// <param name="Id">Stable identifier shared with the artifact.</param>
/// <param name="Label">Human label shown on the page.</param>
/// <param name="Classes">Cobertura class names (or type prefixes) that make up the path.</param>
public sealed record HotPathRule(string Id, string Label, IReadOnlyList<string> Classes);

/// <summary>A named edge-case scenario, verified by the presence of a passing test.</summary>
/// <param name="Id">Stable identifier shared with the artifact.</param>
/// <param name="Label">Human label shown on the page.</param>
/// <param name="Test">Exact TRX test-name prefix, e.g. <c>Namespace.Class.Method</c>.</param>
public sealed record EdgeCaseRule(string Id, string Label, string Test);

/// <summary>A browser suite that contributes (free) or does not contribute (paid) to the UI score.</summary>
/// <param name="Id">Stable identifier shared with the artifact.</param>
/// <param name="Label">Human label shown on the page.</param>
/// <param name="Script">File name under <c>scripts/ui</c>.</param>
/// <param name="Paid">True when the suite reaches a paid provider and is never run by status scripts.</param>
public sealed record UiSuiteRule(string Id, string Label, string Script, bool Paid);

/// <summary>Relative importance of the three rated dimensions.</summary>
public sealed record RatingWeights(double HotPaths, double EdgeCases, double Ui)
{
    /// <summary>Weights used when the rubric omits them.</summary>
    public static readonly RatingWeights Default = new(40, 30, 30);

    /// <summary>Sum of the weights, used to normalise the composite.</summary>
    public double Total => HotPaths + EdgeCases + Ui;
}

/// <summary>Score floor for each band, highest first.</summary>
public sealed record BandThresholds(int Strong, int Solid, int Thin)
{
    /// <summary>Band edges used when the rubric omits them.</summary>
    public static readonly BandThresholds Default = new(85, 70, 55);
}

/// <summary>
/// The committed judgement behind the coverage rating: which areas are hot, which edge
/// cases matter, which browser suites count, and how much each dimension weighs. The
/// measured numbers come from the artifact; this file only supplies the bar and the
/// labels, so the two can be argued with separately.
/// </summary>
public sealed record CoverageRubric(
    IReadOnlyList<HotPathRule> HotPaths,
    IReadOnlyList<EdgeCaseRule> EdgeCases,
    IReadOnlyList<UiSuiteRule> UiSuites,
    IReadOnlyList<string> KnownGaps,
    double LineThreshold,
    double BranchThreshold,
    double EdgeBranchTarget,
    RatingWeights Weights,
    BandThresholds Bands)
{
    /// <summary>Repository-relative path of the rubric.</summary>
    public const string RelativePath = "status/coverage-rubric.json";

    /// <summary>Parses the rubric, or returns null when it is absent or unusable.</summary>
    public static CoverageRubric? Parse(string? json)
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

            var hotPaths = new List<HotPathRule>();
            foreach (var item in Array(root, "hotPaths"))
            {
                var id = Text(item, "id");
                var label = Text(item, "label");
                var classes = Array(item, "classes")
                    .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : null)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToArray();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(label) && classes.Length > 0)
                {
                    hotPaths.Add(new HotPathRule(id, label, classes));
                }
            }

            var edges = new List<EdgeCaseRule>();
            foreach (var item in Array(root, "edgeCases"))
            {
                var id = Text(item, "id");
                var label = Text(item, "label");
                var test = Text(item, "test");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(test))
                {
                    edges.Add(new EdgeCaseRule(id, label, test));
                }
            }

            var suites = new List<UiSuiteRule>();
            foreach (var item in Array(root, "uiSuites"))
            {
                var id = Text(item, "id");
                var label = Text(item, "label");
                var script = Text(item, "script");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(script))
                {
                    suites.Add(new UiSuiteRule(id, label, script, Bool(item, "paid") ?? false));
                }
            }

            var gaps = Array(root, "knownGaps")
                .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToArray();

            if (hotPaths.Count == 0)
            {
                return null;
            }

            var thresholds = TryGetObject(root, "thresholds");
            return new CoverageRubric(
                hotPaths,
                edges,
                suites,
                gaps,
                Rate(thresholds, "line") ?? Rate(root, "lineThreshold") ?? 0.8,
                Rate(thresholds, "branch") ?? Rate(root, "branchThreshold") ?? 0.6,
                Rate(thresholds, "edgeBranchTarget") ?? Rate(root, "edgeBranchTarget") ?? 0.75,
                WeightsOf(root),
                BandsOf(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads and parses the rubric at <paramref name="path"/>, or null if absent.</summary>
    public static CoverageRubric? ReadFile(string path)
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

    private static RatingWeights WeightsOf(JsonElement root)
    {
        if (!root.TryGetProperty("weights", out var weights) || weights.ValueKind != JsonValueKind.Object)
        {
            return RatingWeights.Default;
        }

        var hotPaths = Number(weights, "hotPaths") ?? RatingWeights.Default.HotPaths;
        var edges = Number(weights, "edgeCases") ?? RatingWeights.Default.EdgeCases;
        var ui = Number(weights, "ui") ?? RatingWeights.Default.Ui;
        var candidate = new RatingWeights(hotPaths, edges, ui);
        return candidate.Total > 0 && hotPaths >= 0 && edges >= 0 && ui >= 0
            ? candidate
            : RatingWeights.Default;
    }

    private static BandThresholds BandsOf(JsonElement root)
    {
        if (!root.TryGetProperty("bands", out var bands) || bands.ValueKind != JsonValueKind.Object)
        {
            return BandThresholds.Default;
        }

        var strong = Score(bands, "strong") ?? BandThresholds.Default.Strong;
        var solid = Score(bands, "solid") ?? BandThresholds.Default.Solid;
        var thin = Score(bands, "thin") ?? BandThresholds.Default.Thin;

        // Keep the bands ordered and inside the scale even if the file is edited badly.
        if (thin < 0 || solid < thin || strong < solid || strong > 100)
        {
            return BandThresholds.Default;
        }

        return new BandThresholds(strong, solid, thin);
    }

    private static int? Score(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    private static IEnumerable<JsonElement> Array(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? Bool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static double? Rate(JsonElement? parent, string name) =>
        Number(parent, name) is { } number ? Math.Clamp(number, 0d, 1d) : null;

    private static double? Number(JsonElement? parent, string name) =>
        parent is { } element
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
        && !double.IsNaN(number)
        && !double.IsInfinity(number)
            ? number
            : null;

    private static JsonElement? TryGetObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
}

/// <summary>One checked input in a rated dimension.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Label">Human label.</param>
/// <param name="Met">True when the item clears its bar.</param>
/// <param name="Evidence">The measured reason, e.g. <c>98% line · 87% branch</c>.</param>
public sealed record RatingItem(string Id, string Label, bool Met, string Evidence);

/// <summary>One weighted dimension of the rating.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Label">Human label.</param>
/// <param name="Weight">Share of the composite, normalised so the dimensions add up to 100.</param>
/// <param name="Fraction">Achieved fraction in the range 0..1.</param>
/// <param name="Value">Short achievement label, e.g. <c>8/10</c>.</param>
/// <param name="Evidence">One-line measured explanation.</param>
/// <param name="Items">Every checked input in this dimension.</param>
public sealed record RatingDimension(
    string Id,
    string Label,
    int Weight,
    double Fraction,
    string Value,
    string Evidence,
    IReadOnlyList<RatingItem> Items)
{
    /// <summary>Achieved fraction as a whole percentage, for the meter.</summary>
    public int Percent => (int)Math.Round(Fraction * 100, MidpointRounding.AwayFromZero);
}

/// <summary>The explainable coverage rating shown on the status page.</summary>
/// <param name="Score">Composite score, 0..100.</param>
/// <param name="Band">strong / solid / thin / weak, or failing when the suite is red.</param>
/// <param name="Failing">True when the measured suite has failures.</param>
/// <param name="Summary">One-line why, naming every dimension.</param>
/// <param name="Dimensions">The weighted inputs.</param>
/// <param name="Gaps">Inputs that did not meet their bar in this measurement.</param>
/// <param name="KnownGaps">Standing gaps declared by the rubric.</param>
public sealed record CoverageRating(
    int Score,
    string Band,
    bool Failing,
    string Summary,
    IReadOnlyList<RatingDimension> Dimensions,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> KnownGaps)
{
    /// <summary>Band returned when the suite is red.</summary>
    public const string FailingBand = "failing";
}

/// <summary>Whether the committed measurement still describes the working tree.</summary>
/// <param name="Stale">True when the measured inputs no longer match the tree.</param>
/// <param name="Verified">True when freshness could be established and the inputs match.</param>
/// <param name="Note">Honest one-line explanation for the page.</param>
public sealed record CoverageStaleness(bool Stale, bool Verified, string Note);

/// <summary>
/// Turns a measured artifact and the committed rubric into an explainable rating. This is
/// the only place the judgement lives, so it stays pure and unit-testable.
/// </summary>
public static class CoverageRater
{
    /// <summary>Share of the edge dimension that comes from named scenarios.</summary>
    private const double NamedScenarioShare = 0.6;

    /// <summary>Score ceiling applied when the measured suite is red.</summary>
    private const int FailingCeiling = 25;

    /// <summary>
    /// Builds the rating, or returns null when there is nothing honest to rate: no
    /// artifact, no rubric, or no per-hot-path measurement (e.g. a schema-1 artifact).
    /// </summary>
    public static CoverageRating? Evaluate(CoverageSummary? coverage, CoverageRubric? rubric)
    {
        if (coverage is null || rubric is null || coverage.HotPaths.Count == 0 || rubric.HotPaths.Count == 0)
        {
            return null;
        }

        var gaps = new List<string>();
        var totalWeight = rubric.Weights.Total > 0 ? rubric.Weights.Total : 1d;
        var hot = EvaluateHotPaths(coverage, rubric, WeightOf(rubric.Weights.HotPaths, totalWeight), gaps);
        var edges = EvaluateEdgeCases(coverage, rubric, WeightOf(rubric.Weights.EdgeCases, totalWeight), gaps);
        var ui = EvaluateUi(coverage, rubric, WeightOf(rubric.Weights.Ui, totalWeight), gaps);

        var weighted =
            (hot.Fraction * rubric.Weights.HotPaths
                + edges.Fraction * rubric.Weights.EdgeCases
                + ui.Fraction * rubric.Weights.Ui) / totalWeight;

        var score = (int)Math.Round(weighted * 100, MidpointRounding.AwayFromZero);
        var failing = coverage.Failed > 0;
        if (failing)
        {
            score = Math.Min(score, FailingCeiling);
            gaps.Insert(0, $"{coverage.Failed} test{(coverage.Failed == 1 ? "" : "s")} failing — coverage claims are not trustworthy.");
        }

        var band = failing ? CoverageRating.FailingBand : Band(score, rubric.Bands);
        var summary =
            $"{hot.Value} hot paths · {edges.Value} edge scenarios · {ui.Value} free UI suites";

        return new CoverageRating(
            Math.Clamp(score, 0, 100),
            band,
            failing,
            summary,
            [hot,
             edges,
             ui],
            gaps,
            rubric.KnownGaps);
    }

    /// <summary>Maps a score to its band, using the rubric thresholds.</summary>
    public static string Band(int score, BandThresholds bands) =>
        score >= bands.Strong ? "strong"
        : score >= bands.Solid ? "solid"
        : score >= bands.Thin ? "thin"
        : "weak";

    /// <summary>
    /// Describes whether <paramref name="recorded"/> still matches the working tree. The
    /// fingerprint is authoritative; the head only supplies human context.
    /// </summary>
    public static CoverageStaleness DescribeStaleness(
        CoverageFingerprint? recorded,
        string? currentInputs,
        string? measuredHead,
        string? liveHead,
        IReadOnlyList<ChangeEntry> changes)
    {
        var measured = string.IsNullOrWhiteSpace(measuredHead) ? "an unrecorded commit" : measuredHead;
        var behind = MeasuredCommitsBehind(measuredHead, liveHead, changes);
        var context = behind switch
        {
            0 => "at HEAD",
            > 0 => $"{behind} commit{(behind == 1 ? "" : "s")} back",
            _ => null,
        };
        var location = behind is > 0
            ? $"measured at {measured}, {behind} commit{(behind == 1 ? "" : "s")} back"
            : $"measured at {measured}";

        if (recorded is null || string.IsNullOrWhiteSpace(recorded.Inputs) || string.IsNullOrWhiteSpace(currentInputs))
        {
            return new CoverageStaleness(
                false,
                false,
                $"Freshness unknown — {location}. Regenerate with scripts/status.ps1.");
        }

        if (recorded.Version != CoverageInputs.Version)
        {
            return new CoverageStaleness(
                false,
                false,
                $"Freshness unknown — the artifact uses fingerprint v{recorded.Version}, this page v{CoverageInputs.Version}. Regenerate with scripts/status.ps1.");
        }

        if (!string.Equals(recorded.Inputs, currentInputs, StringComparison.Ordinal))
        {
            return new CoverageStaleness(
                true,
                false,
                $"Sources changed since this measurement — {location}. Regenerate with scripts/status.ps1.");
        }

        return new CoverageStaleness(
            false,
            true,
            context switch
            {
                "at HEAD" => $"Measured at {measured}; matches the current sources.",
                null => $"Measured at {measured}; sources unchanged since.",
                _ => $"Measured at {measured} ({context}); sources unchanged since.",
            });
    }

    private static int? MeasuredCommitsBehind(string? measuredHead, string? liveHead, IReadOnlyList<ChangeEntry> changes)
    {
        if (string.IsNullOrWhiteSpace(measuredHead))
        {
            return null;
        }

        if (string.Equals(measuredHead, liveHead, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        for (var index = 0; index < changes.Count; index++)
        {
            if (string.Equals(changes[index].Hash, measuredHead, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private static RatingDimension EvaluateHotPaths(
        CoverageSummary coverage,
        CoverageRubric rubric,
        int weight,
        List<string> gaps)
    {
        var items = new List<RatingItem>();
        foreach (var rule in rubric.HotPaths)
        {
            var measured = coverage.HotPaths.FirstOrDefault(
                item => string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));

            var met = measured is not null
                && measured.LineRate is { } line && line >= rubric.LineThreshold
                && measured.BranchRate is { } branch && branch >= rubric.BranchThreshold;

            var evidence = measured switch
            {
                null => "not measured",
                _ when measured.LineRate is null && measured.BranchRate is null => "no matching class in the report",
                _ => Describe(measured, rubric, met),
            };

            items.Add(new RatingItem(rule.Id, rule.Label, met, evidence));
            if (!met)
            {
                gaps.Add($"{rule.Label}: {evidence}");
            }
        }

        var passed = items.Count(item => item.Met);
        var fraction = items.Count == 0 ? 0 : (double)passed / items.Count;
        return new RatingDimension(
            "hotPaths",
            "hot paths",
            weight,
            fraction,
            $"{passed}/{items.Count}",
            $"at ≥ {Percent(rubric.LineThreshold)} line and ≥ {Percent(rubric.BranchThreshold)} branch",
            items);
    }

    private static RatingDimension EvaluateEdgeCases(
        CoverageSummary coverage,
        CoverageRubric rubric,
        int weight,
        List<string> gaps)
    {
        var items = new List<RatingItem>();
        var passed = 0;
        foreach (var rule in rubric.EdgeCases)
        {
            var measured = coverage.EdgeCases.FirstOrDefault(
                item => string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));

            var met = measured?.Passed == true;
            var evidence = measured switch
            {
                null => $"test not found: {rule.Test}",
                { Passed: true } => "passing",
                _ => "failing",
            };

            items.Add(new RatingItem(rule.Id, rule.Label, met, evidence));
            if (met)
            {
                passed += 1;
            }
            else
            {
                gaps.Add($"{rule.Label}: {evidence}");
            }
        }

        var namedFraction = items.Count == 0 ? 0 : (double)passed / items.Count;

        // The branch proxy is only read for paths the rubric calls hot, so an extra
        // area measured by the script cannot quietly raise the edge dimension. Above
        // the target the proxy saturates: the dimension rewards clearing the bar, not
        // chasing branch counts in areas the rubric already scores elsewhere.
        var rubricIds = rubric.HotPaths
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var measuredPaths = coverage.HotPaths
            .Where(item => item.BranchRate is not null && rubricIds.Contains(item.Id))
            .ToArray();
        var branch = measuredPaths.Length == 0 ? (double?)null : measuredPaths.Average(item => item.BranchRate!.Value);
        var branchFraction = branch is null
            ? 0
            : Math.Clamp(branch.Value / Math.Max(rubric.EdgeBranchTarget, 0.01), 0, 1);

        if (branch is null)
        {
            gaps.Add("Branch coverage across hot paths: not measured.");
        }

        var fraction = NamedScenarioShare * namedFraction + (1 - NamedScenarioShare) * branchFraction;
        var branchEvidence = branch is null
            ? "branch not measured"
            : $"branch {Percent(branch.Value)} across {measuredPaths.Length}/{rubric.HotPaths.Count} hot paths (target {Percent(rubric.EdgeBranchTarget)})";

        return new RatingDimension(
            "edgeCases",
            "edge cases",
            weight,
            fraction,
            $"{passed}/{items.Count}",
            branchEvidence,
            items);
    }

    private static RatingDimension EvaluateUi(
        CoverageSummary coverage,
        CoverageRubric rubric,
        int weight,
        List<string> gaps)
    {
        var free = rubric.UiSuites.Where(suite => !suite.Paid).ToArray();
        var paid = rubric.UiSuites.Where(suite => suite.Paid).ToArray();
        var items = new List<RatingItem>();
        var passed = 0;

        if (coverage.Ui is null)
        {
            gaps.Add("Browser suites were not run in this measurement; the UI dimension is not covered.");
        }

        // Browser evidence records the commit it was recorded at. If that is not the
        // commit the tests were measured at, the suites describe different code and
        // must not be scored as current.
        var uiHead = coverage.Ui?.Head;
        var wrongCommit = !string.IsNullOrWhiteSpace(uiHead)
            && !string.IsNullOrWhiteSpace(coverage.Head)
            && !string.Equals(uiHead, coverage.Head, StringComparison.OrdinalIgnoreCase);

        if (wrongCommit)
        {
            gaps.Add($"Browser suites ran at {uiHead}, not the measured {coverage.Head}; the UI dimension is not counted.");
        }

        foreach (var rule in free)
        {
            var measured = coverage.Ui?.Suites.FirstOrDefault(
                item => string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));

            var met = !wrongCommit && measured?.Passed == true;
            var evidence = measured switch
            {
                null => "not run in this artifact",
                _ => DescribeSuite(measured, wrongCommit ? uiHead : null),
            };

            items.Add(new RatingItem(rule.Id, rule.Label, met, evidence));
            if (met)
            {
                passed += 1;
            }
            else if (!wrongCommit)
            {
                gaps.Add($"{rule.Label}: {evidence}");
            }
        }

        // Paid suites are listed so the page shows every declared suite with its state,
        // but they are never run by the status scripts and never earn points.
        foreach (var rule in paid)
        {
            items.Add(new RatingItem(
                rule.Id,
                rule.Label,
                false,
                $"not run — paid harness (scripts/ui/{rule.Script}), never counted"));
        }

        var fraction = free.Length == 0 ? 0 : (double)passed / free.Length;
        var recorded = coverage.Ui?.Generated is { } generated
            ? $" · recorded {generated.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture)}"
            : string.Empty;

        return new RatingDimension(
            "ui",
            "UI integration",
            weight,
            fraction,
            $"{passed}/{free.Length}",
            coverage.Ui is null ? "no free browser suite was run" : $"free browser suites in scripts/ui{recorded}",
            items);
    }

    private static string DescribeSuite(UiSuiteEvidence measured, string? recordedHead)
    {
        var detail = measured.Checks > 0 ? $" · {measured.Checks} checks" : string.Empty;
        var evidence = measured.Passed ? $"passed{detail}" : $"failed{detail}";
        return recordedHead is null ? evidence : $"{evidence} — recorded at {recordedHead}";
    }

    private static string Describe(HotPathCoverage measured, CoverageRubric rubric, bool met)
    {
        var line = measured.LineRate is { } lineRate ? Percent(lineRate) : "—";
        var branch = measured.BranchRate is { } branchRate ? Percent(branchRate) : "—";
        var evidence = $"{line} line · {branch} branch";
        return met
            ? evidence
            : $"{evidence} — below the {Percent(rubric.LineThreshold)}/{Percent(rubric.BranchThreshold)} bar";
    }

    private static int WeightOf(double weight, double total) =>
        (int)Math.Round(weight / total * 100, MidpointRounding.AwayFromZero);

    private static string Percent(double rate) =>
        ((int)Math.Round(rate * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";
}
