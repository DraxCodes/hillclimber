using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>The general single-shot opencode run and its shared spend accumulator.</summary>
public class RunServiceTests
{
    private const string StepFinish =
        """{"type":"step_finish","part":{"reason":"stop","cost":0.0125,"tokens":{"input":120,"output":30,"reasoning":0,"cache":{"read":400,"write":10}}}}""";

    private static RunOptions Options(string folder = @"C:\work\repo", string prompt = "explain the code")
        => new(folder, prompt);

    [Fact]
    public async Task Run_StreamsOutput_AccumulatesSpend_AndCompletes()
    {
        var runner = new FakeCommandRunner((_, _) => CannedResults.Raw($"hello\n{StepFinish}"));
        var service = new RunService(runner);

        await service.StartAsync(Options());

        var state = service.State;
        Assert.Equal(RunStatus.Completed, state.Status);
        Assert.Contains(state.Logs, line => line.Contains("opencode run \"explain the code\" --format json"));
        Assert.Contains(state.Logs, line => line.Contains("hello"));
        Assert.Equal(0.0125m, state.Metrics.SpendUsd);
        Assert.Equal(120, state.Metrics.InputTokens);
        Assert.Equal(30, state.Metrics.OutputTokens);
        Assert.Equal(400, state.Metrics.CacheReadTokens);
        Assert.Equal(10, state.Metrics.CacheWriteTokens);
        Assert.NotNull(state.StartedAt);
        Assert.NotNull(state.FinishedAt);
        Assert.False(service.IsRunning);

        // A plain run touches only opencode; no measurement or revert.
        Assert.Single(runner.RequestsFor("opencode"));
        Assert.Empty(runner.RequestsFor("dotnet"));
        Assert.Empty(runner.RequestsFor("git"));
    }

    [Fact]
    public async Task Run_PassesTheAgentToOpencode()
    {
        var runner = new FakeCommandRunner((_, _) => CannedResults.Raw(string.Empty));
        var service = new RunService(runner);

        await service.StartAsync(new RunOptions(@"C:\repo", "go", "task-orchestrator"));

        var request = Assert.Single(runner.RequestsFor("opencode"));
        Assert.Contains("--agent", request.Arguments);
        Assert.Contains("task-orchestrator", request.Arguments);
    }

    [Fact]
    public async Task Run_NonZeroExit_IsFailed_WithReason()
    {
        var runner = new FakeCommandRunner((_, _) => CannedResults.Raw("boom", exitCode: 1));
        var service = new RunService(runner);

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.Contains("exited with code 1", service.State.Message);
        Assert.Contains(service.State.Logs, line => line.Contains("opencode exited with code 1"));
    }

    [Fact]
    public async Task Run_MissingOpencode_IsFailed_WithNotFoundMessage()
    {
        var runner = new FakeCommandRunner((_, _) => CannedResults.Raw(string.Empty, 127));
        var service = new RunService(runner);

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.Contains("not found", service.State.Message);
        Assert.Contains(service.State.Logs, line => line.Contains("opencode not found"));
    }

    [Fact]
    public async Task Run_UnexpectedException_IsFailed()
    {
        var runner = new FakeCommandRunner((_, _) => throw new InvalidOperationException("boom"));
        var service = new RunService(runner);

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.Equal("boom", service.State.Message);
        Assert.Contains(service.State.Logs, line => line.Contains("ERROR: boom"));
    }

    [Fact]
    public async Task Stop_DuringRun_StopsCleanly()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeCommandRunner(async (_, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return CannedResults.Raw(string.Empty);
        });

