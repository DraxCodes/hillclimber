using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>
/// The only route from a rendered option to the world. It owns the wire (and, in production,
/// the child process), keeps the last state per form, and publishes every change as one
/// <see cref="QuestionState"/>. There is deliberately no allow/approve/execute member.
/// </summary>
public interface IQuestionChannel : IAsyncDisposable
{
    event Action<QuestionState>? Changed;

    /// <summary>Starts the transport and lists whatever is already pending for the session.</summary>
    Task StartAsync(string sessionId, string targetFolder, CancellationToken cancellationToken);

    Task<QuestionSubmitOutcome> SubmitAsync(string formId, string fieldKey, string value);

    Task<QuestionSubmitOutcome> CancelAsync(string formId);

    /// <summary>Asks the server what happened; the <c>check</c> action on a refused/unconfirmed row.</summary>
    Task<QuestionSubmitOutcome> RecheckAsync(string formId);
}

/// <summary>Reconnect policy for <see cref="QuestionChannel"/>. Defaults are the spec's numbers.</summary>
public sealed record QuestionChannelOptions
{
    public static QuestionChannelOptions Default { get; } = new();

    /// <summary>A stream silent this long reconnects without counting a failure.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Failures older than this no longer count toward <see cref="FailureLimit"/>.</summary>
    public TimeSpan FailureWindow { get; init; } = TimeSpan.FromSeconds(60);

    public int FailureLimit { get; init; } = 5;

    /// <summary>The message the model receives when the reader dismisses a question.</summary>
    public string CancelMessage { get; init; } = "no answer sent";
}

/// <summary>
/// Speaks one run's forms over <see cref="IFormWire"/>. It dispatches an allowlist of events
/// (<c>form.created</c>, <c>form.replied</c>, <c>form.cancelled</c>) and drops everything else,
/// including every <c>permission.*</c> event, before it can become a state or a line.
/// </summary>
public sealed class QuestionChannel : IQuestionChannel
{
    private readonly object _gate = new();
    private readonly Dictionary<string, QuestionState> _forms = new(StringComparer.Ordinal);
    private readonly List<DateTimeOffset> _failures = new();
    private readonly ServeSupervisor? _supervisor;
    private IFormWire? _wire;
    private readonly QuestionChannelOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;

    private string _sessionId = string.Empty;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private ServeChild? _child;
    private int _started;
    private int _disposed;

    public QuestionChannel(
        ServeSupervisor supervisor,
        QuestionChannelOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _options = options ?? QuestionChannelOptions.Default;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Tests and the dev fake: a wire with no process behind it.</summary>
    internal QuestionChannel(
        IFormWire wire,
        QuestionChannelOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<DateTimeOffset>? clock = null)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _options = options ?? QuestionChannelOptions.Default;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action<QuestionState>? Changed;

    /// <summary>The wire, once started. Null before <see cref="StartAsync"/>.</summary>
    private IFormWire Wire => _wire ?? throw new InvalidOperationException("The channel is not started.");

    public async Task StartAsync(string sessionId, string targetFolder, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The channel is already started.");
        }

        _sessionId = sessionId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (_wire is null)
        {
            _child = await _supervisor!.StartAsync(targetFolder, cancellationToken).ConfigureAwait(false);
            _wire = new OpenCodeFormClient(_child.Client);
        }

        // The first list is the restore path: pending forms become closed rows before any
        // frame is read, so a form created before the stream attached still appears.
        await ReconcileAsync(_lifetime.Token).ConfigureAwait(false);
        _loop = RunAsync(_lifetime.Token);
    }

    public async Task<QuestionSubmitOutcome> SubmitAsync(string formId, string fieldKey, string value)
    {
        if (!TryTracked(formId, out var state))
        {
            return QuestionSubmitOutcome.AlreadySettled;
        }

        if (state.Kind == QuestionKind.Unsupported)
        {
            return QuestionSubmitOutcome.InvalidAnswer;
        }

        if (state.Status != QuestionStatus.Pending)
        {
            return QuestionSubmitOutcome.AlreadySettled;
        }

        var key = string.IsNullOrEmpty(fieldKey) ? state.FieldKey : fieldKey;

        // Only a listed option can be sent: free text stays the composer's job, even when the
        // server marks the field custom.
        if (!state.Options.Any(option => string.Equals(option.Value, value, StringComparison.Ordinal)))
        {
            return QuestionSubmitOutcome.InvalidAnswer;
        }

        Publish(state with { Status = QuestionStatus.Submitting });

        var outcome = await Wire
            .ReplyAsync(_sessionId, state.FormId, key, state.Kind, value, LifetimeToken)
            .ConfigureAwait(false);

        switch (outcome)
        {
            case QuestionSubmitOutcome.Accepted:
                Publish(state with { Status = QuestionStatus.Answered, ChosenValue = value });
                break;
            case QuestionSubmitOutcome.AlreadySettled:
                await SettleFromWireAsync(state).ConfigureAwait(false);
                break;
            case QuestionSubmitOutcome.InvalidAnswer:
                Publish(state with { Status = QuestionStatus.Refused });
                break;
            case QuestionSubmitOutcome.Unconfirmed:
                Publish(state with { Status = QuestionStatus.Unconfirmed });
                break;
            default:
                Publish(state with { Status = QuestionStatus.Unavailable });
                break;
        }

        return outcome;
    }

