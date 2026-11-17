using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>One server-sent event: its optional <c>event:</c> name and its <c>data:</c> payload.</summary>
public readonly record struct SseFrame(string? Event, string Data);

/// <summary>
/// One opencode event: the <c>type</c> and <c>data</c> from the frame's JSON payload. opencode
/// leaves the SSE <c>event:</c> field empty and puts the type inside the JSON, so this is the
/// authoritative shape.
/// </summary>
public readonly record struct OpenCodeEvent(string Type, JsonElement Data);

/// <summary>
/// Incremental parser for <c>text/event-stream</c>. Feed it one line at a time; it returns a
/// frame when a blank line closes one. Comments and unknown fields are ignored, and a frame
/// with no data is never emitted.
/// </summary>
public sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private string? _event;

    /// <summary>Consumes one line and returns the finished frame, if this line closed one.</summary>
    public SseFrame? Push(string? line)
    {
        if (line is null)
        {
            return null;
        }

        if (line.Length == 0)
        {
            return Complete();
        }

        if (line[0] == ':')
        {
            return null;
        }

        var colon = line.IndexOf(':');
        var field = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? string.Empty : line[(colon + 1)..];

        // A single leading space after the colon is part of the framing, not the data.
        if (value.Length > 0 && value[0] == ' ')
        {
            value = value[1..];
        }

        if (field == "data")
        {
            if (_data.Length > 0)
            {
                _data.Append('\n');
            }

            _data.Append(value);
        }
        else if (field == "event")
        {
            _event = value.Length == 0 ? null : value;
        }

        return null;
    }

    private SseFrame? Complete()
    {
        if (_data.Length == 0)
        {
            _event = null;
            return null;
        }

        var frame = new SseFrame(_event, _data.ToString());
        _data.Clear();
        _event = null;
        return frame;
    }
}

/// <summary>
/// The transport boundary the channel talks to: one run's forms. Tests substitute it; the
/// production implementation is <see cref="OpenCodeFormClient"/> over a spawned child.
/// </summary>
public interface IFormWire
{
    Task<IReadOnlyList<QuestionState>> ListAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<QuestionState?> GetAsync(string sessionId, string formId, CancellationToken cancellationToken = default);

    Task<QuestionSubmitOutcome> ReplyAsync(
        string sessionId,
        string formId,
        string fieldKey,
        QuestionKind kind,
        string value,
        CancellationToken cancellationToken = default);

    Task<QuestionSubmitOutcome> CancelAsync(
        string sessionId,
        string formId,
        string message,
        CancellationToken cancellationToken = default);

    Task WatchAsync(Func<SseFrame, Task> onFrame, CancellationToken cancellationToken, Action? onOpen = null);
}

/// <summary>
/// The wire for one run's forms: list, get, reply, cancel and the event stream. It maps the
/// server's status codes to <see cref="QuestionSubmitOutcome"/> and never decides what a state
/// means; the channel does that. Only display data leaves this class.
/// </summary>
public sealed class OpenCodeFormClient : IFormWire
{
    private static readonly TimeSpan DefaultSubmitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _client;
    private readonly TimeSpan _submitTimeout;
    private readonly TimeSpan _readTimeout;

    public OpenCodeFormClient(
        HttpClient client,
        TimeSpan? submitTimeout = null,
        TimeSpan? readTimeout = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _submitTimeout = submitTimeout ?? DefaultSubmitTimeout;
        _readTimeout = readTimeout ?? DefaultReadTimeout;
    }

    /// <summary>
    /// Every form the server will list for <paramref name="sessionId"/>. A form carrying a
    /// settled state is mapped to it; one with no state is <see cref="QuestionStatus.Pending"/>.
    /// </summary>
    public async Task<IReadOnlyList<QuestionState>> ListAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = Timeout(cancellationToken, _readTimeout);
        using var response = await _client
            .GetAsync($"api/session/{Uri.EscapeDataString(sessionId)}/form", timeout.Token)
            .ConfigureAwait(false);

