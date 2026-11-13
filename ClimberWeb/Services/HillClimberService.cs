using Microsoft.Extensions.Logging;

namespace ClimberWeb.Services;

/// <summary>Lifecycle state of a climb run.</summary>
public enum ClimberStatus
{
    /// <summary>No run has been started yet.</summary>
    Idle,

    /// <summary>The loop is actively running.</summary>
    Running,

    /// <summary>All requested iterations finished normally.</summary>
    Completed,

    /// <summary>The run was aborted by the user (cancellation).</summary>
    Stopped,

    /// <summary>The run terminated because of an unexpected error.</summary>
    Failed,
}

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

    public ClimberStatus Status { get; init; } = ClimberStatus.Idle;

    /// <summary>Human-readable detail about the current status.</summary>
    public string? Message { get; init; }

    /// <summary>Live log feed, oldest first.</summary>
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();
}

/// <summary>Inputs for a climb run.</summary>
/// <param name="TargetFolder">Absolute path of the git repository to hill-climb.</param>
/// <param name="GoalPrompt">Instruction handed to <c>opencode run</c> each iteration.</param>
/// <param name="MaxIterations">Maximum number of iterations (defaults to 5).</param>
public sealed record ClimberOptions(
    string TargetFolder,
    string GoalPrompt,
    int MaxIterations = 5);

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
}

/// <summary>
/// Runs <c>opencode run</c> followed by <c>dotnet test</c> for a fixed number of iterations,
/// keeping iterations that improve the passing-test count and reverting those that regress.
/// </summary>
public sealed class HillClimberService : IHillClimberService
{
    private const int MaxLogLines = 5000;

    private readonly ICommandRunner _runner;
    private readonly ILogger<HillClimberService>? _logger;
    private readonly object _gate = new();
    private readonly List<string> _logs = new();

    private ClimberState _state = new();
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;

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
                return _state.Status == ClimberStatus.Running;
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
            if (_state.Status == ClimberStatus.Running)
            {
                throw new InvalidOperationException("A climb is already running. Stop it before starting another.");
            }

            _logs.Clear();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _state = new ClimberState
            {
                Status = ClimberStatus.Running,
                MaxIterations = maxIterations,
                CurrentIteration = 0,
                BestScore = 0,
                Logs = Array.Empty<string>(),
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
        }
    }

    /// <summary>Awaits the current run, if any. Intended for tests and graceful shutdown.</summary>
    public Task WaitForCompletionAsync() => _runTask;

    private async Task RunLoopAsync(ClimberOptions options, int maxIterations, CancellationToken cancellationToken)
    {
        var best = -1;

        try
        {
            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                UpdateState(state => state with { CurrentIteration = iteration });
                Log($"===== Iteration {iteration}/{maxIterations} =====");

                // 1. Ask opencode to make a change toward the goal.
                Log($"$ opencode run \"{options.GoalPrompt}\" --format json");
                var opencode = await _runner.RunAsync(
                    new CommandRequest("opencode", new[] { "run", options.GoalPrompt, "--format", "json" }, options.TargetFolder),
                    Log,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (opencode.ExitCode != 0)
                {
                    Log($"[opencode exited with code {opencode.ExitCode}]");
                }

                // 2. Measure the result.
                Log("$ dotnet test --nologo");
                var testRun = await _runner.RunAsync(
                    new CommandRequest("dotnet", new[] { "test", "--nologo" }, options.TargetFolder),
                    Log,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (testRun.ExitCode != 0)
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

                if (best < 0 || passed > best)
                {
                    best = passed;
                    UpdateState(state => state with { BestScore = best });
                    Log($"New best score: {best}. Keeping changes.");
                }
                else if (passed < best)
                {
                    Log($"Regression: {passed} < {best}. Reverting with 'git checkout -- .'.");
                    await RevertAsync(options.TargetFolder, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    Log($"Score unchanged at {passed}. Keeping changes.");
                }
            }

            UpdateState(state => state with
            {
                Status = ClimberStatus.Completed,
                Message = $"Completed {maxIterations} iteration(s). Best score: {best}.",
            });
            Log($"===== Completed. Best score: {best} =====");
        }
        catch (OperationCanceledException)
        {
            UpdateState(state => state with
            {
                Status = ClimberStatus.Stopped,
                Message = "Stopped by user.",
            });
            Log("===== Stopped by user =====");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Hill climb failed.");
            Log($"ERROR: {ex.Message}");
            UpdateState(state => state with
            {
                Status = ClimberStatus.Failed,
                Message = ex.Message,
            });
        }
    }

    private async Task RevertAsync(string targetFolder, CancellationToken cancellationToken)
    {
        Log("$ git checkout -- .");
        var result = await _runner.RunAsync(
            new CommandRequest("git", new[] { "checkout", "--", "." }, targetFolder),
            Log,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            Log($"[git checkout exited with code {result.ExitCode}]");
        }
    }

    private static string Combine(CommandResult result) =>
        string.Concat(result.StandardOutput, "\n", result.StandardError);

    private void Log(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        ClimberState snapshot;

        lock (_gate)
        {
            _logs.Add(stamped);
            if (_logs.Count > MaxLogLines)
            {
                _logs.RemoveRange(0, _logs.Count - MaxLogLines);
            }

            _state = _state with { Logs = _logs.ToArray() };
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
