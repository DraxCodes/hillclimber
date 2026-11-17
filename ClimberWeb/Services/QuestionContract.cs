using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClimberWeb.Services;

/// <summary>What the row can do with a form's single field.</summary>
public enum QuestionKind
{
    /// <summary>A <c>string</c> field with a non-empty option list; one option is committed.</summary>
    Choice,

    /// <summary>A <c>boolean</c> field, rendered as the server's own <c>Yes</c>/<c>No</c> pair.</summary>
    Boolean,

    /// <summary>Every other shape: free text, numbers, multi-select, external, or more than one field.</summary>
    Unsupported,
}

/// <summary>The lifecycle state of one question row.</summary>
public enum QuestionStatus
{
    Pending,
    Submitting,
    Answered,
    Cancelled,
    Unsupported,
    Refused,
    Unconfirmed,
    Stale,
    Unavailable,
}

/// <summary>One listed answer, exactly as the server wrote it.</summary>
public sealed record QuestionOption(string Value, string Label, string? Description = null);

/// <summary>
/// Everything the row needs to render and the channel needs to answer. Display-only: no URL,
/// password, header or raw frame may enter this record.
/// </summary>
/// <param name="Restored">
/// True when the state came back from a stored session rather than a live channel. Nothing in
/// an archived session can be answered, dismissed or checked, so a restored row is inert.
/// </param>
public sealed record QuestionState(
    string FormId,
    string SessionId,
    string Title,
    string FieldKey,
    QuestionKind Kind,
    IReadOnlyList<QuestionOption> Options,
    string? ChosenValue,
    QuestionStatus Status,
    string? Notice = null,
    string? KindLabel = null,
    bool Restored = false);

/// <summary>
/// Projects a question state read back from a stored session into the only state a restored
/// transcript may show. The store keeps the status that was true when the session was saved;
/// this is the read-side rule: a pending or mid-submit row can no longer be answered, so it
/// reads <see cref="QuestionStatus.Stale"/> (<c>no answer sent</c>), and every restored row is
/// marked inert so no action is offered over a channel that no longer exists.
/// </summary>
public static class QuestionRestore
{
    /// <summary>Returns <paramref name="state"/> as a restored, non-interactive row.</summary>
    public static QuestionState Apply(QuestionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state with
        {
            Status = state.Status is QuestionStatus.Pending or QuestionStatus.Submitting
                ? QuestionStatus.Stale
                : state.Status,
            Restored = true,
        };
    }
}

/// <summary>The result of one submit or cancel, mapped from the server's status.</summary>
public enum QuestionSubmitOutcome
{
    Accepted,
    AlreadySettled,
    InvalidAnswer,
    Unconfirmed,
    Disconnected,
}

/// <summary>The classified shape of one form.</summary>
public sealed record QuestionSpec(
    QuestionKind Kind,
    string FieldKey,
    string Title,
    IReadOnlyList<QuestionOption> Options,
    string? KindLabel);

