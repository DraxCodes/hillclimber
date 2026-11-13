using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>
/// Pass/fail counts parsed from a <c>dotnet test</c> run.
/// </summary>
public readonly record struct TestSummary(int Passed, int Failed)
{
    /// <summary>Total tests that produced a result.</summary>
    public int Total => Passed + Failed;
}

/// <summary>
/// Parses the summary line emitted by <c>dotnet test</c> (VSTest / Microsoft.Testing.Platform),
/// for example: <c>Passed!  - Failed: 1, Passed: 11, Skipped: 0, Total: 12</c>.
/// </summary>
public static partial class TestResultParser
{
    [GeneratedRegex(@"Failed:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex FailedRegex();

    [GeneratedRegex(@"Passed:\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PassedRegex();

    /// <summary>
    /// Attempts to extract the passing/failing counts from <paramref name="output"/>.
    /// Returns <c>null</c> when no recognizable summary is present (for example a build failure
    /// before any test ran).
    /// </summary>
    public static TestSummary? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var passedMatch = PassedRegex().Match(output);
        var failedMatch = FailedRegex().Match(output);

        if (!passedMatch.Success && !failedMatch.Success)
        {
            return null;
        }

        var passed = passedMatch.Success ? int.Parse(passedMatch.Groups[1].Value) : 0;
        var failed = failedMatch.Success ? int.Parse(failedMatch.Groups[1].Value) : 0;

        return new TestSummary(passed, failed);
    }
}
