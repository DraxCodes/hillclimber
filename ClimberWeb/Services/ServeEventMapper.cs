using System.Text;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>How the turn behind one serve producer ended.</summary>
public enum ServeTurnEnd
{
    /// <summary>The server has not reported a terminal event yet.</summary>
    Running,

    /// <summary><c>session.execution.succeeded</c> (or a bare <c>session.idle</c>) landed.</summary>
    Succeeded,

    /// <summary><c>session.execution.failed</c> landed; <see cref="ServeEventMapper.Failure"/> says why.</summary>
    Failed,

    /// <summary><c>session.execution.interrupted</c> landed, usually because the reader stopped the run.</summary>
    Interrupted,
}

/// <summary>
/// Turns the served event stream back into the exact <c>opencode run --format json</c> lines the
/// renderer already parses, so the UI cannot tell which producer fed it. One mapping decision is
/// worth stating: text and reasoning are streamed as deltas but emitted **once**, at
/// <c>…ended</c>, because that event carries the complete block and the CLI line it replaces is
/// also complete. Everything outside the mapped families (forms, permissions, ptys, shells) is
/// deliberately dropped — the question channel owns forms, and nothing else has a CLI shape.
/// </summary>
public sealed class ServeEventMapper
{
    private readonly string _sessionId;
    private readonly Dictionary<string, ToolCall> _tools = new(StringComparer.Ordinal);
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    private bool _armed;
    private bool _terminal;
    private bool _failureEmitted;

    public ServeEventMapper(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
    }

    /// <summary>The turn's outcome so far. Terminal states are final.</summary>
    public ServeTurnEnd End { get; private set; } = ServeTurnEnd.Running;

    /// <summary>The server's failure message, when <see cref="End"/> is <see cref="ServeTurnEnd.Failed"/>.</summary>
    public string? Failure { get; private set; }

    /// <summary>True once the server has bracketed this turn with <c>session.execution.started</c>.</summary>
    public bool Armed => _armed;

