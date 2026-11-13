using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class TestResultParserTests
{
    [Fact]
    public void ParsesStandardPassedSummary()
    {
        var output = "Passed!  - Failed:     1, Passed:    11, Skipped:     0, Total:    12, Duration: 1 s";

        var summary = TestResultParser.Parse(output);

        Assert.NotNull(summary);
        Assert.Equal(11, summary!.Value.Passed);
        Assert.Equal(1, summary.Value.Failed);
        Assert.Equal(12, summary.Value.Total);
    }

    [Fact]
    public void ParsesFailedSummary()
    {
        var output = "Failed!  - Failed:     3, Passed:     7, Skipped:     0, Total:    10";

        var summary = TestResultParser.Parse(output);

        Assert.NotNull(summary);
        Assert.Equal(7, summary!.Value.Passed);
        Assert.Equal(3, summary.Value.Failed);
    }

    [Fact]
    public void ParsesSummaryWhenOnlyFailedCountPresent()
    {
        var summary = TestResultParser.Parse("Failed: 2");

        Assert.NotNull(summary);
        Assert.Equal(0, summary!.Value.Passed);
        Assert.Equal(2, summary.Value.Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Build FAILED.\nNo test summary here.")]
    public void ReturnsNullWhenNoSummaryIsPresent(string? output)
    {
        Assert.Null(TestResultParser.Parse(output));
    }

    [Fact]
    public void ParsesAcrossCombinedStandardOutputAndError()
    {
        var combined = "some stderr text\nPassed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4";

        var summary = TestResultParser.Parse(combined);

        Assert.NotNull(summary);
        Assert.Equal(4, summary!.Value.Passed);
        Assert.Equal(0, summary.Value.Failed);
    }

    [Fact]
    public void SumsSummariesFromMultipleProjects()
    {
        var output = string.Join('\n',
            "Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5",
            "Failed!  - Failed:     2, Passed:     8, Skipped:     0, Total:    10");

        var summary = TestResultParser.Parse(output);

        Assert.NotNull(summary);
        Assert.Equal(13, summary!.Value.Passed);
        Assert.Equal(2, summary.Value.Failed);
        Assert.Equal(15, summary.Value.Total);
    }
}
