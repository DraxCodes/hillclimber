using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class CoverageRatingTests
{
    private const string RubricJson = """
        {
          "thresholds": { "line": 0.8, "branch": 0.6, "edgeBranchTarget": 0.75 },
          "weights": { "hotPaths": 40, "edgeCases": 30, "ui": 30 },
          "hotPaths": [
            { "id": "alpha", "label": "Alpha", "classes": ["A"] },
            { "id": "beta", "label": "Beta", "classes": ["B"] }
          ],
          "edgeCases": [
            { "id": "edge-one", "label": "Edge one", "test": "T.One" },
            { "id": "edge-two", "label": "Edge two", "test": "T.Two" }
          ],
          "uiSuites": [
            { "id": "ui-one", "label": "UI one", "script": "one.mjs", "paid": false },
            { "id": "ui-two", "label": "UI two", "script": "two.mjs", "paid": false },
            { "id": "paid", "label": "Paid harness", "script": "paid.mjs", "paid": true }
          ],
          "bands": { "strong": 85, "solid": 70, "thin": 55 },
          "knownGaps": ["No first-run suite."]
        }
        """;

    private static CoverageRubric Rubric() => CoverageRubric.Parse(RubricJson)!;

    private static CoverageSummary Coverage(
        int failed = 0,
        double? line = 0.9,
        double? branch = 0.8,
        IReadOnlyList<HotPathCoverage>? hotPaths = null,
        IReadOnlyList<EdgeCaseEvidence>? edges = null,
        UiEvidence? ui = null,
        string? head = "abc1234",
        CoverageFingerprint? fingerprint = null) =>
        new(
            DateTimeOffset.Parse("2026-11-16T08:00:00Z"),
            100 - failed,
            failed,
            0,
            line,
            branch,
            head,
            fingerprint ?? new CoverageFingerprint(CoverageInputs.Version, "fingerprint"),
            hotPaths ?? [new("alpha", 0.9, 0.8, 90, 20), new("beta", 0.95, 0.7, 50, 12)],
            edges ?? [new("edge-one", "T.One", true), new("edge-two", "T.Two", true)],
            ui ?? new UiEvidence(
                DateTimeOffset.Parse("2026-11-16T08:10:00Z"),
                "abc1234",
                [new("ui-one", true, 12), new("ui-two", true, 8)]));

    // ---- scoring -------------------------------------------------------------

    [Fact]
    public void Evaluate_AllInputsGreen_ScoresOneHundredAndStrong()
    {
        var rating = CoverageRater.Evaluate(Coverage(), Rubric());

        Assert.NotNull(rating);
        Assert.Equal(100, rating!.Score);
        Assert.Equal("strong", rating.Band);
        Assert.False(rating.Failing);
        Assert.Equal(3, rating.Dimensions.Count);
        Assert.Equal([40, 30, 30], rating.Dimensions.Select(dimension => dimension.Weight));
        var ui = Assert.Single(rating.Dimensions, dimension => dimension.Id == "ui");
        Assert.Contains(ui.Items, item => item.Id == "paid" && !item.Met);
        Assert.Equal(["No first-run suite."], rating.KnownGaps);
    }

    [Fact]
    public void Evaluate_HotPathBelowBar_IsGapAndLosesItsShare()
    {
        var coverage = Coverage(
            hotPaths: [new("alpha", 0.9, 0.8, 90, 20), new("beta", 0.7, 0.9, 50, 12)]);

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        Assert.Equal(80, rating!.Score);
        Assert.Equal("solid", rating.Band);
        var hot = Assert.Single(rating.Dimensions, dimension => dimension.Id == "hotPaths");
        Assert.Equal("1/2", hot.Value);
        Assert.Contains(rating.Gaps, gap => gap.StartsWith("Beta:", StringComparison.Ordinal));
        Assert.Contains(
            hot.Items,
            item => item.Id == "beta" && !item.Met && item.Evidence.Contains("below", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_HotPathWithNoMeasurement_IsGap()
    {
        var coverage = Coverage(hotPaths: [new("alpha", 0.9, 0.8, 90, 20)]);

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        Assert.Contains(rating!.Gaps, gap => gap.StartsWith("Beta:", StringComparison.Ordinal) && gap.Contains("not measured"));
    }

    [Fact]
    public void Evaluate_MissingEdgeTest_ReducesTheEdgeDimension()
    {
        var coverage = Coverage(edges: [new("edge-one", "T.One", true)]);

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        Assert.Equal(91, rating!.Score);
        var edge = Assert.Single(rating.Dimensions, dimension => dimension.Id == "edgeCases");
        Assert.Equal(70, edge.Percent);
        Assert.Contains(rating.Gaps, gap => gap.Contains("test not found: T.Two", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_BranchProxyBelowTarget_ReducesTheEdgeDimension()
    {
        var coverage = Coverage(
            hotPaths: [new("alpha", 0.9, 0.3, 90, 20), new("beta", 0.95, 0.45, 50, 12)]);

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        var edge = Assert.Single(rating!.Dimensions, dimension => dimension.Id == "edgeCases");
        // Named scenarios are all green (0.6); the branch proxy lands at 50%.
        Assert.Equal(80, edge.Percent);
    }

    [Fact]
    public void Evaluate_BranchProxy_IgnoresHotPathsTheRubricDoesNotList()
    {
        var coverage = Coverage(
            hotPaths:
            [
                new("alpha", 0.9, 0.2, 90, 20),
                new("beta", 0.95, 0.2, 50, 12),
                new("unlisted", 1.0, 1.0, 10, 4),
            ]);

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        var edge = Assert.Single(rating!.Dimensions, dimension => dimension.Id == "edgeCases");
        // Named scenarios all pass (0.6); the proxy still reads 20% of the 75% target.
        Assert.Equal(71, edge.Percent);
    }

    [Fact]
    public void Evaluate_UiNotRun_ZeroesTheUiDimensionAndNamesTheGap()
    {
        var coverage = Coverage() with { Ui = null };

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        Assert.Equal(70, rating!.Score);
        var ui = Assert.Single(rating.Dimensions, dimension => dimension.Id == "ui");
        Assert.Equal(0, ui.Percent);
        Assert.Equal("0/2", ui.Value);
        Assert.Contains(rating.Gaps, gap => gap.Contains("Browser suites were not run", StringComparison.Ordinal));
        Assert.Contains(rating.Gaps, gap => gap.Contains("not run in this artifact", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_PaidSuiteIsListedButNeverCounted()
    {
        var rating = CoverageRater.Evaluate(Coverage(), Rubric());

        Assert.NotNull(rating);
        var ui = Assert.Single(rating!.Dimensions, dimension => dimension.Id == "ui");
        Assert.Equal("2/2", ui.Value);
        Assert.Contains(
            ui.Items,
            item => item.Id == "paid"
                && !item.Met
                && item.Evidence.Contains("never counted", StringComparison.Ordinal));
        Assert.DoesNotContain(rating.Gaps, gap => gap.Contains("never counted", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_UiRecordedAtAnotherCommit_IsNotCounted()
    {
        var coverage = Coverage() with
        {
            Ui = new UiEvidence(
                DateTimeOffset.Parse("2026-11-16T08:10:00Z"),
                "old9999",
                [new("ui-one", true, 12), new("ui-two", true, 8)]),
        };

        var rating = CoverageRater.Evaluate(coverage, Rubric());

        Assert.NotNull(rating);
        var ui = Assert.Single(rating!.Dimensions, dimension => dimension.Id == "ui");
        Assert.Equal("0/2", ui.Value);
        Assert.Contains(rating.Gaps, gap => gap.Contains("ran at old9999", StringComparison.Ordinal));
        Assert.Contains(
            ui.Items,
            item => item.Id == "ui-one"
                && !item.Met
                && item.Evidence.Contains("recorded at old9999", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_UiRecordedAtTheSameCommit_StaysCounted()
    {
        var rating = CoverageRater.Evaluate(Coverage(), Rubric());

        Assert.NotNull(rating);
        var ui = Assert.Single(rating!.Dimensions, dimension => dimension.Id == "ui");
        Assert.Equal(100, ui.Percent);
        Assert.Contains(ui.Items, item => item.Id == "ui-one" && item.Evidence.Contains("12 checks", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RedSuite_CapsTheScoreAndBandsFailing()
    {
        var rating = CoverageRater.Evaluate(Coverage(failed: 1), Rubric());

        Assert.NotNull(rating);
        Assert.True(rating!.Failing);
        Assert.Equal(25, rating.Score);
        Assert.Equal("failing", rating.Band);
        Assert.Contains(rating.Gaps, gap => gap.Contains("1 test failing", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_WithoutAUsableRubric_ReturnsNull()
    {
        Assert.Null(CoverageRater.Evaluate(Coverage(), null));
        Assert.Null(CoverageRater.Evaluate(Coverage(), CoverageRubric.Parse("{ not json }")));
    }

    [Fact]
    public void Evaluate_SchemaOneArtifact_HasNothingToRate()
    {
        var coverage = CoverageArtifact.Parse("""{ "tests": { "passed": 10 }, "coverage": { "line": 0.8 } }""");

        Assert.Null(CoverageRater.Evaluate(coverage, Rubric()));
    }

    [Theory]
    [InlineData(100, "strong")]
    [InlineData(85, "strong")]
    [InlineData(84, "solid")]
    [InlineData(70, "solid")]
    [InlineData(69, "thin")]
    [InlineData(55, "thin")]
    [InlineData(54, "weak")]
    [InlineData(0, "weak")]
    public void Band_UsesTheRubricEdges(int score, string expected)
    {
        Assert.Equal(expected, CoverageRater.Band(score, BandThresholds.Default));
    }

    // ---- rubric parsing ------------------------------------------------------

    [Fact]
    public void Rubric_ParsesThresholdsWeightsBandsAndLists()
    {
        var rubric = Rubric();

        Assert.Equal(2, rubric.HotPaths.Count);
        Assert.Equal(2, rubric.EdgeCases.Count);
        Assert.Equal(3, rubric.UiSuites.Count);
        Assert.True(rubric.UiSuites.Single(suite => suite.Id == "paid").Paid);
        Assert.Equal(0.8, rubric.LineThreshold);
        Assert.Equal(0.6, rubric.BranchThreshold);
        Assert.Equal(0.75, rubric.EdgeBranchTarget);
        Assert.Equal(40, rubric.Weights.HotPaths);
        Assert.Equal(30, rubric.Weights.EdgeCases);
        Assert.Equal(30, rubric.Weights.Ui);
        Assert.Equal(85, rubric.Bands.Strong);
        Assert.Equal(70, rubric.Bands.Solid);
        Assert.Equal(55, rubric.Bands.Thin);
        Assert.Single(rubric.KnownGaps);
    }

    [Fact]
    public void Rubric_WithDefaultedThresholdsAndWeights_StaysUsable()
    {
        var rubric = CoverageRubric.Parse("""
            { "hotPaths": [ { "id": "a", "label": "A", "classes": ["A"] } ] }
            """);

        Assert.NotNull(rubric);
        Assert.Equal(0.8, rubric!.LineThreshold);
        Assert.Equal(0.6, rubric.BranchThreshold);
        Assert.Equal(RatingWeights.Default, rubric.Weights);
        Assert.Equal(BandThresholds.Default, rubric.Bands);
    }

    [Fact]
    public void Rubric_WithNoHotPaths_IsRejected()
    {
        Assert.Null(CoverageRubric.Parse("""{ "edgeCases": [] }"""));
        Assert.Null(CoverageRubric.Parse("""{ "hotPaths": [] }"""));
    }

    // ---- staleness -----------------------------------------------------------

    [Fact]
    public void Staleness_MatchingInputsAtHead_IsVerified()
    {
        var freshness = CoverageRater.DescribeStaleness(
            new CoverageFingerprint(CoverageInputs.Version, "same"), "same", "abc1234", "abc1234", []);

        Assert.False(freshness.Stale);
        Assert.True(freshness.Verified);
        Assert.Contains("matches the current sources", freshness.Note);
    }

    [Fact]
    public void Staleness_ChangedInputs_IsStaleAndNamesTheCommand()
    {
        var freshness = CoverageRater.DescribeStaleness(
            new CoverageFingerprint(CoverageInputs.Version, "old"), "new", "abc1234", "def5678", []);

        Assert.True(freshness.Stale);
        Assert.False(freshness.Verified);
        Assert.Contains("Sources changed", freshness.Note);
        Assert.Contains("scripts/status.ps1", freshness.Note);
    }

    [Fact]
    public void Staleness_VersionMismatch_IsUnknownRatherThanStale()
    {
        var freshness = CoverageRater.DescribeStaleness(
            new CoverageFingerprint(99, "same"), "same", "abc1234", "abc1234", []);

        Assert.False(freshness.Stale);
        Assert.False(freshness.Verified);
        Assert.Contains("Freshness unknown", freshness.Note);
    }

    [Fact]
    public void Staleness_MissingFingerprint_IsUnknown()
    {
        var freshness = CoverageRater.DescribeStaleness(null, "same", "abc1234", "abc1234", []);

        Assert.False(freshness.Verified);
        Assert.Contains("Freshness unknown", freshness.Note);
    }

    [Fact]
    public void Staleness_DocsOnlyCommit_StaysVerifiedAndCountsCommitsBack()
    {
        var changes = new[]
        {
            new ChangeEntry("ccc3333", DateTimeOffset.UtcNow, "docs: b", "B", true),
            new ChangeEntry("bbb2222", DateTimeOffset.UtcNow, "docs: a", "A", true),
            new ChangeEntry("abc1234", DateTimeOffset.UtcNow, "feat: measured", "Measured", true),
        };

        var freshness = CoverageRater.DescribeStaleness(
            new CoverageFingerprint(CoverageInputs.Version, "same"), "same", "abc1234", "ccc3333", changes);

        Assert.True(freshness.Verified);
        Assert.False(freshness.Stale);
        Assert.Contains("2 commits back", freshness.Note);
        Assert.Contains("sources unchanged", freshness.Note);
    }

    [Fact]
    public void Staleness_MeasuredHeadOutsideRecentHistory_StillVerifiesByFingerprint()
    {
        var freshness = CoverageRater.DescribeStaleness(
            new CoverageFingerprint(CoverageInputs.Version, "same"), "same", "old9999", "abc1234", []);

        Assert.True(freshness.Verified);
        Assert.Contains("Measured at old9999", freshness.Note);
    }

    // ---- artifact schema 2 ---------------------------------------------------

    [Fact]
    public void Artifact_FingerprintWithoutVersion_IsUnknownRatherThanTrusted()
    {
        var coverage = CoverageArtifact.Parse("""
            { "head": "abc1234", "fingerprint": { "inputs": "feed" }, "tests": { "passed": 1 } }
            """);

        Assert.NotNull(coverage);
        Assert.Null(coverage!.Fingerprint);

        var freshness = CoverageRater.DescribeStaleness(coverage.Fingerprint, "feed", "abc1234", "abc1234", []);

        Assert.False(freshness.Verified);
        Assert.False(freshness.Stale);
        Assert.Contains("Freshness unknown", freshness.Note);
    }

    [Fact]
    public void Artifact_ParsesSchemaTwo()
    {
        const string json = """
            {
              "schema": 2,
              "generated": "2026-11-16T08:00:00Z",
              "head": "abc1234",
              "fingerprint": { "version": 1, "inputs": "feed" },
              "tests": { "passed": 40, "failed": 1, "skipped": 2 },
              "coverage": { "line": 0.83, "branch": 0.61 },
              "hotPaths": [
                { "id": "alpha", "line": 0.9, "branch": 0.7, "lines": 100, "branches": 30 },
                { "id": "", "line": 0.1 }
              ],
              "edgeCases": [ { "id": "edge-one", "test": "T.One", "passed": true } ],
              "ui": {
                "generated": "2026-11-16T08:10:00Z",
                "head": "abc1234",
                "suites": [ { "id": "ui-one", "passed": true, "checks": 22 }, { "id": "" } ]
              }
            }
            """;

        var coverage = CoverageArtifact.Parse(json);

        Assert.NotNull(coverage);
        Assert.Equal(43, coverage!.Total);
        Assert.Equal(83, coverage.LinePercent);
        Assert.Equal(61, coverage.BranchPercent);
        Assert.Equal("abc1234", coverage.Head);
        Assert.NotNull(coverage.Fingerprint);
        Assert.Equal(1, coverage.Fingerprint!.Version);
        Assert.Equal("feed", coverage.Fingerprint.Inputs);
        var hot = Assert.Single(coverage.HotPaths);
        Assert.Equal("alpha", hot.Id);
        Assert.Equal(100, hot.Lines);
        Assert.Single(coverage.EdgeCases);
        Assert.True(coverage.Ui!.Ran);
        var suite = Assert.Single(coverage.Ui.Suites);
        Assert.Equal(22, suite.Checks);
    }

    [Fact]
    public void Artifact_SchemaOne_StillParsesWithoutRatingInputs()
    {
        const string json = """
            { "generated": "2026-11-13T09:00:00Z", "tests": { "passed": 77 }, "coverage": { "line": 0.83 } }
            """;

        var coverage = CoverageArtifact.Parse(json);

        Assert.NotNull(coverage);
        Assert.Null(coverage!.BranchRate);
        Assert.Null(coverage.Fingerprint);
        Assert.Null(coverage.Ui);
        Assert.Empty(coverage.HotPaths);
        Assert.Empty(coverage.EdgeCases);
        Assert.Null(coverage.Head);
    }

    [Fact]
    public void Artifact_NonArraySections_DegradeToEmpty()
    {
        var coverage = CoverageArtifact.Parse("""
            { "hotPaths": { "id": "alpha" }, "edgeCases": 4, "ui": { "suites": "nope" } }
            """);

        Assert.NotNull(coverage);
        Assert.Empty(coverage!.HotPaths);
        Assert.Empty(coverage.EdgeCases);
        Assert.Empty(coverage.Ui!.Suites);
    }
}