        var service = new RunService(runner);
        var run = service.StartAsync(Options());

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsRunning);

        service.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RunStatus.Stopped, service.State.Status);
        Assert.Contains(service.State.Logs, line => line.Contains("Stopped by user"));
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task StartAsync_ThrowsWhileAlreadyRunning()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeCommandRunner(async (_, _, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return CannedResults.Raw(string.Empty);
        });

        var service = new RunService(runner);
        var run = service.StartAsync(Options());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(Options()));

        service.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartAsync_RejectsEmptyFolderOrPrompt(string value)
    {
        var service = new RunService(new FakeCommandRunner((_, _) => CannedResults.Raw(string.Empty)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(new RunOptions(value, "prompt")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(new RunOptions(@"C:\repo", value)));
    }

    [Fact]
    public async Task ExcessiveLogOutput_IsTrimmedToMaxLogLines()
    {
        var manyLines = string.Join("\n", Enumerable.Range(0, 5100).Select(i => $"line {i}"));
        var service = new RunService(new FakeCommandRunner((_, _) => CannedResults.Raw(manyLines)));

        await service.StartAsync(Options());

        Assert.Equal(5000, service.State.Logs.Count);
    }

    [Fact]
    public async Task StateChanged_ReportsRunningAndCompleted()
    {
        var service = new RunService(new FakeCommandRunner((_, _) => CannedResults.Raw(StepFinish)));
        var updates = new List<RunState>();
        service.StateChanged += state => { lock (updates) updates.Add(state); };

        await service.StartAsync(Options());

        List<RunState> snapshot;
        lock (updates)
        {
            snapshot = updates.ToList();
        }

        Assert.Contains(snapshot, s => s.Status == RunStatus.Running);
        Assert.Contains(snapshot, s => s.Status == RunStatus.Completed);
    }

    // ---- Surplus estimate + approved fallback --------------------------------

    private static SurplusPricing Pricing => new(0.000000038, 0.0000005, 0.0000000075, 0.000000038);

    [Fact]
    public async Task Run_OnSurplusFailure_OffersTheFallbackWithoutSwitching()
    {
        var runner = new FakeCommandRunner((_, _) => CannedResults.Raw("boom", exitCode: 1));
        var service = new RunService(runner);

        await service.StartAsync(new RunOptions(
            @"C:\repo", "go", "task-orchestrator", "surplus/deepseek-v4.1-flash-1017", AiProvider.Surplus, Pricing));

        var state = service.State;
        Assert.Equal(RunStatus.Failed, state.Status);
        Assert.True(state.FallbackAvailable);
        Assert.Contains("Retry on OpenRouter", state.Message);
        Assert.Single(runner.RequestsFor("opencode"));
        Assert.Contains("surplus/deepseek-v4.1-flash-1017", Assert.Single(runner.RequestsFor("opencode")).Arguments);
    }

    [Fact]
    public async Task Run_ApproveFallback_RetriesWithTheOpenRouterModel()
    {
        var opencodeCalls = 0;
        var runner = new FakeCommandRunner((request, _) => request.FileName == "opencode"
            ? opencodeCalls++ == 0 ? CannedResults.Raw("boom", exitCode: 1) : CannedResults.Raw(StepFinish)
            : CannedResults.Raw(string.Empty));
        var service = new RunService(runner);

        await service.StartAsync(new RunOptions(
            @"C:\repo", "go", "task-orchestrator", "surplus/deepseek-v4.1-flash-1017", AiProvider.Surplus, Pricing));

        await service.ApproveFallbackAsync("openrouter/deepseek/deepseek-v4.1-flash");

        var requests = runner.RequestsFor("opencode");
        Assert.Equal(2, requests.Count);
        Assert.Contains("openrouter/deepseek/deepseek-v4.1-flash", requests[1].Arguments);
        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.False(service.State.FallbackAvailable);
        Assert.Equal(0.0125m, service.State.Metrics.SpendUsd);
        Assert.Equal(0m, service.State.Metrics.EstimatedSpendUsd);
    }

    [Fact]
    public async Task Run_UnexpectedExceptionOnSurplus_OffersTheFallback()
    {
        var runner = new FakeCommandRunner((_, _) => throw new InvalidOperationException("boom"));
        var service = new RunService(runner);

        await service.StartAsync(new RunOptions(
            @"C:\repo", "go", "task-orchestrator", "surplus/deepseek-v4.1-flash-1017", AiProvider.Surplus, Pricing));

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.True(service.State.FallbackAvailable);
        Assert.Contains("Retry on OpenRouter", service.State.Message);
    }

    [Fact]
    public void RunMetrics_WithPricing_AddsAnEstimatedSpend()
    {
        var metrics = RunMetrics.Zero.Add(StepFinish, Pricing);

        Assert.Equal(0.0125m, metrics.SpendUsd);
        Assert.Equal(0.00002294m, metrics.EstimatedSpendUsd);
    }

    // ---- shared accumulator --------------------------------------------------

    [Fact]
    public void RunMetrics_Add_SumsStepFinishUsageAndIgnoresOtherLines()
    {
        var metrics = RunMetrics.Zero
            .Add(StepFinish)
            .Add("""{"type":"text","part":{"text":"no cost"}}""")
            .Add("not json")
            .Add(StepFinish);

        Assert.Equal(0.025m, metrics.SpendUsd);
        Assert.Equal(240, metrics.InputTokens);
        Assert.Equal(60, metrics.OutputTokens);
        Assert.Equal(800, metrics.CacheReadTokens);
        Assert.Equal(20, metrics.CacheWriteTokens);
    }

    [Fact]
    public void RunMetrics_Add_LeavesAnUnrelatedLineUnchanged()
    {
        var metrics = RunMetrics.Zero.Add("just a loop line");

        Assert.Equal(RunMetrics.Zero, metrics);
    }
}
