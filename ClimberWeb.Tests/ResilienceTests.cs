using System.Text.RegularExpressions;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>Failure-mode behaviour: a missing tool, a bad target, and the fixture shape.</summary>
public class ResilienceTests
{
    // ---- fixture shape -------------------------------------------------------

    [Fact]
    public void Fixture_IsAHalfFinishedDotNetTestProject()
    {
        var fixture = Path.Combine(RepoRoot(), "fixtures", "simple-climb");

        Assert.True(File.Exists(Path.Combine(fixture, "SimpleClimb.Tests.csproj")));
        Assert.True(File.Exists(Path.Combine(fixture, "Calculator.cs")));
        Assert.True(File.Exists(Path.Combine(fixture, "CalculatorTests.cs")));

        // Three members still throw, so the climb has something to fix.
        var source = File.ReadAllText(Path.Combine(fixture, "Calculator.cs"));
        Assert.Equal(3, Regex.Matches(source, "NotImplementedException").Count);
    }

    // ---- pre-flight ----------------------------------------------------------

    [Fact]
    public void Preflight_MissingFolder_CannotRun()
    {
        var preflight = new ClimbPreflight();

        var result = preflight.Check(Path.Combine(Path.GetTempPath(), $"climber-missing-{Guid.NewGuid():N}"));

        Assert.False(result.CanRun);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Preflight_NonGitFolder_WarnsButCanRun()
    {
        var dir = Directory.CreateTempSubdirectory("climber-preflight").FullName;
        try
        {
            var result = new ClimbPreflight().Check(dir);

            Assert.True(result.CanRun);
            Assert.Contains(result.Warnings, w => w.Contains("not a git repository", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- PATH lookup ---------------------------------------------------------

    [Fact]
    public void FindOnPath_ResolvesKnownToolAndRejectsUnknownOne()
    {
        Assert.NotNull(ProcessCommandRunner.FindOnPath("dotnet"));
        Assert.Null(ProcessCommandRunner.FindOnPath("definitely-not-a-real-tool-xyz"));
    }

    // ---- missing tools in the loop -------------------------------------------

    [Fact]
    public async Task Climb_MissingToolExitCode_IsLoggedClearly()
    {
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.Raw(string.Empty, 127),
            "dotnet" => CannedResults.Raw(string.Empty, 127),
            _ => CannedResults.Git(),
        });

        var service = new HillClimberService(runner);
        await service.StartAsync(new ClimberOptions(@"C:\work\repo", "goal", 1));

        Assert.Contains(service.State.Logs, line => line.Contains("opencode not found"));
        Assert.Contains(service.State.Logs, line => line.Contains("dotnet not found"));
        Assert.Equal(ClimberStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task Climb_MissingGitDuringRevert_IsReportedAsNotFound()
    {
        var dotnetCalls = 0;
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(),
            "dotnet" => CannedResults.Tests(dotnetCalls++ == 0 ? 5 : 1),
            "git" => CannedResults.Raw(string.Empty, 127),
            _ => CannedResults.Raw(string.Empty, 1),
        });

        var service = new HillClimberService(runner);
        await service.StartAsync(new ClimberOptions(@"C:\work\repo", "goal", 2));

        Assert.Contains(service.State.Logs, line => line.Contains("git not found"));
        Assert.Equal(ClimberStatus.Completed, service.State.Status);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HillClimberUI.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