    public async Task<QuestionSubmitOutcome> CancelAsync(string formId)
    {
        if (!TryTracked(formId, out var state))
        {
            return QuestionSubmitOutcome.AlreadySettled;
        }

        if (state.Status is QuestionStatus.Answered
            or QuestionStatus.Cancelled
            or QuestionStatus.Submitting)
        {
            return QuestionSubmitOutcome.AlreadySettled;
        }

        var outcome = await Wire
            .CancelAsync(_sessionId, state.FormId, _options.CancelMessage, LifetimeToken)
            .ConfigureAwait(false);

        // One retry only; a second failure keeps the pending row rather than dropping it.
        if (outcome is QuestionSubmitOutcome.Disconnected or QuestionSubmitOutcome.Unconfirmed)
        {
            outcome = await Wire
                .CancelAsync(_sessionId, state.FormId, _options.CancelMessage, LifetimeToken)
                .ConfigureAwait(false);
        }

        switch (outcome)
        {
            case QuestionSubmitOutcome.Accepted:
                Publish(state with { Status = QuestionStatus.Cancelled });
                break;
            case QuestionSubmitOutcome.AlreadySettled:
                await SettleFromWireAsync(state).ConfigureAwait(false);
                break;
            case QuestionSubmitOutcome.InvalidAnswer:
                Publish(state with { Status = QuestionStatus.Refused });
                break;
            default:
                Publish(state with { Status = state.Status });
                break;
        }

        return outcome;
    }