    /// <summary>
    /// Maps one event to zero or more CLI-shaped JSON lines. A line is returned only for an
    /// event this producer claims; anything else — a foreign session, an unmapped family, a
    /// duplicate — returns an empty list and changes no state.
    /// </summary>
    public IReadOnlyList<string> Map(OpenCodeEvent value)
    {
        if (_terminal
            || value.Data.ValueKind != JsonValueKind.Object
            || !string.Equals(ReadString(value.Data, "sessionID"), _sessionId, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>(2);
        switch (value.Type)
        {
            case "session.execution.started":
                _armed = true;
                break;

            case "session.execution.succeeded":
                if (_armed)
                {
                    End = ServeTurnEnd.Succeeded;
                    _terminal = true;
                }

                break;

            case "session.execution.failed":
                if (_armed)
                {
                    End = ServeTurnEnd.Failed;
                    Failure = ReadError(value.Data) ?? "the run failed";
                    _terminal = true;

                    // A failed step usually reports the same message a moment earlier; the row
                    // it already printed is the one to keep.
                    if (!_failureEmitted)
                    {
                        lines.Add(ErrorLine(Failure));
                    }
                }

                break;

            case "session.execution.interrupted":
                if (_armed)
                {
                    End = ServeTurnEnd.Interrupted;
                    _terminal = true;
                }

                break;

            case "session.idle":
                // The quieter terminal marker. Only trusted after the turn was bracketed, so a
                // stream that opens on a session already sitting idle cannot end the run early.
                if (_armed && End == ServeTurnEnd.Running)
                {
                    End = ServeTurnEnd.Succeeded;
                    _terminal = true;
                }

                break;

            case "session.step.started":
                if (Once(value))
                {
                    lines.Add(Line("step_start", new Dictionary<string, object?>
                    {
                        ["snapshot"] = ReadString(value.Data, "snapshot"),
                    }));
                }

                break;

            case "session.step.ended":
                lines.Add(Line("step_finish", new Dictionary<string, object?>
                {
                    ["reason"] = ReadString(value.Data, "finish") ?? "stop",
                    ["cost"] = ReadNumber(value.Data, "cost"),
                    ["tokens"] = ReadElement(value.Data, "tokens"),
                }));
                break;

            case "session.step.failed":
                // The step's error is worth one row, but only the execution's end decides
                // whether the run failed: a retried step can fail and still succeed.
                lines.Add(ErrorLine(ReadError(value.Data) ?? "the step failed"));
                _failureEmitted = true;
                break;

            case "session.text.ended":
                if (Once(value) && ReadString(value.Data, "text") is { Length: > 0 } text)
                {
                    lines.Add(Line("text", new Dictionary<string, object?> { ["text"] = text }));
                }

                break;

            case "session.reasoning.ended":
                if (Once(value) && ReadString(value.Data, "text") is { Length: > 0 } reasoning)
                {
                    lines.Add(Line("reasoning", new Dictionary<string, object?> { ["text"] = reasoning }));
                }

                break;

            case "session.tool.input.started":
            {
                var id = ReadString(value.Data, "id");
                if (id is { Length: > 0 })
                {
                    // Keep an input already seen: the server does not promise that the name
                    // arrives before the call, and losing the input would lose the command.
                    var previous = _tools.TryGetValue(id, out var known) ? known : new ToolCall("tool", null);
                    _tools[id] = previous with { Name = ReadString(value.Data, "name") ?? previous.Name };
                }

                break;
            }

            case "session.tool.called":
            {
                if (ReadString(value.Data, "id") is not { Length: > 0 } id)
                {
                    break;
                }

                var input = ReadElement(value.Data, "input");
                var call = _tools.TryGetValue(id, out var known) ? known : new ToolCall("tool", null);
                _tools[id] = call with { Input = input };
                lines.Add(ToolLine(call.Name, "running", input, null, null, null));
                break;
            }

            case "session.tool.success":
            {
                var call = Tool(value.Data);
                lines.Add(ToolLine(
                    call.Name,
                    "completed",
                    call.Input,
                    Flatten(value.Data),
                    null,
                    ReadElement(value.Data, "metadata")));
                break;
            }

            case "session.tool.failed":
            {
                var call = Tool(value.Data);
                lines.Add(ToolLine(
                    call.Name,
                    "error",
                    call.Input,
                    Flatten(value.Data),
                    ReadError(value.Data) ?? "the tool call failed",
                    ReadElement(value.Data, "metadata")));
                break;
            }
        }

        return lines;
    }

    private ToolCall Tool(JsonElement data)
    {
        var id = ReadString(data, "id") ?? string.Empty;
        return _tools.TryGetValue(id, out var known) ? known : new ToolCall("tool", null);
    }

    /// <summary>True the first time this block is seen; false for a durable replay of the same id.</summary>
    private bool Once(OpenCodeEvent value)
    {
        var key = $"{value.Type}:{ReadString(value.Data, "assistantMessageID")}:{ReadRaw(value.Data, "ordinal")}";
        return _emitted.Add(key);
    }

    private static string ToolLine(
        string tool,
        string status,
        JsonElement? input,
        string? output,
        string? error,
        JsonElement? metadata)
    {
        var state = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["input"] = input,
            ["output"] = output,
            ["error"] = error,
            ["metadata"] = metadata,
        };

        return Line("tool_use", new Dictionary<string, object?> { ["tool"] = tool, ["state"] = state });
    }

    private static string ErrorLine(string message) =>
        JsonSerializer.Serialize(new
        {
            type = "error",
            error = new { name = "SessionError", data = new { message } },
        });

    private static string Line(string type, Dictionary<string, object?> part) =>
        JsonSerializer.Serialize(new
        {
            type,
            part,
        });

    /// <summary>Joins the text blocks of a <c>session.tool.*</c> <c>content</c> array.</summary>
    private static string? Flatten(JsonElement data)
    {
        if (!data.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && ReadString(item, "text") is { Length: > 0 } block)
            {
                if (text.Length > 0)
                {
                    text.Append('\n');
                }

                text.Append(block);
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>Reads <c>error.message</c>, or a bare string <c>error</c>, without throwing.</summary>
    private static string? ReadError(JsonElement data)
    {
        if (!data.TryGetProperty("error", out var error))
        {
            return null;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return error.ValueKind == JsonValueKind.Object
            ? ReadString(error, "message") ?? ReadString(error, "type")
            : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The value's literal text for a string or number, so ids and ordinals key alike.</summary>
    private static string ReadRaw(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.GetRawText()
            : string.Empty;

    private static double? ReadNumber(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private static JsonElement? ReadElement(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? value
            : null;

    private sealed record ToolCall(string Name, JsonElement? Input);
}