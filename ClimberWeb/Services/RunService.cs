using Microsoft.Extensions.Logging;

namespace ClimberWeb.Services;

/// <summary>
/// Immutable snapshot of a general OpenCode run, everything the main page renders.
/// </summary>
public sealed record RunState
{
    public RunStatus Status { get; init; } = RunStatus.Idle;

    /// <summary>
    /// The session this run belongs to, when it was started through one. The session
    /// coordinator keys its persistence by this id; a null means no session is bound.
    /// </summary>
    public string? SessionId { get; init; }

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

    /// <summary>True when the primary provider failed and the fallback awaits approval.</summary>
    public bool FallbackAvailable { get; init; }

    /// <summary>Explains the fallback offer, or null when none is available.</summary>
    public string? FallbackMessage { get; init; }

    /// <summary>The provider this run used, when known.</summary>
    public ProviderSelection? Provider { get; init; }

    /// <summary>The next provider awaiting approval, or null when no fallback is offered.</summary>
    public ProviderSelection? Fallback { get; init; }
}

/// <summary>Inputs for a single OpenCode run.</summary>
/// <param name="TargetFolder">Absolute path of the folder <c>opencode run</c> executes in.</param>
/// <param name="Prompt">Instruction handed to <c>opencode run</c>.</param>
/// <param name="Agent">Agent to run with, or null to use opencode's own default.</param>
/// <param name="Provider">The provider/model for this run, or null to let opencode decide.</param>
/// <param name="Fallbacks">Providers offered, one approved step at a time, after a failure.</param>
/// <param name="SessionId">The conversation to continue, or null to let opencode mint its own.</param>
public sealed record RunOptions(
    string TargetFolder,
    string Prompt,
    string? Agent = null,
    ProviderSelection? Provider = null,
    IReadOnlyList<ProviderSelection>? Fallbacks = null,
    string? SessionId = null)
{
    /// <summary>Provides a non-null view of <see cref="Fallbacks"/>.</summary>
    public IReadOnlyList<ProviderSelection> FallbackList => Fallbacks ?? Array.Empty<ProviderSelection>();
}

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

    /// <summary>
    /// Re-runs the last failed run on the next configured provider after the user approved
    /// the fallback. Throws when there is no failed run awaiting approval.
    /// </summary>
    Task ApproveFallbackAsync();

    /// <summary>
    /// Sends one listed option as data to the model. There is no allow, approve or execute
    /// route: an answer is never an app action. Returns <see cref="QuestionSubmitOutcome.Disconnected"/>
    /// when this run has no channel.
    /// </summary>
    Task<QuestionSubmitOutcome> AnswerQuestionAsync(string formId, string fieldKey, string value);

    /// <summary>Dismisses a question without answering it.</summary>
    Task<QuestionSubmitOutcome> DismissQuestionAsync(string formId);

    /// <summary>The read-only <c>check</c> action: asks the server what happened to a form.</summary>
    Task<QuestionSubmitOutcome> CheckQuestionAsync(string formId);
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
    private readonly IQuestionChannelFactory? _questions;
    private readonly object _gate = new();
    private readonly RunLogFeed _log = new();

    private RunMetrics _metrics = RunMetrics.Zero;
    private RunState _state = new();
    private CancellationTokenSource? _cts;
    private Task _runTask = Task.CompletedTask;
    private RunOptions? _lastOptions;
    private ProviderPricing? _activePricing;
    private IQuestionChannel? _channel;

    /// <summary>
    /// Builds the producer for a serve run from the run's one child. Injectable so a test can
    /// drive the serve branch of the run loop without spawning a process; the real one speaks
    /// the child's HTTP API.
    /// </summary>
    private readonly Func<ServeChild, RunOptions, ICommandRunner> _serveRunner;

    public RunService(
        ICommandRunner runner,
        ILogger<RunService>? logger = null,
        IQuestionChannelFactory? questions = null,
        Func<ServeChild, RunOptions, ICommandRunner>? serveRunner = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;
        _questions = questions;
        _serveRunner = serveRunner ?? ((child, options) => new ServeRunRunner(
            child.Client,
            new OpenCodeFormClient(child.Client),
            options.SessionId!,
            options.Prompt,
            options.Agent,
            options.Provider));
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
            Volatile.Write(ref _activePricing, options.Provider?.Pricing);
            _lastOptions = options;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _state = new RunState
            {
                Status = RunStatus.Running,
                StartedAt = DateTimeOffset.Now,
                Logs = Array.Empty<string>(),
                Provider = options.Provider,
                SessionId = options.SessionId,
            };
        }

        RaiseStateChanged();
        _runTask = Task.Run(() => RunLoopAsync(options, _cts.Token));
        return _runTask;
    }

    /// <inheritdoc />
    public Task ApproveFallbackAsync()
    {
        RunOptions options;
        lock (_gate)
        {
            if (_lastOptions is not { } last
                || !_state.FallbackAvailable
                || last.FallbackList.Count == 0)
            {
                throw new InvalidOperationException("There is no failed run awaiting fallback approval.");
            }

            options = last with
            {
                Provider = last.FallbackList[0],
                Fallbacks = last.FallbackList.Skip(1).ToArray(),
            };
        }

        return StartAsync(options);
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    /// <inheritdoc />
    public async Task<QuestionSubmitOutcome> AnswerQuestionAsync(string formId, string fieldKey, string value)
    {
        var channel = Volatile.Read(ref _channel);
        if (channel is null || string.IsNullOrEmpty(formId))
        {
            return QuestionSubmitOutcome.Disconnected;
        }

        try
        {
            return await channel.SubmitAsync(formId, fieldKey, value).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "Question answer failed.");
            return QuestionSubmitOutcome.Disconnected;
        }
    }

    /// <inheritdoc />
    public async Task<QuestionSubmitOutcome> DismissQuestionAsync(string formId)
    {
        var channel = Volatile.Read(ref _channel);
        if (channel is null || string.IsNullOrEmpty(formId))
        {
            return QuestionSubmitOutcome.Disconnected;
        }

        try
        {
            return await channel.CancelAsync(formId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "Question dismiss failed.");
            return QuestionSubmitOutcome.Disconnected;
        }
    }

    /// <inheritdoc />
    public async Task<QuestionSubmitOutcome> CheckQuestionAsync(string formId)
    {
        var channel = Volatile.Read(ref _channel);
        if (channel is null || string.IsNullOrEmpty(formId))
        {
            return QuestionSubmitOutcome.Disconnected;
        }

        try
        {
            return await channel.RecheckAsync(formId).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "Question check failed.");
            return QuestionSubmitOutcome.Disconnected;
        }
    }

    /// <summary>Awaits the current run, if any. Intended for tests and graceful shutdown.</summary>
    public Task WaitForCompletionAsync() => _runTask;

    private async Task RunLoopAsync(RunOptions options, CancellationToken cancellationToken)
    {
        IQuestionChannel? questions = null;
        ServeChild? server = null;
        try
        {
            // The run owns one serve child in serve mode; the producer and the question
            // channel both ride it, because a form is only visible to the server that made it.
            server = await TryStartServerAsync(options, cancellationToken).ConfigureAwait(false);

            Log($"$ {(server is null
                ? OpenCodeRun.CommandLine(options.Prompt, options.Agent, options.Provider?.Model, options.SessionId)
                : OpenCodeRun.ServeCommandLine(options.Prompt, options.SessionId))}");

            // The channel is optional and never changes the run's outcome: a failure to start
            // it is one line, and the run continues on the producer it already has.
            questions = await StartQuestionsAsync(options, server, cancellationToken).ConfigureAwait(false);

            var result = server is not null
                ? await _serveRunner(server, options)
                    .RunAsync(
                        OpenCodeRun.Request(options.Prompt, options.TargetFolder, options.Agent, options.Provider?.Model, options.SessionId),
                        OnOpenCodeLine,
                        cancellationToken)
                    .ConfigureAwait(false)
                : await _runner.RunAsync(
                    OpenCodeRun.Request(options.Prompt, options.TargetFolder, options.Agent, options.Provider?.Model, options.SessionId),
                    OnOpenCodeLine,
                    cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode == 127)
            {
                // A missing binary is not a provider failure; never bill a fallback for it.
                Fail("[opencode not found — is it installed and on PATH?]", "opencode not found — is it installed and on PATH?", options, offerFallback: false);
            }
            else if (result.ExitCode != 0)
            {
                if (server is not null)
                {
                    // The serve producer already wrote the server's own failure row; the band
                    // repeats those words instead of inventing a process exit code for a
                    // process this run never started.
                    var detail = string.IsNullOrWhiteSpace(result.StandardError)
                        ? "the run failed"
                        : result.StandardError.Trim();
                    Fail(null, detail, options, offerFallback: CanFallBack(options));
                }
                else
                {
                    Fail(
                        $"[opencode exited with code {result.ExitCode}]",
                        $"opencode exited with code {result.ExitCode}",
                        options,
                        offerFallback: CanFallBack(options));
                }
            }
            else
            {
                UpdateState(state => state with
                {
                    Status = RunStatus.Completed,
                    FinishedAt = DateTimeOffset.Now,
                    // The status word says "completed"; a message here would only
                    // print the same fact twice in the band.
                    Message = null,
                    FallbackAvailable = false,
                    FallbackMessage = null,
                    Fallback = null,
                });
                Log("===== Completed =====");
                LogSpend();
            }
        }
        catch (ServeInterruptedException exception)
        {
            // The server ended the turn itself. That is not a provider failure either, and it
            // is not the reader's stop, so the band says which one it was.
            Log($"[serve: {exception.Message}]");
            UpdateState(state => state with
            {
                Status = RunStatus.Failed,
                FinishedAt = DateTimeOffset.Now,
                Message = exception.Message,
                FallbackAvailable = false,
                FallbackMessage = null,
                Fallback = null,
            });
        }
        catch (OperationCanceledException)
        {
            UpdateState(state => state with
            {
                Status = RunStatus.Stopped,
                FinishedAt = DateTimeOffset.Now,
                // Same rule as completion: "stopped" is already the statement.
                Message = null,
                FallbackAvailable = false,
                FallbackMessage = null,
                Fallback = null,
            });
            Log("===== Stopped by user =====");
        }
        catch (ServeStartException exception)
        {
            // The serve transport failing is not a provider failing: one line, and no fallback
            // offer, because retrying the same transport on another provider would fail alike.
            Log($"[serve: {exception.Message}]");
            UpdateState(state => state with
            {
                Status = RunStatus.Failed,
                FinishedAt = DateTimeOffset.Now,
                Message = exception.Message,
                FallbackAvailable = false,
                FallbackMessage = null,
                Fallback = null,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OpenCode run failed.");
            Fail($"ERROR: {ex.Message}", ex.Message, options, offerFallback: CanFallBack(options));
        }
        finally
        {
            await DisposeQuestionsAsync(questions).ConfigureAwait(false);

            // Last, and after the channel: the channel borrows this child in serve mode.
            if (server is not null)
            {
                await server.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Starts the run's serve child when the transport is enabled and the run has a session.
    /// A child that will not start is one line and a null: the run falls back to the CLI
    /// producer, which is exactly what it used before this lane existed.
    /// </summary>
    private async Task<ServeChild?> TryStartServerAsync(RunOptions options, CancellationToken cancellationToken)
    {
        if (_questions is not { Enabled: true } || string.IsNullOrWhiteSpace(options.SessionId))
        {
            return null;
        }

        try
        {
            return await _questions.StartServerAsync(options.TargetFolder, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "Question server unavailable.");
            Log($"[questions unavailable: {exception.Message}]");
            return null;
        }
    }

    /// <summary>
    /// Starts a question channel for this run when one is configured and the run has a
    /// session. Any failure is one quiet line and a null: the run continues without questions.
    /// </summary>
    private async Task<IQuestionChannel?> StartQuestionsAsync(
        RunOptions options,
        ServeChild? server,
        CancellationToken cancellationToken)
    {
        if (_questions is not { Enabled: true } || string.IsNullOrWhiteSpace(options.SessionId))
        {
            return null;
        }

        // Serve mode means the run already owns its one child. A channel that cannot attach to
        // that child must not start a second one: forms are process-local to the server that
        // raised them, so a second child could never see this run's questions anyway.
        if (server is null)
        {
            return null;
        }

        IQuestionChannel? channel = null;
        try
        {
            channel = _questions.Create();
            if (channel is null)
            {
                return null;
            }

            channel.Changed += OnQuestionChanged;
            await channel.StartAsync(options.SessionId, options.TargetFolder, cancellationToken, server)
                .ConfigureAwait(false);
            Volatile.Write(ref _channel, channel);
            return channel;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "Question channel unavailable.");
            // The type, not the message: an HttpClient fault reads "… (127.0.0.1:4099)", and no
            // line in the transcript may carry the run's loopback address. The full exception is
            // in the host log for diagnosis.
            Log($"[questions unavailable: the question channel did not attach ({exception.GetType().Name})]");
            if (channel is not null)
            {
                channel.Changed -= OnQuestionChanged;
                await channel.DisposeAsync().ConfigureAwait(false);
            }

            return null;
        }
    }

    private async Task DisposeQuestionsAsync(IQuestionChannel? channel)
    {
        if (channel is null)
        {
            return;
        }

        Volatile.Write(ref _channel, null);
        channel.Changed -= OnQuestionChanged;
        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "Disposing the question channel failed.");
        }
    }

    /// <summary>One line per change; the formatter turns it into a question row and the grouper
    /// collapses repeated states for one form.</summary>
    private void OnQuestionChanged(QuestionState state) => Log(QuestionLine.Write(state));

    /// <summary>True when this run has a provider and at least one provider after it.</summary>
    private static bool CanFallBack(RunOptions options) =>
        options.Provider is not null && options.FallbackList.Count > 0;

    /// <summary>
    /// Logs <paramref name="logLine"/> (when it is not null) and records the run as failed. A
    /// failure on a provider with a configured fallback exposes the next provider, which only
    /// runs after approval.
    /// </summary>
    private void Fail(string? logLine, string message, RunOptions options, bool offerFallback)
    {
        var next = offerFallback ? options.FallbackList.FirstOrDefault() : null;
        var current = options.Provider?.DisplayName ?? "The run";
        if (logLine is not null)
        {
            Log(logLine);
        }
        UpdateState(state => state with
        {
            Status = RunStatus.Failed,
            FinishedAt = DateTimeOffset.Now,
            Message = next is null ? message : $"{message} Retry on {next.DisplayName}?",
            FallbackAvailable = next is not null,
            FallbackMessage = next is null ? null : $"{current} failed. Retry this run on {next.DisplayName}?",
            Fallback = next,
        });
    }

    private void LogSpend()
    {
        var metrics = State.Metrics;
        var provider = State.Provider;
        Log(SpendFormat.SpendLine(
            metrics.SpendUsd,
            metrics.EstimatedSpendUsd,
            provider?.SpendMode ?? SpendMode.None,
            provider?.DisplayName));
    }

    /// <summary>
    /// Handles one line of opencode's JSON stream: logs it verbatim (the renderer decides
    /// how to show it) and accumulates the cost reported by <c>step_finish</c> events.
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
