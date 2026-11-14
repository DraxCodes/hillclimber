using Microsoft.Extensions.Logging;

namespace ClimberWeb.Services;

/// <summary>Immutable snapshot of everything the dashboard needs to render.</summary>
public sealed record ClimberState
{
    public int CurrentIteration { get; init; }

    public int MaxIterations { get; init; }

    /// <summary>Highest number of passing tests seen so far.</summary>
    public int BestScore { get; init; }

    /// <summary>Passing tests from the most recent iteration, if any has finished.</summary>
    public int? LastPassed { get; init; }

    /// <summary>Failing tests from the most recent iteration, if any has finished.</summary>
    public int? LastFailed { get; init; }

    public RunStatus Status { get; init; } = RunStatus.Idle;

    /// <summary>Human-readable detail about the current status.</summary>
    public string? Message { get; init; }

    /// <summary>Live log feed, oldest first.</summary>
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total spend in USD for this run, summed from opencode's own <c>step_finish</c>
    /// cost events. Runs are not persisted, so this covers the current/last run only.
    /// </summary>
    public decimal SpendUsd { get; init; }

    /// <summary>Prompt tokens consumed this run, from <c>step_finish</c> events.</summary>
    public long InputTokens { get; init; }

    /// <summary>Completion tokens produced this run.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Prompt-cache read tokens this run.</summary>
    public long CacheReadTokens { get; init; }

    /// <summary>Prompt-cache write tokens this run.</summary>
    public long CacheWriteTokens { get; init; }

    /// <summary>Token-based spend estimate for providers that report no cost.</summary>
    public decimal EstimatedSpendUsd { get; init; }

    /// <summary>True when the primary provider failed and the fallback awaits approval.</summary>
    public bool FallbackAvailable { get; init; }

    /// <summary>Explains the fallback offer, or null when none is available.</summary>
    public string? FallbackMessage { get; init; }

    /// <summary>The provider the current iteration is using, when known.</summary>
    public AiProvider? Provider { get; init; }
}

/// <summary>Inputs for a climb run.</summary>
/// <param name="TargetFolder">Absolute path of the git repository to hill-climb.</param>
/// <param name="GoalPrompt">Instruction handed to <c>opencode run</c> each iteration.</param>
/// <param name="MaxIterations">Maximum number of iterations (defaults to 5).</param>
/// <param name="Agent">Agent to run with, or null to use opencode's own default.</param>
/// <param name="Model">Explicit model id; null lets the target folder's config decide.</param>
/// <param name="Provider">The provider in use, when known.</param>
/// <param name="Pricing">Surplus pricing for a token-based estimate, or null for exact cost.</param>
public sealed record ClimberOptions(
    string TargetFolder,
    string GoalPrompt,
    int MaxIterations = 5,
    string? Agent = null,
    string? Model = null,
    AiProvider? Provider = null,
    SurplusPricing? Pricing = null);

/// <summary>The hill-climbing loop, as consumed by the UI.</summary>
public interface IHillClimberService
{
    /// <summary>Latest snapshot; safe to read at any time.</summary>
    ClimberState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes. Handlers may be invoked on any thread.</summary>
    event Action<ClimberState>? StateChanged;

    bool IsRunning { get; }

    /// <summary>
    /// Starts a climb. The returned task completes when the loop finishes, stops or fails
    /// (it does not throw for expected outcomes).
    /// </summary>
    Task StartAsync(ClimberOptions options);

    /// <summary>Requests immediate cancellation of the running loop.</summary>
    void Stop();

    /// <summary>
    /// Resumes an awaiting climb on <paramref name="model"/> after the user approved the
    /// OpenRouter fallback. Throws when the loop is not awaiting approval.
    /// </summary>
    void ApproveFallback(string model);
}

/// <summary>
/// Runs <c>opencode run</c> followed by <c>dotnet test</c> for a fixed number of iterations,
/// keeping iterations that improve the passing-test count and reverting those that regress.
/// </summary>
public sealed class HillClimberService : IHillClimberService
{
    private readonly ICommandRunner _runner;
    private readonly ILogger<HillClimberService>? _logger;
    private readonly object _gate = new();
    private readonly RunLogFeed _log = new();

    private RunMetrics _metrics = RunMetrics.Zero;
    private ClimberState _state = new();
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;
    private SurplusPricing? _activePricing;
    private TaskCompletionSource<string>? _approval;

    public HillClimberService(ICommandRunner runner, ILogger<HillClimberService>? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;
    }

