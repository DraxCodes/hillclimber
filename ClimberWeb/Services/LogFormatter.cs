using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>The shape of a rendered log line, after <see cref="LogFormatter"/> classification.</summary>
public enum LogKind
{
    /// <summary>Anything that is not a recognised opencode JSON event; rendered verbatim.</summary>
    Plain,

    /// <summary>An opencode <c>text</c> event; <see cref="LogEntry.Text"/> is the message.</summary>
    Text,

    /// <summary>An opencode <c>tool_use</c> event; tool name, input summary and output are populated.</summary>
    Tool,

    /// <summary>
    /// An opencode <c>subagent</c> tool call; <see cref="LogEntry.Text"/> is the agent name,
    /// the input is its prompt, and the output is its (unwrapped) result.
    /// </summary>
    Subagent,

    /// <summary>An opencode <c>step_start</c> event.</summary>
    StepStart,

    /// <summary>An opencode <c>step_finish</c> event.</summary>
    StepFinish,

    /// <summary>An opencode <c>error</c> event.</summary>
    Error,

    /// <summary>
    /// An opencode <c>reasoning</c> event; <see cref="LogEntry.Text"/> is the thought.
    /// An empty thought carries no displayable content and is skipped by the grouper.
    /// </summary>
    Reasoning,

    /// <summary>
    /// A JSON event whose <c>type</c> this parser does not know. The raw payload stays
    /// available verbatim behind a disclosure instead of filling the feed.
    /// </summary>
    Unrecognized,

    /// <summary>
    /// The prompt that opened a turn. Never produced by parsing: the session coordinator
    /// synthesizes one when a stored session is restored, so the reader's own question is
    /// part of the transcript and not only the model's answer.
    /// </summary>
    UserPrompt,
}

/// <summary>A log line prepared for display.</summary>
/// <param name="Raw">The original line exactly as it lives in <c>ClimberState.Logs</c>.</param>
/// <param name="Timestamp">The <c>HH:mm:ss</c> stamp, or empty when the line carries none.</param>
/// <param name="Kind">How the line should be rendered.</param>
/// <param name="Text">
/// The primary display text: the message for <see cref="LogKind.Text"/>, the tool name for
/// <see cref="LogKind.Tool"/>, and the marker/summary for the step and error kinds.
/// </param>
/// <param name="ToolInput">One-line summary of the tool input; may be empty.</param>
/// <param name="ToolOutput">Tool output, or the error text when a tool failed; may be null.</param>
/// <param name="Status">The step's completion state; <see cref="StepStatus.None"/> for non-steps.</param>
/// <param name="ToolTitle">opencode's human description of the call (<c>state.title</c>); may be null.</param>
/// <param name="Command">The full shell command when the tool is a shell; may be null.</param>
/// <param name="ExitCode">The command's exit code (<c>state.metadata.exit</c>); may be null.</param>
public sealed record LogEntry(
    string Raw,
    string Timestamp,
    LogKind Kind,
    string Text,
    string? ToolInput = null,
    string? ToolOutput = null,
    StepStatus Status = StepStatus.None,
    string? ToolTitle = null,
    string? Command = null,
    int? ExitCode = null);

/// <summary>
/// Turns the run's log feed into display-ready <see cref="LogEntry"/> rows. Lines the
/// hill-climb loop writes itself, and <c>dotnet test</c> / <c>git</c> output, pass through
/// as <see cref="LogKind.Plain"/>. Lines emitted by <c>opencode run --format json</c> — one
/// long JSON object per event — are parsed so the panel shows what the event means instead
/// of its raw payload.
/// </summary>
/// <remarks>
/// Parsing is a render-only concern: the log feed in <c>ClimberState.Logs</c> is never modified.
/// Results are cached per line because the UI re-renders on every state change.
/// </remarks>
public sealed class LogFormatter
{
    private const int MaxCachedLines = 6000;
    private const int MaxSummaryLength = 100;
    private const int MaxPromptLength = 500;
    private const int MaxReasoningLength = 4000;
    private const int MaxCommandLength = 16000;
    private const int MaxTitleLength = 200;
    private const int MaxPayloadLength = 2000;

