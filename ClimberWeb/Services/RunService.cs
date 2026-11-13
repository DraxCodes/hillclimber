using Microsoft.Extensions.Logging;

namespace ClimberWeb.Services;

/// <summary>Immutable snapshot of a general OpenCode run, everything the main page renders.</summary>
public sealed record RunState
{
    public RunStatus Status { get; init; } = RunStatus.Idle;

    /// <summary>Human-readable detail about the current status.</summary>
    public string? Message { get; init; }

    /// <summary>Live log feed, oldest first.</summary>
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();

    /// <summary>Cost and tokens summed from opencode's <c>step_finish</c> events.</summary>
    public RunMetrics Metrics { get; init; } = RunMetrics.Zero;

    /// <summary>When the run started, or null before the first run.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the run finished, or null while idle/running.</summary>
    public DateTimeOffset? FinishedAt { get; init; }
}

/// <summary>Inputs for a single OpenCode run.</summary>
/// <param name="TargetFolder">Absolute path of the folder <c>opencode run</c> executes in.</param>
/// <param name="Prompt">Instruction handed to <c>opencode run</c>.</param>
public sealed record RunOptions(string TargetFolder, string Prompt);

/// <summary>The general single-shot opencode run, as consumed by the UI.</summary>
public interface IRunService
{
    /// <summary>Latest snapshot; safe to read at any time.</summary>
    RunState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes. Handlers may be invoked on any thread.</summary>
    event Action<RunState>? StateChanged;

    bool IsRunning { get; }

    /// <summary>
    /// Starts a run. The returned task completes when the run finishes, stops or fails
    /// (it does not throw for expected outcomes).
    /// </summary>
    Task StartAsync(RunOptions options);

    /// <summary>Requests immediate cancellation of the running run.</summary>
    void Stop();
}

/// <summary>
/// Runs <c>opencode run &lt;prompt&gt; --format json</c> once in the target folder and
/// streams its output: the everyday workflow, with no measurement and no revert. The
/// hillclimb loop wraps the same opencode step and adds those.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly ICommandRunner _runner;
    private readonly ILogger<RunService>? _logger;
    private readonly object _gate = new();
    private readonly RunLogFeed _log = new();

    private RunMetrics _metrics = RunMetrics.Zero;
    private RunState _state = new();
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;

    public RunService(ICommandRunner runner, ILogger<RunService>? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;
    }

    public RunState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public event Action<RunState>? StateChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _state.Status == RunStatus.Running;
            }
        }
    }

    public Task StartAsync(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.TargetFolder))
        {
            throw new ArgumentException("A target folder is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Prompt))
        {
            throw new ArgumentException("A prompt is required.", nameof(options));
        }

        lock (_gate)
        {
            if (_state.Status == RunStatus.Running)
            {
                throw new InvalidOperationException("A run is already in progress. Stop it before starting another.");
            }

            _log.Clear();
            _metrics = RunMetrics.Zero;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _state = new RunState
            {
                Status = RunStatus.Running,
                StartedAt = DateTimeOffset.Now,
                Logs = Array.Empty<string>(),
            };
        }

        RaiseStateChanged();
        _runTask = Task.Run(() => RunLoopAsync(options, _cts.Token));
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

    private async Task RunLoopAsync(RunOptions options, CancellationToken cancellationToken)
    {
        try
        {
            Log($"$ {OpenCodeRun.CommandLine(options.Prompt)}");
            var result = await _runner.RunAsync(
                OpenCodeRun.Request(options.Prompt, options.TargetFolder),
                OnOpenCodeLine,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode == 127)
            {
                Fail("[opencode not found — is it installed and on PATH?]", "opencode not found — is it installed and on PATH?");
            }
            else if (result.ExitCode != 0)
            {
                Fail($"[opencode exited with code {result.ExitCode}]", $"opencode exited with code {result.ExitCode}");
            }
            else
            {
                var spend = State.Metrics.SpendUsd;
                UpdateState(state => state with
                {
                    Status = RunStatus.Completed,
                    FinishedAt = DateTimeOffset.Now,
                    Message = "Run complete.",
                });
                Log("===== Completed =====");
                Log($"Spend this run: ${spend:0.######}.");
            }
        }
        catch (OperationCanceledException)
        {
            UpdateState(state => state with
            {
                Status = RunStatus.Stopped,
                FinishedAt = DateTimeOffset.Now,
                Message = "Stopped by user.",
            });
            Log("===== Stopped by user =====");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCode run failed.");
            Fail($"ERROR: {ex.Message}", ex.Message);
        }
    }

    /// <summary>Logs <paramref name="logLine"/> and records the run as failed with <paramref name="message"/>.</summary>
    private void Fail(string logLine, string message)
    {
        Log(logLine);
        UpdateState(state => state with
        {
            Status = RunStatus.Failed,
            FinishedAt = DateTimeOffset.Now,
            Message = message,
        });
    }

    /// <summary>
    /// Handles one line of opencode's JSON stream: logs it verbatim (the renderer decides
    /// how to show it) and accumulates the cost reported by <c>step_finish</c> events.
    /// </summary>
    private void OnOpenCodeLine(string line)
    {
        Log(line);

        var updated = _metrics.Add(line);
        if (updated == _metrics)
        {
            return;
        }

        _metrics = updated;
        UpdateState(state => state with { Metrics = updated });
    }

    private void Log(string line)
    {
        string stamped;
        RunState snapshot;

        lock (_gate)
        {
            stamped = _log.Append(line);
            _state = _state with { Logs = _log.Snapshot() };
            snapshot = _state;
        }

        _logger?.LogInformation("{Line}", stamped);
        StateChanged?.Invoke(snapshot);
    }

    private void UpdateState(Func<RunState, RunState> mutate)
    {
        RunState snapshot;
        lock (_gate)
        {
            _state = mutate(_state);
            snapshot = _state;
        }

        StateChanged?.Invoke(snapshot);
    }

    private void RaiseStateChanged()
    {
        RunState snapshot;
        lock (_gate)
        {
            snapshot = _state;
        }

        StateChanged?.Invoke(snapshot);
    }
}
