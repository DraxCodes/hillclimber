using System.Net;
using System.Text;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>
/// Runs one turn through the run's <c>opencode serve</c> child instead of spawning
/// <c>opencode run</c>, and writes the events back out as the same JSON lines that command would
/// have printed. It is the second implementation of the run seam
/// (<see cref="ICommandRunner"/>): the call site, the log feed, the metrics and the renderer are
/// unchanged, which is what makes the producer migration one revert.
/// </summary>
/// <remarks>
/// The inputs arrive at construction. <see cref="ICommandRunner.RunAsync"/> exists only so the
/// type can stand in the run seam; <see cref="RunAsync(Action{string}?, CancellationToken)"/> is
/// the honest call, and the request overload simply ignores the <see cref="CommandRequest"/> that
/// would describe a process this runner must not start. The stream is attached **before** the
/// prompt is admitted, because a turn's opening event must never be missed; the mapper arms
/// itself on <c>session.execution.started</c>, so a replayed terminal event from an earlier turn
/// cannot end this one.
/// </remarks>
public sealed class ServeRunRunner : ICommandRunner
{
    /// <summary>Exit code for a turn that ended without a terminal event.</summary>
    public const int StreamClosedExitCode = 1;

    private static readonly TimeSpan DefaultStreamOpenTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InterruptGrace = TimeSpan.FromSeconds(2);

    private readonly HttpClient _client;
    private readonly IFormWire _wire;
    private readonly string _sessionId;
    private readonly string _prompt;
    private readonly string? _agent;
    private readonly ProviderSelection? _provider;
    private readonly TimeSpan _streamOpenTimeout;

    public ServeRunRunner(
        HttpClient client,
        IFormWire wire,
        string sessionId,
        string prompt,
        string? agent = null,
        ProviderSelection? provider = null,
        TimeSpan? streamOpenTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        _sessionId = sessionId;
        _prompt = prompt;
        _agent = agent;
        _provider = provider;
        _streamOpenTimeout = streamOpenTimeout ?? DefaultStreamOpenTimeout;
    }

    /// <summary>The turn's outcome, for tests and for a quiet log line.</summary>
    public ServeTurnEnd End { get; private set; } = ServeTurnEnd.Running;

    /// <summary>The server's own failure message when <see cref="End"/> is failed.</summary>
    public string? Failure { get; private set; }

    /// <summary>
    /// Why a served turn was interrupted, when <see cref="End"/> is
    /// <see cref="ServeTurnEnd.Interrupted"/>: <c>user</c>, <c>shutdown</c>, <c>superseded</c> or
    /// <c>inactivity</c>.
    /// </summary>
    public string? InterruptReason { get; private set; }

    /// <inheritdoc />
    public Task<CommandResult> RunAsync(
        CommandRequest request,
        Action<string>? onOutputLine,
        CancellationToken cancellationToken)
    {
        _ = request;
        return RunAsync(onOutputLine, cancellationToken);
    }