/// <summary>
/// Classifies a form's fields into the v1 matrix, and builds the state the row renders.
/// The same rules are enforced where a reply is sent: an unsupported form can never reach
/// <c>session.form.reply</c>.
/// </summary>
public static class QuestionSupport
{
    /// <summary>
    /// Classifies <paramref name="fields"/> for a form titled <paramref name="formTitle"/>.
    /// Exactly one <c>string</c> field with options is a choice; exactly one <c>boolean</c> is
    /// a boolean; everything else is unsupported and carries the kind label the notice uses.
    /// </summary>
    public static QuestionSpec Classify(string? formTitle, JsonElement fields)
    {
        var list = new List<JsonElement>();
        if (fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in fields.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    list.Add(item);
                }
            }
        }

        var title = string.IsNullOrWhiteSpace(formTitle) ? null : formTitle;

        if (list.Count != 1)
        {
            var key = list.Count > 0 ? ReadKey(list[0]) : string.Empty;
            return new QuestionSpec(
                QuestionKind.Unsupported,
                key,
                title ?? key,
                Array.Empty<QuestionOption>(),
                list.Count > 1 ? "more than one answer required" : "an unsupported answer is required");
        }

        var field = list[0];
        var fieldKey = ReadKey(field);
        var fieldTitle = ReadString(field, "title");
        var display = title ?? (string.IsNullOrWhiteSpace(fieldTitle) ? fieldKey : fieldTitle);
        var type = ReadString(field, "type") ?? string.Empty;

        // A hidden field, or one whose visibility depends on another field, is not answerable
        // from a single-field row: the condition can never be evaluated here.
        if (ReadBool(field, "hidden") == true || HasWhen(field))
        {
            return Unsupported(fieldKey, display, "an unsupported answer is required");
        }

        return type switch
        {
            "string" => StringSpec(field, fieldKey, display),
            "boolean" => new QuestionSpec(
                QuestionKind.Boolean,
                fieldKey,
                display,
                new[]
                {
                    new QuestionOption("true", "Yes"),
                    new QuestionOption("false", "No"),
                },
                null),
            "multiselect" => Unsupported(fieldKey, display, "several answers required"),
            "number" => Unsupported(fieldKey, display, "number answer required"),
            "integer" => Unsupported(fieldKey, display, "whole-number answer required"),
            "external" => Unsupported(fieldKey, display, "a link is required"),
            _ => Unsupported(fieldKey, display, "an unsupported answer is required"),
        };
    }

    /// <summary>Builds the initial row state from a <c>Form.Info</c> (or <c>Form.Detail</c>').</summary>
    public static QuestionState FromForm(JsonElement form)
    {
        var id = ReadString(form, "id") ?? string.Empty;
        var session = ReadString(form, "sessionID") ?? string.Empty;
        var title = ReadString(form, "title");
        var fields = form.TryGetProperty("fields", out var value) ? value : default;
        var spec = Classify(title, fields);

        return new QuestionState(
            id,
            session,
            spec.Title,
            spec.FieldKey,
            spec.Kind,
            spec.Options,
            null,
            spec.Kind == QuestionKind.Unsupported ? QuestionStatus.Unsupported : QuestionStatus.Pending,
            null,
            spec.KindLabel);
    }

    private static QuestionSpec StringSpec(JsonElement field, string key, string title)
    {
        var options = ReadOptions(field);
        return options.Count > 0
            ? new QuestionSpec(QuestionKind.Choice, key, title, options, null)
            : Unsupported(key, title, "text answer required");
    }

    private static QuestionSpec Unsupported(string key, string title, string label) =>
        new(QuestionKind.Unsupported, key, title, Array.Empty<QuestionOption>(), label);

    private static List<QuestionOption> ReadOptions(JsonElement field)
    {
        var options = new List<QuestionOption>();
        if (!field.TryGetProperty("options", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return options;
        }

        foreach (var option in value.EnumerateArray())
        {
            if (option.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var optionValue = ReadString(option, "value");
            if (string.IsNullOrEmpty(optionValue))
            {
                continue;
            }

            var label = ReadString(option, "label");
            options.Add(new QuestionOption(
                optionValue,
                string.IsNullOrWhiteSpace(label) ? optionValue : label,
                ReadString(option, "description")));
        }

        return options;
    }

    private static bool HasWhen(JsonElement field) =>
        field.TryGetProperty("when", out var when)
        && when.ValueKind == JsonValueKind.Array
        && when.GetArrayLength() > 0;

    private static string ReadKey(JsonElement field) => ReadString(field, "key") ?? string.Empty;

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}

/// <summary>
/// The display line for one question state. It is a compact JSON object with a private
/// <c>type</c>, parsed back by <see cref="LogFormatter"/>; it carries display data only.
/// </summary>
public static class QuestionLine
{
    /// <summary>The private event type. Not an opencode event.</summary>
    public const string Type = "climber.question";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes <paramref name="state"/> as one line (no newline).</summary>
    public static string Write(QuestionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var payload = new Payload(
            Type,
            new StatePayload(
                state.FormId,
                state.SessionId,
                state.Title,
                state.FieldKey,
                state.Kind.ToString(),
                state.Options.Select(option => new OptionPayload(option.Value, option.Label, option.Description)).ToArray(),
                state.ChosenValue,
                state.Status.ToString(),
                state.Notice,
                state.KindLabel));

        return JsonSerializer.Serialize(payload, Options);
    }

    /// <summary>
    /// Parses a line written by <see cref="Write"/> (with or without the feed's
    /// <c>[HH:mm:ss]</c> stamp). Returns null for anything else, so a malformed line falls
    /// back to text instead of becoming a question.
    /// </summary>
    public static QuestionState? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var start = line.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line[start..]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal)
                || !root.TryGetProperty("question", out var question)
                || question.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var formId = ReadString(question, "formId");
            if (string.IsNullOrEmpty(formId))
            {
                return null;
            }

            if (!TryParse(ReadString(question, "kind"), out QuestionKind kind)
                || !TryParse(ReadString(question, "status"), out QuestionStatus status))
            {
                return null;
            }

            var options = new List<QuestionOption>();
            if (question.TryGetProperty("options", out var optionsElement)
                && optionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in optionsElement.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var value = ReadString(option, "value");
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    options.Add(new QuestionOption(
                        value,
                        ReadString(option, "label") ?? value,
                        ReadString(option, "description")));
                }
            }

            return new QuestionState(
                formId,
                ReadString(question, "sessionId") ?? string.Empty,
                LogFormatter.Clean(ReadString(question, "title") ?? string.Empty),
                ReadString(question, "fieldKey") ?? string.Empty,
                kind,
                options,
                ReadString(question, "chosenValue"),
                status,
                ReadString(question, "notice"),
                ReadString(question, "kindLabel"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParse<T>(string? text, out T value) where T : struct, Enum =>
        Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed record Payload(string Type, StatePayload Question);

    private sealed record StatePayload(
        string FormId,
        string SessionId,
        string Title,
        string FieldKey,
        string Kind,
        OptionPayload[] Options,
        string? ChosenValue,
        string Status,
        string? Notice,
        string? KindLabel);

    private sealed record OptionPayload(string Value, string Label, string? Description);
}

/// <summary>Collapses repeated question states for one form into a single row in place.</summary>
public static class QuestionLines
{
    /// <summary>
    /// Keeps one entry per form id: the first position it occupied and the last state it
    /// reached. Answering a question therefore replaces its row instead of appending a second
    /// one, and a restored session that stored a pending and an answered line shows one answer.
    /// </summary>
    public static IReadOnlyList<LogEntry> Collapse(IReadOnlyList<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = new List<LogEntry>(entries.Count);
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Kind != LogKind.Question || entry.Question is not { } state)
            {
                result.Add(entry);
                continue;
            }

            if (positions.TryGetValue(state.FormId, out var index))
            {
                result[index] = entry;
            }
            else
            {
                positions[state.FormId] = result.Count;
                result.Add(entry);
            }
        }

        return result;
    }
}