    public ClimberState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<ClimberState>? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _state.Status is RunStatus.Running or RunStatus.AwaitingApproval;
            }
        }
    }

    public Task StartAsync(ClimberOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TargetFolder))
        {
            throw new ArgumentException("A target folder is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.GoalPrompt))
        {
            throw new ArgumentException("A goal prompt is required.", nameof(options));
        }

        var maxIterations = Math.Max(1, options.MaxIterations);

        lock (_gate)
        {
            if (_state.Status is RunStatus.Running or RunStatus.AwaitingApproval)
            {
                throw new InvalidOperationException("A climb is already running. Stop it before starting another.");
            }

            _log.Clear();
            _metrics = RunMetrics.Zero;
            Volatile.Write(ref _activePricing, options.Pricing);
            _approval = null;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _state = new ClimberState
            {
                Status = RunStatus.Running,
                MaxIterations = maxIterations,
                CurrentIteration = 0,
                BestScore = 0,
                Logs = Array.Empty<string>(),
                Provider = options.Provider,
            };
        }

        RaiseStateChanged();
        _runTask = Task.Run(() => RunLoopAsync(options, maxIterations, _cts.Token));
        return _runTask;
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _approval?.TrySetCanceled();
        }
    }

    /// <inheritdoc />
    public void ApproveFallback(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        TaskCompletionSource<string>? approval;
        lock (_gate)
        {
            approval = _approval;
            if (approval is null || _state.Status != RunStatus.AwaitingApproval)
            {
                throw new InvalidOperationException("The climb is not awaiting fallback approval.");
            }
        }

        if (!approval.TrySetResult(model))
        {
            throw new InvalidOperationException("The climb is not awaiting fallback approval.");
        }
    }

    /// <summary>Awaits the current run, if any. Intended for tests and graceful shutdown.</summary>
    public Task WaitForCompletionAsync() => _runTask;

    private async Task RunLoopAsync(ClimberOptions options, int maxIterations, CancellationToken cancellationToken)
    {
        try
        {
            // Once the user approves a fallback, every remaining iteration stays on OpenRouter
            // rather than repeatedly failing against Surplus.
            var model = options.Model;
            var provider = options.Provider;
            Volatile.Write(ref _activePricing, options.Pricing);

            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                UpdateState(state => state with { CurrentIteration = iteration });
                Log($"===== Iteration {iteration}/{maxIterations} =====");

                // 1. Ask opencode to make a change toward the goal.

                while (true)
                {
                    Log($"$ {OpenCodeRun.CommandLine(options.GoalPrompt, options.Agent, model)}");
                    CommandResult opencode;
                    try
                    {
                        opencode = await _runner.RunAsync(
                            OpenCodeRun.Request(options.GoalPrompt, options.TargetFolder, options.Agent, model),
                            OnOpenCodeLine,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (provider == AiProvider.Surplus)
                    {
                        // A thrown provider call is a Surplus failure too; ask before spending.
                        Log($"[opencode failed: {ex.Message}]");
                        model = await RequestFallbackAsync(cancellationToken).ConfigureAwait(false);
                        provider = AiProvider.OpenRouter;
                        Volatile.Write(ref _activePricing, null);
                        UpdateState(state => state with
                        {
                            Status = RunStatus.Running,
                            FallbackAvailable = false,
                            FallbackMessage = null,
                            Message = "Surplus failed; continuing on OpenRouter.",
                            Provider = AiProvider.OpenRouter,
                        });
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (opencode.ExitCode == 127)
                    {
                        Log("[opencode not found — is it installed and on PATH?]");
                        break;
                    }

                    if (opencode.ExitCode == 0)
                    {
                        break;
                    }

                    Log($"[opencode exited with code {opencode.ExitCode}]");
                    if (provider != AiProvider.Surplus)
                    {
                        break;
                    }

                    // The primary provider failed: pause until the user approves the fallback.
                    model = await RequestFallbackAsync(cancellationToken).ConfigureAwait(false);
                    provider = AiProvider.OpenRouter;
                    Volatile.Write(ref _activePricing, null);
                    UpdateState(state => state with
                    {
                        Status = RunStatus.Running,
                        FallbackAvailable = false,
                        FallbackMessage = null,
                        Message = "Surplus failed; continuing on OpenRouter.",
                        Provider = AiProvider.OpenRouter,
                    });
                }

                // 2. Measure the result.
                Log("$ dotnet test --nologo");
                var testRun = await _runner.RunAsync(
                    new CommandRequest("dotnet", new[] { "test", "--nologo" }, options.TargetFolder),
                    Log,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (testRun.ExitCode == 127)
                {
                    Log("[dotnet not found — is it installed and on PATH?]");
                }
                else if (testRun.ExitCode != 0)
                {
                    Log($"[dotnet test exited with code {testRun.ExitCode}]");
                }

                var parsed = TestResultParser.Parse(Combine(testRun));
                if (parsed is null)
                {
                    Log("[could not parse a test summary; assuming 0 passing]");
                }

                var summary = parsed ?? new TestSummary(0, 0);
                var passed = summary.Passed;
                var failed = summary.Failed;
                Log($"Result: {passed} passed, {failed} failed.");
                UpdateState(state => state with { LastPassed = passed, LastFailed = failed });

                // The run's BestScore starts at 0 (see StartAsync), so the first iteration is
                // reported as a new best only when it actually beats what we carried in. Snapshot
                // it once so the comparison and both messages agree without re-reading state.
                var bestSoFar = State.BestScore;
                if (passed > bestSoFar)
                {
                    UpdateState(state => state with { BestScore = passed });
                    Log($"New best score: {passed}. Keeping changes.");
                }
                else if (passed < bestSoFar)
                {
                    Log($"Regression: {passed} < {bestSoFar}. Reverting with 'git checkout -- .'.");
                    await RevertAsync(options.TargetFolder, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Log($"Score unchanged at {passed}. Keeping changes.");
                }
            }

            var finalBest = State.BestScore;
            UpdateState(state => state with
            {
                Status = RunStatus.Completed,
                Message = $"Completed {maxIterations} iteration(s). Best score: {finalBest}.",
                FallbackAvailable = false,
                FallbackMessage = null,
            });
            Log($"===== Completed. Best score: {finalBest} =====");
            LogSpend();
        }
        catch (OperationCanceledException)
        {
            UpdateState(state => state with
            {
                Status = RunStatus.Stopped,
                Message = "Stopped by user.",
                FallbackAvailable = false,
                FallbackMessage = null,
            });
            Log("===== Stopped by user =====");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Hill climb failed.");
            Log($"ERROR: {ex.Message}");
            UpdateState(state => state with
            {
                Status = RunStatus.Failed,
                Message = ex.Message,
            });
        }
    }

    /// <summary>
    /// Pauses the loop and waits for the user to approve the OpenRouter fallback. Returns the
    /// approved model; cancellation (Stop) propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<string> RequestFallbackAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<string> approval;
        lock (_gate)
        {
            approval = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _approval = approval;
        }

        Log("----- Surplus failed. Retry this iteration on OpenRouter, or stop. -----");
        UpdateState(state => state with
        {
            Status = RunStatus.AwaitingApproval,
            FallbackAvailable = true,
            FallbackMessage = "Surplus failed. Retry this iteration on OpenRouter?",
            Message = "Waiting for approval to use OpenRouter.",
        });

        string model;
        try
        {
            model = await approval.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_approval, approval))
                {
                    _approval = null;
                }
            }
        }

        Log($"[approved: retrying with OpenRouter model {model}]");
        return model;
    }

    private void LogSpend() =>
        Log(SpendFormat.SpendLine(State.SpendUsd, State.EstimatedSpendUsd, State.Provider));

    private async Task RevertAsync(string targetFolder, CancellationToken cancellationToken)
    {
        Log("$ git checkout -- .");
        var result = await _runner.RunAsync(
            new CommandRequest("git", new[] { "checkout", "--", "." }, targetFolder),
            Log,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == 127)
        {
            Log("[git not found — is it installed and on PATH?]");
        }
        else if (result.ExitCode != 0)
        {
            Log($"[git checkout exited with code {result.ExitCode}]");
        }
    }

    private static string Combine(CommandResult result) =>
        string.Concat(result.StandardOutput, "\n", result.StandardError);

    /// <summary>
    /// Handles one line of opencode's JSON stream: logs it verbatim (the renderer decides
    /// how to show it) and accumulates the cost reported by <c>step_finish</c> events so
    /// "this climb's spend" is exact without persisting anything.
    /// </summary>
    private void OnOpenCodeLine(string line)
    {
        Log(line);

        var updated = _metrics.Add(line, Volatile.Read(ref _activePricing));
        if (updated == _metrics)
        {
            return;
        }

        _metrics = updated;
        UpdateState(state => state with
        {
            SpendUsd = updated.SpendUsd,
            InputTokens = updated.InputTokens,
            OutputTokens = updated.OutputTokens,
            CacheReadTokens = updated.CacheReadTokens,
            CacheWriteTokens = updated.CacheWriteTokens,
            EstimatedSpendUsd = updated.EstimatedSpendUsd,
        });
    }

    private void Log(string line)
    {
        string stamped;
        ClimberState snapshot;

        lock (_gate)
        {
            stamped = _log.Append(line);
            _state = _state with { Logs = _log.Snapshot() };
            snapshot = _state;
        }

        _logger?.LogInformation("{Line}", stamped);
        StateChanged?.Invoke(snapshot);
    }

    private void UpdateState(Func<ClimberState, ClimberState> mutate)
    {
        ClimberState snapshot;
        lock (_gate)
        {
            _state = mutate(_state);
            snapshot = _state;
        }

        StateChanged?.Invoke(snapshot);
    }

    private void RaiseStateChanged()
    {
        ClimberState snapshot;
        lock (_gate)
        {
            snapshot = _state;
        }

        StateChanged?.Invoke(snapshot);
    }
}