    public async Task<QuestionSubmitOutcome> RecheckAsync(string formId)
    {
        if (!TryTracked(formId, out var state))
        {
            return QuestionSubmitOutcome.Disconnected;
        }

        QuestionState? fresh;
        try
        {
            fresh = await Wire.GetAsync(_sessionId, state.FormId, LifetimeToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            Publish(state with { Status = QuestionStatus.Unavailable });
            return QuestionSubmitOutcome.Disconnected;
        }

        if (fresh is null)
        {
            // The server no longer knows the form: report it as settled rather than pending.
            Publish(state with { Status = QuestionStatus.Stale });
            return QuestionSubmitOutcome.AlreadySettled;
        }

        Publish(fresh);
        return fresh.Status == QuestionStatus.Pending
            ? QuestionSubmitOutcome.Unconfirmed
            : QuestionSubmitOutcome.Accepted;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime?.Cancel();

        if (_loop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch
            {
                // The loop ends by cancellation; nothing to report.
            }
        }

        if (_child is { } child)
        {
            await child.DisposeAsync().ConfigureAwait(false);
        }

        _lifetime?.Dispose();
    }

    // ------------------------------------------------------------------ the stream

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Reconcile on every connect: a missed frame is recoverable by listing.
                await ReconcileAsync(cancellationToken).ConfigureAwait(false);
                await WatchOnceAsync(cancellationToken).ConfigureAwait(false);

                attempt = 0;

                // The server closed the stream: reconnect after the shortest pause, silently.
                await _delay(_options.InitialBackoff, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                // The idle watchdog fired: a silent stream reconnects without counting a failure.
                attempt = 0;

                try
                {
                    await _delay(_options.InitialBackoff, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
            catch (Exception exception) when (IsTransportFailure(exception))
            {
                if (RecordFailure())
                {
                    PublishUnavailable();
                    return;
                }

                try
                {
                    await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                attempt = Math.Min(attempt + 1, 4);
            }
        }
    }

    private async Task WatchOnceAsync(CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timer = new Timer(
            _ => idle.Cancel(),
            null,
            _options.IdleTimeout,
            Timeout.InfiniteTimeSpan);

        void Touched() => timer.Change(_options.IdleTimeout, Timeout.InfiniteTimeSpan);

        await Wire.WatchAsync(
            frame =>
            {
                Touched();
                HandleFrame(frame);
                return Task.CompletedTask;
            },
            idle.Token).ConfigureAwait(false);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var listed = await Wire.ListAsync(_sessionId, cancellationToken).ConfigureAwait(false);

        foreach (var state in listed)
        {
            if (state.FormId.Length == 0 || !BelongsToThisSession(state.SessionId))
            {
                continue;
            }

            lock (_gate)
            {
                // A form already on screen keeps its state: a brief reconnect must not turn a
                // live question into a closed one. Only an unseen form is restored as stale.
                if (_forms.ContainsKey(state.FormId))
                {
                    continue;
                }
            }

            Publish(state.Kind == QuestionKind.Unsupported
                ? state with { Status = QuestionStatus.Unsupported }
                : state with { Status = QuestionStatus.Stale });
        }
    }

    private void HandleFrame(SseFrame frame)
    {
        if (!OpenCodeFormClient.TryParseEvent(frame.Data, out var evt))
        {
            return;
        }

        // The allowlist. Anything else — notably permission.asked and permission.replied —
        // is dropped here, before it can become a state, a line or a counter.
        switch (evt.Type)
        {
            case "form.created":
                OnCreated(evt.Data);
                break;
            case "form.replied":
                OnReplied(evt.Data);
                break;
            case "form.cancelled":
                OnCancelled(evt.Data);
                break;
        }
    }

    private void OnCreated(JsonElement data)
    {
        if (!data.TryGetProperty("form", out var form) || form.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var state = QuestionSupport.FromForm(form);
        if (state.FormId.Length == 0 || !BelongsToThisSession(state.SessionId))
        {
            return;
        }

        Publish(state);
    }

    private void OnReplied(JsonElement data)
    {
        if (ReadFormId(data) is not { } formId || !TryTracked(formId, out var state))
        {
            return;
        }

        var chosen = ReadAnswer(data, state.FieldKey);
        Publish(state with
        {
            Status = QuestionStatus.Answered,
            ChosenValue = chosen ?? state.ChosenValue,
        });
    }

    private void OnCancelled(JsonElement data)
    {
        if (ReadFormId(data) is not { } formId || !TryTracked(formId, out var state))
        {
            return;
        }

        Publish(state with { Status = QuestionStatus.Cancelled, ChosenValue = null });
    }

    // ------------------------------------------------------------------ decisions

    private async Task SettleFromWireAsync(QuestionState state)
    {
        QuestionState? fresh = null;
        try
        {
            fresh = await Wire.GetAsync(_sessionId, state.FormId, LifetimeToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransportFailure(exception))
        {
            // Fall through: a settled form is never left pending.
        }

        if (fresh is not null && fresh.Status is QuestionStatus.Answered or QuestionStatus.Cancelled)
        {
            Publish(fresh);
            return;
        }

        // 409 with no readable answer means the form was consumed elsewhere: it is not pending.
        Publish(state with { Status = QuestionStatus.Cancelled, ChosenValue = null });
    }

    private bool RecordFailure()
    {
        var now = _clock();
        lock (_gate)
        {
            _failures.RemoveAll(at => now - at > _options.FailureWindow);
            _failures.Add(now);
            return _failures.Count >= _options.FailureLimit;
        }
    }

    private void PublishUnavailable()
    {
        lock (_gate)
        {
            foreach (var state in _forms.Values.ToArray())
            {
                if (state.Status is QuestionStatus.Pending
                    or QuestionStatus.Submitting
                    or QuestionStatus.Unconfirmed
                    or QuestionStatus.Stale)
                {
                    PublishLocked(state with { Status = QuestionStatus.Unavailable });
                }
            }
        }
    }

    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(
            _options.InitialBackoff.TotalMilliseconds * Math.Pow(8, attempt),
            _options.MaxBackoff.TotalMilliseconds));

    // ------------------------------------------------------------------- helpers

    private CancellationToken LifetimeToken =>
        _lifetime?.Token ?? throw new InvalidOperationException("The channel is not started.");

    private bool TryTracked(string formId, out QuestionState state)
    {
        lock (_gate)
        {
            return _forms.TryGetValue(formId, out state!);
        }
    }

    private bool BelongsToThisSession(string sessionId) =>
        sessionId.Length == 0 || string.Equals(sessionId, _sessionId, StringComparison.Ordinal);

    private void Publish(QuestionState state)
    {
        lock (_gate)
        {
            PublishLocked(state);
        }
    }

    private void PublishLocked(QuestionState state)
    {
        if (state.FormId.Length > 0)
        {
            _forms[state.FormId] = state;
        }

        try
        {
            Changed?.Invoke(state);
        }
        catch
        {
            // A subscriber failed; the channel keeps its own state.
        }
    }

    private static string? ReadFormId(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    private static string? ReadAnswer(JsonElement data, string fieldKey)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("answer", out var answer)
            || answer.ValueKind != JsonValueKind.Object
            || !answer.TryGetProperty(fieldKey, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or IOException
            or JsonException
            or InvalidOperationException
            or ObjectDisposedException;
}