    /// <summary>Runs the turn, mapping every event onto the CLI line it replaces.</summary>
    public async Task<CommandResult> RunAsync(Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        var emit = onOutputLine ?? (_ => { });

        var mapper = new ServeEventMapper(_sessionId);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = new TaskCompletionSource<ServeTurnEnd>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reason = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watch = Task.Run(async () =>
        {
            try
            {
                await _wire.WatchAsync(
                    frame =>
                    {
                        if (!OpenCodeFormClient.TryParseEvent(frame.Data, out var value))
                        {
                            return Task.CompletedTask;
                        }

                        foreach (var line in mapper.Map(value))
                        {
                            emit(line);
                        }

                        if (mapper.End != ServeTurnEnd.Running)
                        {
                            reason.TrySetResult(mapper.Failure);
                            terminal.TrySetResult(mapper.End);
                        }

                        return Task.CompletedTask;
                    },
                    linked.Token,
                    () => opened.TrySetResult()).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Our own stop; the caller decides what that means.
            }
            catch (Exception exception)
            {
                opened.TrySetException(exception);
                terminal.TrySetException(exception);
            }
            finally
            {
                // A stream that ends without emptying the terminal is a closed server, not a
                // finished turn: keep that distinct so the caller can say so.
                if (!terminal.Task.IsCompleted)
                {
                    reason.TrySetResult(null);
                    terminal.TrySetResult(ServeTurnEnd.Running);
                }
            }
        }, CancellationToken.None);

        try
        {
            await WaitForStreamAsync(opened).ConfigureAwait(false);
            await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            await PromptAsync(cancellationToken).ConfigureAwait(false);

            var end = await terminal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            End = end;
            Failure = reason.Task.IsCompletedSuccessfully ? reason.Task.Result : null;
            InterruptReason = mapper.InterruptReason;

            if (end == ServeTurnEnd.Interrupted)
            {
                // The server can end a turn for reasons the reader did not cause (shutdown,
                // superseded, inactivity) or because somebody asked it to. Only "user" is a
                // stop a reader pressed; calling the rest "Stopped by user" would be a lie.
                throw InterruptReason is null or "user"
                    ? new OperationCanceledException("The turn was interrupted.")
                    : new ServeInterruptedException(InterruptReason);
            }

            return end switch
            {
                ServeTurnEnd.Succeeded => new CommandResult(0, string.Empty, string.Empty),
                ServeTurnEnd.Failed => new CommandResult(StreamClosedExitCode, string.Empty, Failure ?? "the run failed"),
                _ => StreamClosed(emit),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop means "ask the server to stop, then let it close": an interrupt keeps the
            // session clean for the next turn, where a kill would leave it mid-step.
            await InterruptAsync().ConfigureAwait(false);
            End = ServeTurnEnd.Interrupted;
            throw;
        }
        finally
        {
            linked.Cancel();
            await ObserveAsync(watch).ConfigureAwait(false);
        }
    }

    private async Task WaitForStreamAsync(TaskCompletionSource opened)
    {
        using var budget = new CancellationTokenSource(_streamOpenTimeout);
        try
        {
            await opened.Task.WaitAsync(budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new ServeStartException("the run's event stream did not open");
        }
    }

    private CommandResult StreamClosed(Action<string> emit)
    {
        const string message = "[serve: the run's server closed the stream]";
        emit(message);
        End = ServeTurnEnd.Failed;
        Failure = "the run's server closed the stream";
        return new CommandResult(StreamClosedExitCode, string.Empty, Failure);
    }

    /// <summary>
    /// Ensures the app's own session id exists on this server. The app mints <c>ses_…</c> before
    /// the run starts, so the first turn creates it by id; a later turn finds it.
    /// </summary>
    private async Task EnsureSessionAsync(CancellationToken cancellationToken)
    {
        using (var probe = await _client
                   .GetAsync($"api/session/{Uri.EscapeDataString(_sessionId)}", cancellationToken)
                   .ConfigureAwait(false))
        {
            if (probe.IsSuccessStatusCode)
            {
                await ApplyAgentAndModelAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (probe.StatusCode != HttpStatusCode.NotFound)
            {
                throw new ServeStartException($"the run's session could not be read ({(int)probe.StatusCode})");
            }
        }

        using var content = Json(DescribeSession());
        using var response = await _client
            .PostAsync("api/session", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ServeStartException($"the run's session could not be created ({(int)response.StatusCode})");
        }
    }

    private Dictionary<string, object?> DescribeSession()
    {
        var body = new Dictionary<string, object?> { ["id"] = _sessionId };
        if (!string.IsNullOrWhiteSpace(_agent))
        {
            body["agent"] = _agent;
        }

        if (DescribeModel() is { } model)
        {
            body["model"] = model;
        }

        return body;
    }

    /// <summary>
    /// The app's model string is <c>provider/model</c> when the provider's serving prefix is
    /// already on it, which is the usual case; the served API wants the two apart.
    /// </summary>
    private Dictionary<string, object?>? DescribeModel()
    {
        if (_provider is not { } provider || string.IsNullOrWhiteSpace(provider.Model))
        {
            return null;
        }

        var model = provider.Model;
        var prefix = provider.Id + "/";
        if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            model = model[prefix.Length..];
        }

        return new Dictionary<string, object?>
        {
            ["providerID"] = provider.Id,
            ["id"] = model,
        };
    }

    /// <summary>A session that already exists keeps its own agent and model unless told otherwise.</summary>
    private async Task ApplyAgentAndModelAsync(CancellationToken cancellationToken)
    {
        await PostAsync(
            $"api/session/{Uri.EscapeDataString(_sessionId)}/agent",
            string.IsNullOrWhiteSpace(_agent) ? null : new Dictionary<string, object?> { ["agent"] = _agent },
            "agent",
            cancellationToken).ConfigureAwait(false);

        await PostAsync(
            $"api/session/{Uri.EscapeDataString(_sessionId)}/model",
            DescribeModel() is { } model ? new Dictionary<string, object?> { ["model"] = model } : null,
            "model",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PostAsync(
        string path,
        Dictionary<string, object?>? body,
        string what,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return;
        }

        using var content = Json(body);
        using var response = await _client
            .PostAsync(path, content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ServeStartException($"the run's {what} could not be set ({(int)response.StatusCode})");
        }
    }

    /// <summary>
    /// Admits the turn. <c>resume</c> is left off on purpose: the contract schedules execution
    /// "unless resume is false", so omitting it is what starts the turn.
    /// </summary>
    private async Task PromptAsync(CancellationToken cancellationToken)
    {
        using var content = Json(new Dictionary<string, object?> { ["text"] = _prompt });
        using var response = await _client
            .PostAsync($"api/session/{Uri.EscapeDataString(_sessionId)}/prompt", content, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        throw new ServeStartException(response.StatusCode switch
        {
            HttpStatusCode.NotFound => "the session is gone",
            HttpStatusCode.Conflict => "the session is busy",
            _ => $"the prompt was refused ({status})",
        });
    }

    /// <summary>
    /// Asks the server to stop, then gives it a moment to close the turn itself. A child that
    /// does not answer is killed by its own dispose, so this never blocks a stop for long.
    /// </summary>
    private async Task InterruptAsync()
    {
        try
        {
            using var budget = new CancellationTokenSource(InterruptGrace);
            using var response = await _client
                .PostAsync($"api/session/{Uri.EscapeDataString(_sessionId)}/interrupt?resume=false", null, budget.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or ObjectDisposedException)
        {
            // A stop that cannot reach the server is still a stop: the child is closed next.
        }
    }

    private static StringContent Json(Dictionary<string, object?> body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The watch's failure is already reported through the stream's own outcome.
        }
    }
}