    private static readonly Regex StampRegex = new(@"^\[(\d{2}:\d{2}:\d{2})\]\s?", RegexOptions.Compiled);

    private readonly Dictionary<string, LogEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Classifies <paramref name="line"/>, caching the result (see <see cref="Parse"/>).</summary>
    public LogEntry Format(string line)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(line, out var cached))
            {
                return cached;
            }
        }

        var entry = Parse(line);

        lock (_gate)
        {
            if (_cache.Count >= MaxCachedLines)
            {
                _cache.Clear();
            }

            _cache[line] = entry;
        }

        return entry;
    }

    /// <summary>
    /// Classifies a single stamped log line. Never throws: malformed JSON, JSON that is not an
    /// opencode event, and unknown event types all fall back to <see cref="LogKind.Plain"/>.
    /// </summary>
    public static LogEntry Parse(string line)
    {
        line ??= string.Empty;
        var (timestamp, payload) = SplitStamp(line);
        var plain = new LogEntry(line, timestamp, LogKind.Plain, line);

        // Cheap guard: opencode events are JSON objects. Anything else skips the parser.
        var trimmed = payload.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return plain;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return plain;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || typeElement.GetString() is not { Length: > 0 } type)
            {
                return plain;
            }

            // A real opencode error has no `part`; it carries `error.name` and
            // `error.data.message` at the root. Handle it before the part guard so the
            // error does not degrade to a raw JSON line.
            if (string.Equals(type, "error", StringComparison.Ordinal))
            {
                var errorPart = root.TryGetProperty("part", out var candidate) && candidate.ValueKind == JsonValueKind.Object
                    ? candidate
                    : default;
                return plain with { Kind = LogKind.Error, Text = SummarizeError(root, errorPart) };
            }

            if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object)
            {
                return plain;
            }

            return type switch
            {
                "text" => TextEntry(plain, part),
                "reasoning" => plain with
                {
                    Kind = LogKind.Reasoning,
                    Text = Truncation.Cut(Clean(GetString(part, "text") ?? string.Empty), MaxReasoningLength),
                },
                "tool_use" => ToolEntry(plain, part),
                "step_start" => plain with { Kind = LogKind.StepStart, Text = "step start" },
                "step_finish" => plain with { Kind = LogKind.StepFinish, Text = SummarizeFinish(part) },
                // A session diff is a known shape even though its type is not: the files it
                // touched are the reader's fact, and the stamped log line is not.
                "session.diff" => plain with
                {
                    Kind = LogKind.Unrecognized,
                    Text = "session.diff",
                    ToolInput = SummarizeFiles(part),
                },
                // Known to be an opencode event, but not one this renderer understands. The
                // type is model-adjacent data, so bound it before it becomes a title. The
                // payload keeps its JSON but loses the log timestamp, which is machinery.
                _ => plain with
                {
                    Kind = LogKind.Unrecognized,
                    Text = Truncation.Cut(Clean(type), MaxSummaryLength),
                    ToolOutput = Truncation.Cut(Clean(payload), MaxPayloadLength),
                },
            };
        }
    }

    private static LogEntry TextEntry(LogEntry plain, JsonElement part)
    {
        var text = GetString(part, "text");
        return string.IsNullOrEmpty(text)
            ? plain
            : plain with { Kind = LogKind.Text, Text = text };
    }

    private static LogEntry ToolEntry(LogEntry plain, JsonElement part)
    {
        var tool = GetString(part, "tool");
        if (string.IsNullOrEmpty(tool))
        {
            return plain;
        }

        var hasState = part.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object;
        var input = hasState && state.TryGetProperty("input", out var rawInput) ? rawInput : default;
        var output = hasState ? GetString(state, "output") : null;
        var error = hasState ? GetText(state, "error") : null;
        var status = hasState ? ReadStatus(state) : StepStatus.None;
        var stateTitle = hasState ? GetString(state, "title") : null;
        var inputTitle = hasState ? GetString(input, "description") : null;
        var exitCode = hasState ? ReadExitCode(state) : null;

        // opencode puts the human description in `state.title`; older fixtures only
        // carry the model's `input.description`. Either beats the tool's own name.
        var title = string.IsNullOrWhiteSpace(stateTitle) || string.Equals(stateTitle, tool, StringComparison.Ordinal)
            ? inputTitle
            : stateTitle;

        // A command that exited non-zero failed, even when the tool reports "completed".
        if (exitCode is not null and not 0)
        {
            status = StepStatus.Failed;
        }

        // A failed call often carries only `state.error`; keep it as the expandable body.
        // Tool output is model-influenced, so strip bidi/control characters here too.
        var detail = string.IsNullOrEmpty(output) ? error : output;
        detail = string.IsNullOrEmpty(detail) ? detail : Clean(detail);

        if (string.Equals(tool, "subagent", StringComparison.Ordinal))
        {
            return SubagentEntry(plain, input, detail, status);
        }

        var isShell = tool is "shell" or "bash";
        return plain with
        {
            Kind = LogKind.Tool,
            Text = tool,
            ToolInput = SummarizeInput(input),
            ToolOutput = detail,
            Status = status,
            ToolTitle = title is null || string.Equals(title, tool, StringComparison.Ordinal)
                ? null
                : Truncation.Cut(Clean(title), MaxTitleLength),
            Command = isShell ? ReadCommand(input) : null,
            ExitCode = exitCode,
        };
    }

    /// <summary>Reads <c>state.metadata.exit</c> (or <c>exitCode</c>/<c>code</c>) as an exit code.</summary>
    private static int? ReadExitCode(JsonElement state)
    {
        if (!state.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "exit", "exitCode", "code" })
        {
            if (metadata.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var code))
            {
                return code;
            }
        }

        return null;
    }

    /// <summary>The full command from a shell tool's input, or null when it has none.</summary>
    private static string? ReadCommand(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in new[] { "command", "cmd" })
        {
            var value = GetString(input, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Truncation.Cut(Clean(value), MaxCommandLength);
            }
        }

        return null;
    }

    /// <summary>Maps opencode's tool <c>state.status</c> onto the display status.</summary>
    private static StepStatus ReadStatus(JsonElement state) => GetString(state, "status") switch
    {
        "running" or "pending" or "in_progress" => StepStatus.Running,
        "error" or "failed" => StepStatus.Failed,
        _ => StepStatus.None,
    };

    /// <summary>
    /// A <c>subagent</c> call: the agent name, its (flattened) prompt, and its result with
    /// opencode's <c>&lt;subagent …&gt;…&lt;/subagent&gt;</c> wrapper removed.
    /// </summary>
    private static LogEntry SubagentEntry(LogEntry plain, JsonElement input, string? output, StepStatus status)
    {
        var isObject = input.ValueKind == JsonValueKind.Object;
        var agent = isObject ? GetString(input, "agent") : null;
        var prompt = isObject ? GetString(input, "prompt") : null;
        var result = StripSubagentWrapper(output);

        return plain with
        {
            Kind = LogKind.Subagent,
            Text = string.IsNullOrEmpty(agent) ? "subagent" : agent,
            ToolInput = string.IsNullOrEmpty(prompt) ? null : Truncation.Cut(Flatten(prompt), MaxPromptLength),
            ToolOutput = string.IsNullOrEmpty(result) ? null : result,
            Status = status,
        };
    }

    /// <summary>Removes the <c>&lt;subagent …&gt;…&lt;/subagent&gt;</c> wrapper from a subagent result.</summary>
    private static string? StripSubagentWrapper(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output;
        }

        var value = output.Trim();
        if (value.StartsWith("<subagent", StringComparison.Ordinal))
        {
            var open = value.IndexOf('>');
            if (open >= 0)
            {
                value = value[(open + 1)..];
            }
        }

        var close = value.LastIndexOf("</subagent>", StringComparison.Ordinal);
        if (close >= 0)
        {
            value = value[..close];
        }

        return value.Trim();
    }

    private static string SummarizeFinish(JsonElement part)
    {
        var reason = GetString(part, "reason") ?? "stop";
        var usage = OpenCodeEvents.ReadUsage(part);

        // Human words, not the event's wire names: this line is read, not parsed.
        var summary = new StringBuilder()
            .Append(string.Equals(reason, "tool-calls", StringComparison.Ordinal) ? "tool calls" : reason)
            .Append(" · $").Append(usage.Cost.ToString("0.######"))
            .Append(" · ").Append(usage.Input).Append(" in / ").Append(usage.Output).Append(" out");

        if (usage.CacheRead > 0 || usage.CacheWrite > 0)
        {
            summary.Append(" · cache ").Append(usage.CacheRead).Append(" read");
            if (usage.CacheWrite > 0)
            {
                summary.Append(" / ").Append(usage.CacheWrite).Append(" write");
            }
        }

        if (usage.Reasoning > 0)
        {
            summary.Append(" · ").Append(usage.Reasoning).Append(" reasoning");
        }

        return summary.ToString();
    }

    /// <summary>
    /// The files a <c>session.diff</c> event names, one per line, or null when it names none.
    /// Newline-separated because the row lists them; the caller counts the lines.
    /// </summary>
    private static string? SummarizeFiles(JsonElement part)
    {
        if (!part.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind == JsonValueKind.String && file.GetString() is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? null : string.Join('\n', names);
    }

    private static string SummarizeError(JsonElement root, JsonElement part)
    {
        // Shipped shape: `error.data.message`; fixtures use `part.error` or `part.message`.
        var message = GetString(part, "error") ?? GetString(part, "message");

        if (string.IsNullOrWhiteSpace(message)
            && root.TryGetProperty("error", out var error)
            && error.ValueKind != JsonValueKind.Null)
        {
            if (error.ValueKind == JsonValueKind.String)
            {
                message = error.GetString();
            }
            else if (error.ValueKind == JsonValueKind.Object)
            {
                var detail = error.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                    ? data
                    : error;
                message = GetString(detail, "message")
                    ?? GetString(error, "message")
                    ?? GetString(detail, "name")
                    ?? GetString(error, "name")
                    ?? error.GetRawText();
            }
            else
            {
                message = error.GetRawText();
            }
        }

        return string.IsNullOrWhiteSpace(message) ? "error" : Flatten(message);
    }

    private static string SummarizeInput(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var property in input.EnumerateObject())
        {
            // The model's own description is promoted to the row title when opencode
            // did not supply one; repeating it as `description=…` is noise on every row.
            if (string.Equals(property.Name, "description", StringComparison.Ordinal))
            {
                continue;
            }

            if (parts.Count >= 3)
            {
                break;
            }

            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };

            if (value is not null)
            {
                parts.Add($"{property.Name}={Truncation.Cut(Flatten(value), MaxSummaryLength)}");
            }
        }

        return string.Join(" · ", parts);
    }

    private static (string Timestamp, string Payload) SplitStamp(string line)
    {
        var match = StampRegex.Match(line);
        return match.Success
            ? (match.Groups[1].Value, line[match.Length..])
            : (string.Empty, line);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Reads a property as text, accepting a string or falling back to its raw JSON. Tool
    /// errors are usually strings, but an object-shaped error must still have an expandable
    /// body rather than disappearing.
    /// </summary>
    private static string? GetText(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    private static string Flatten(string value) =>
        Clean(value).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

    /// <summary>
    /// Removes Unicode format and non-whitespace control characters from model-written
    /// text. Razor escapes the result; this stops bidi overrides ("Trojan Source") and
    /// zero-width characters from spoofing the reading order of a transcript.
    /// </summary>
    private static string Clean(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        StringBuilder? builder = null;
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            var remove = char.GetUnicodeCategory(ch) == UnicodeCategory.Format
                || (char.IsControl(ch) && ch is not '\n' and not '\r' and not '\t');

            if (remove && builder is null)
            {
                builder = new StringBuilder(value.Length).Append(value, 0, index);
            }
            else if (!remove)
            {
                builder?.Append(ch);
            }
        }

        return builder?.ToString() ?? value;
    }
}
