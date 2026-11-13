using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class HillClimberServiceTests
{
    private static ClimberOptions Options(int max = 5, string folder = @"C:\work\repo", string prompt = "make tests pass")
        => new(folder, prompt, max);

    /// <summary>
    /// Builds a runner that answers opencode/git successfully and returns the supplied passing-test
    /// counts in order for each successive <c>dotnet test</c> invocation.
    /// </summary>
    private static FakeCommandRunner RunnerWithScores(params int[] scores)
    {
        var dotnetCalls = 0;
        return new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(),
            "git" => CannedResults.Git(),
            "dotnet" => CannedResults.Tests(scores[Math.Min(dotnetCalls++, scores.Length - 1)]),
            _ => CannedResults.Raw(string.Empty, 127),
        });
    }

    [Fact]
    public async Task ImprovingIterations_AreKept_AndRunCompletes()
    {
        var runner = RunnerWithScores(1, 2, 3);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 3));

        var state = service.State;
        Assert.Equal(RunStatus.Completed, state.Status);
        Assert.Equal(3, state.CurrentIteration);
        Assert.Equal(3, state.BestScore);
        Assert.Equal(3, state.LastPassed);
        Assert.Equal(0, state.LastFailed);
        Assert.Empty(runner.RequestsFor("git"));
        Assert.Equal(3, runner.RequestsFor("opencode").Count);
        Assert.Equal(3, runner.RequestsFor("dotnet").Count);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task WaitForCompletionAsync_ReturnsTheSameRunTask()
    {
        var runner = RunnerWithScores(1);
        var service = new HillClimberService(runner);

        var run = service.StartAsync(Options(max: 1));
        var awaited = service.WaitForCompletionAsync();

        Assert.Same(run, awaited);
        await awaited;
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task OpenCodeNonZeroExit_IsLogged_AndRunCompletes()
    {
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(exitCode: 1),
            "dotnet" => CannedResults.Tests(2),
            _ => CannedResults.Git(),
        });
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Contains(service.State.Logs, line => line.Contains("opencode exited with code 1"));
    }

    [Fact]
    public async Task DotnetNonZeroExit_IsLogged_AndRunCompletes()
    {
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(),
            "dotnet" => CannedResults.Tests(2, exitCode: 1),
            _ => CannedResults.Git(),
        });
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Contains(service.State.Logs, line => line.Contains("dotnet test exited with code 1"));
    }

    [Fact]
    public async Task UnexpectedException_MarksRunAsFailed()
    {
        var runner = new FakeCommandRunner((request, _) =>
            request.FileName == "opencode"
                ? throw new InvalidOperationException("boom")
                : CannedResults.Git());
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        var state = service.State;
        Assert.Equal(RunStatus.Failed, state.Status);
        Assert.NotNull(state.Message);
        Assert.Contains("boom", state.Message);
        Assert.Contains(state.Logs, line => line.Contains("ERROR: boom"));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task ExcessiveLogOutput_IsTrimmedToMaxLogLines()
    {
        var manyLines = string.Join("\n", Enumerable.Range(0, 5100).Select(i => $"line {i}"));
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.Raw(manyLines),
            "dotnet" => CannedResults.Tests(1),
            _ => CannedResults.Git(),
        });
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Equal(5000, service.State.Logs.Count);
    }

    [Fact]
    public async Task RegressingIteration_RevertsWorkingChanges()
    {
        var runner = RunnerWithScores(5, 3);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 2));

        var gitRequests = runner.RequestsFor("git");
        var revert = Assert.Single(gitRequests);
        Assert.Equal(new[] { "checkout", "--", "." }, revert.Arguments);
        Assert.Equal(@"C:\work\repo", revert.WorkingDirectory);

        var state = service.State;
        Assert.Equal(RunStatus.Completed, state.Status);
        // The regression must not lower the best score.
        Assert.Equal(5, state.BestScore);
        Assert.Equal(3, state.LastPassed);
    }

    [Fact]
    public async Task UnchangedScore_IsKept_WithoutReverting()
    {
        var runner = RunnerWithScores(4, 4, 4);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 3));

        Assert.Empty(runner.RequestsFor("git"));
        Assert.Equal(4, service.State.BestScore);
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task MissingSummary_IsTreatedAsZero_AndFirstIterationIsKept()
    {
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(),
            "dotnet" => CannedResults.Raw("Build FAILED.", exitCode: 1),
            _ => CannedResults.Git(),
        });
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Empty(runner.RequestsFor("git"));
        Assert.Equal(0, service.State.BestScore);
        Assert.Equal(0, service.State.LastPassed);
        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Contains(service.State.Logs, line => line.Contains("could not parse", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(service.State.Logs, line => line.Contains("New best score"));
    }

    [Fact]
    public async Task FirstIteration_MeasuringZero_IsNotLoggedAsNewBest()
    {
        var runner = RunnerWithScores(0);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Empty(runner.RequestsFor("git"));
        // A 0 passing count is equal to the carried-in best, so it takes the unchanged branch;
        // asserting the branch (not just BestScore == 0) distinguishes it from a new best.
        Assert.Contains(service.State.Logs, line => line.Contains("Score unchanged at 0"));
        Assert.DoesNotContain(service.State.Logs, line => line.Contains("New best score"));
    }

    [Fact]
    public async Task FirstIteration_BeatingZero_IsLoggedAsNewBest()
    {
        var runner = RunnerWithScores(3);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 1));

        Assert.Equal(3, service.State.BestScore);
        Assert.Contains(service.State.Logs, line => line.Contains("New best score: 3"));
    }

    [Fact]
    public async Task EqualIteration_IsNotLoggedAsNewBest()
    {
        var runner = RunnerWithScores(3, 3);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 2));

        var newBestLines = service.State.Logs.Count(line => line.Contains("New best score"));
        Assert.Equal(1, newBestLines);
        Assert.Equal(3, service.State.BestScore);
    }

    [Fact]
    public async Task Loop_StopsAtMaxIterations()
    {
        var runner = RunnerWithScores(2);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 2));

        Assert.Equal(2, runner.RequestsFor("opencode").Count);
        Assert.Equal(2, runner.RequestsFor("dotnet").Count);
        Assert.Equal(2, service.State.CurrentIteration);
    }

    [Fact]
    public async Task StopDuringIteration_StopsCleanly()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeCommandRunner(async (request, _, ct) =>
        {
            if (request.FileName == "opencode")
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return CannedResults.Tests(0);
        });

        var service = new HillClimberService(runner);
        var run = service.StartAsync(Options(max: 5));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsRunning);

        service.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunStatus.Stopped, service.State.Status);
        Assert.False(service.IsRunning);
        Assert.Contains(service.State.Logs, line => line.Contains("Stopped by user"));
    }

    [Fact]
    public async Task Stop_BeforeAnyIteration_Finishes_StopsCleanly()
    {
        var runner = RunnerWithScores(1, 2, 3);
        var service = new HillClimberService(runner);

        service.Stop(); // no-op when idle
        var run = service.StartAsync(Options(max: 3));
        service.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunStatus.Stopped, service.State.Status);
    }

    [Fact]
    public async Task StartAsync_ThrowsWhileAlreadyRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeCommandRunner(async (request, _, ct) =>
        {
            if (request.FileName == "opencode")
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return CannedResults.Tests(0);
        });

        var service = new HillClimberService(runner);
        var run = service.StartAsync(Options(max: 2));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(Options(max: 2)));

        service.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GitFailure_IsLogged_AndDoesNotAbortTheRun()
    {
        var dotnetCalls = 0;
        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.OpenCode(),
            "dotnet" => CannedResults.Tests(dotnetCalls++ == 0 ? 5 : 1),
            "git" => CannedResults.Raw("fatal: not a git repository", exitCode: 128),
            _ => CannedResults.Raw(string.Empty, 1),
        });
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 2));

        Assert.Single(runner.RequestsFor("git"));
        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Equal(5, service.State.BestScore);
    }

    [Fact]
    public async Task StateChanged_ReportsProgressAndLogs()
    {
        var runner = RunnerWithScores(1, 2);
        var service = new HillClimberService(runner);
        var updates = new List<ClimberState>();
        service.StateChanged += state => { lock (updates) updates.Add(state); };

        await service.StartAsync(Options(max: 2));

        List<ClimberState> snapshot;
        lock (updates)
        {
            snapshot = updates.ToList();
        }

        Assert.Contains(snapshot, s => s.Status == RunStatus.Running);
        Assert.Contains(snapshot, s => s.Status == RunStatus.Completed);
        Assert.Contains(snapshot, s => s.CurrentIteration == 2);
        Assert.Contains(service.State.Logs, line => line.Contains("Iteration 1/2"));
        Assert.Contains(service.State.Logs, line => line.Contains("Iteration 2/2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartAsync_RejectsEmptyFolderOrPrompt(string value)
    {
        var service = new HillClimberService(RunnerWithScores(1));

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(new ClimberOptions(value, "goal", 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(new ClimberOptions(@"C:\repo", value, 1)));
    }

    [Fact]
    public async Task MaxIterations_LowerThanOne_IsClampedToOne()
    {
        var runner = RunnerWithScores(1, 2, 3);
        var service = new HillClimberService(runner);

        await service.StartAsync(Options(max: 0));

        Assert.Single(runner.RequestsFor("opencode"));
        Assert.Equal(1, service.State.MaxIterations);
    }
}