        // The channel attaches before the producer has created the session, so an unknown
        // session is the normal first read, not a failure: it has no forms yet, and the event
        // stream carries the ones the run raises afterwards.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<QuestionState>();
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return ParseForms(body);
    }

    /// <summary>One form, or null when the server no longer knows it (404).</summary>
    public async Task<QuestionState?> GetAsync(
        string sessionId,
        string formId,
        CancellationToken cancellationToken = default)
    {
        using var timeout = Timeout(cancellationToken, _readTimeout);
        using var response = await _client
            .GetAsync(
                $"api/session/{Uri.EscapeDataString(sessionId)}/form/{Uri.EscapeDataString(formId)}",
                timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        using var document = JsonDocument.Parse(body);
        var form = Unwrap(document.RootElement);
        return Settle(QuestionSupport.FromForm(form), form);
    }

    /// <summary>
    /// Sends one answer. A choice sends the option's <c>value</c> as a JSON string; a boolean
    /// sends JSON <c>true</c>/<c>false</c>. Unsupported shapes throw: they never reach the wire.
    /// </summary>
    public async Task<QuestionSubmitOutcome> ReplyAsync(
        string sessionId,
        string formId,
        string fieldKey,
        QuestionKind kind,
        string value,
        CancellationToken cancellationToken = default)
    {
        var body = BuildAnswer(kind, fieldKey, value);

        using var timeout = Timeout(cancellationToken, _submitTimeout);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await _client
                .PostAsync(
                    $"api/session/{Uri.EscapeDataString(sessionId)}/form/{Uri.EscapeDataString(formId)}/reply",
                    content,
                    timeout.Token)
                .ConfigureAwait(false);

            return MapStatus(response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QuestionSubmitOutcome.Unconfirmed;
        }
        catch (Exception exception) when (IsDisconnect(exception))
        {
            return QuestionSubmitOutcome.Disconnected;
        }
    }

    /// <summary>Cancels a form. The message is for the model, never echoed in the event.</summary>
    public async Task<QuestionSubmitOutcome> CancelAsync(
        string sessionId,
        string formId,
        string message,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/session/{Uri.EscapeDataString(sessionId)}/form/{Uri.EscapeDataString(formId)}";
        if (!string.IsNullOrWhiteSpace(message))
        {
            path += "?message=" + Uri.EscapeDataString(message);
        }

        using var timeout = Timeout(cancellationToken, _submitTimeout);
        try
        {
            using var response = await _client.DeleteAsync(path, timeout.Token).ConfigureAwait(false);
            return MapStatus(response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QuestionSubmitOutcome.Unconfirmed;
        }
        catch (Exception exception) when (IsDisconnect(exception))
        {
            return QuestionSubmitOutcome.Disconnected;
        }
    }

    /// <summary>
    /// Streams <c>GET /api/event</c> until cancelled or the server closes the stream. The
    /// caller owns reconnection and filtering; every frame is handed over unfiltered.
    /// <paramref name="onOpen"/> runs once the stream is live, before any frame — the producer
    /// uses it to admit its first turn only after it is certain it can see the whole of it.
    /// </summary>
    public async Task WatchAsync(
        Func<SseFrame, Task> onFrame,
        CancellationToken cancellationToken,
        Action? onOpen = null)
    {
        ArgumentNullException.ThrowIfNull(onFrame);

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/event");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        onOpen?.Invoke();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var parser = new SseParser();

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (parser.Push(line) is { } frame)
            {
                await onFrame(frame).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The reply body. Public only so the status/body table is testable without a server;
    /// every caller goes through <see cref="ReplyAsync"/>.
    /// </summary>
    public static string BuildAnswer(QuestionKind kind, string fieldKey, string value)
    {
        if (string.IsNullOrEmpty(fieldKey))
        {
            throw new ArgumentException("A reply needs a field key.", nameof(fieldKey));
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("A reply needs a value.", nameof(value));
        }

        object answer = kind switch
        {
            QuestionKind.Choice => value,
            QuestionKind.Boolean => value switch
            {
                "true" => true,
                "false" => false,
                _ => throw new ArgumentException("A boolean answer must be true or false.", nameof(value)),
            },
            _ => throw new InvalidOperationException("An unsupported form cannot be answered."),
        };

        return JsonSerializer.Serialize(
            new { answer = new Dictionary<string, object>(StringComparer.Ordinal) { [fieldKey] = answer } },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>Maps one reply/cancel status code to the outcome the row shows.</summary>
    public static QuestionSubmitOutcome MapStatus(HttpStatusCode status) => (int)status switch
    {
        200 or 201 or 204 => QuestionSubmitOutcome.Accepted,
        404 or 409 => QuestionSubmitOutcome.AlreadySettled,
        400 or 422 => QuestionSubmitOutcome.InvalidAnswer,
        _ => QuestionSubmitOutcome.Disconnected,
    };

    // ------------------------------------------------------------------ internals

    internal static IReadOnlyList<QuestionState> ParseForms(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<QuestionState>();
        }

        using var document = JsonDocument.Parse(body);
        var root = Unwrap(document.RootElement);

        // The list route answers with an array, possibly inside the envelope's data property.
        var array = root.ValueKind == JsonValueKind.Array ? root : FindArray(root);

        if (array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<QuestionState>();
        }

        var states = new List<QuestionState>();
        foreach (var form in array.EnumerateArray())
        {
            if (form.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            states.Add(Settle(QuestionSupport.FromForm(form), form));
        }

        return states;
    }

    /// <summary>
    /// The server wraps every REST payload in <c>{"data":…}</c> and every event in an envelope
    /// whose type is <c>type</c> and whose payload is <c>data</c>. Unwrap the outer layer only.
    /// </summary>
    internal static JsonElement Unwrap(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("data", out var data)
        && data.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? data
            : root;

    /// <summary>Parses one event frame's payload into its type and data.</summary>
    public static bool TryParseEvent(string? payload, out OpenCodeEvent value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() is not { Length: > 0 } name)
            {
                return false;
            }

            var data = root.TryGetProperty("data", out var payloadElement)
                ? payloadElement.Clone()
                : default;

            value = new OpenCodeEvent(name, data);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement FindArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        foreach (var name in new[] { "forms", "data", "items" })
        {
            if (root.TryGetProperty(name, out var value)
                && (value.ValueKind == JsonValueKind.Array || value.ValueKind == JsonValueKind.Object))
            {
                return value.ValueKind == JsonValueKind.Array ? value : FindArray(value);
            }
        }

        return default;
    }

    /// <summary>Applies a <c>state</c> object, when present, to a form parsed as pending.</summary>
    private static QuestionState Settle(QuestionState state, JsonElement form)
    {
        if (!form.TryGetProperty("state", out var info) || info.ValueKind != JsonValueKind.Object)
        {
            return state;
        }

        var status = ReadString(info, "status") switch
        {
            "answered" => QuestionStatus.Answered,
            "cancelled" or "canceled" => QuestionStatus.Cancelled,
            "pending" => QuestionStatus.Pending,
            _ => QuestionStatus.Stale,
        };

        if (status != QuestionStatus.Answered)
        {
            return state with { Status = status };
        }

        var chosen = ReadAnswer(info, state.FieldKey);
        return state with
        {
            Status = status,
            ChosenValue = chosen ?? state.ChosenValue,
        };
    }

    private static string? ReadAnswer(JsonElement state, string fieldKey)
    {
        if (!state.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!answer.TryGetProperty(fieldKey, out var value))
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

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CancellationTokenSource Timeout(CancellationToken cancellationToken, TimeSpan budget)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(budget);
        return source;
    }

    private static bool IsDisconnect(Exception exception) =>
        exception is HttpRequestException
            or IOException
            or InvalidOperationException
            or ObjectDisposedException;
}