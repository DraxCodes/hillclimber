using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClimberWeb.Services;

/// <summary>Everything the sessions rail renders about one stored session.</summary>
/// <param name="Id">The app-minted session id; also the <c>--session</c> id opencode continues.</param>
/// <param name="Title">The first prompt, truncated; never empty for a stored session.</param>
/// <param name="Method">Which workflow the session's last run used.</param>
/// <param name="Status">The last run's terminal status, or <see cref="RunStatus.Idle"/> before one.</param>
/// <param name="StartedAt">When the session's first run started.</param>
/// <param name="FinishedAt">When the session's last run finished, or null while live.</param>
/// <param name="UpdatedAt">When the session was last written to the store.</param>
/// <param name="SpendUsd">Total reported spend across every turn of the session.</param>
/// <param name="EstimatedSpendUsd">Token-based spend estimate, summed over every turn.</param>
/// <param name="TotalTokens">Prompt plus completion tokens, summed over every turn.</param>
public sealed record SessionSummary(
    string Id,
    string Title,
    RunMethod Method,
    RunStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset UpdatedAt,
    decimal SpendUsd,
    decimal EstimatedSpendUsd,
    long TotalTokens);

/// <summary>One answer option as it is kept on disk: display data only.</summary>
public sealed record StoredOption(string Value, string Label, string? Description = null);

/// <summary>
/// The display subset of one question row. Only what the transcript renders is kept: the
/// status is the one that was true when the row was written, and the run's session id is not
/// duplicated here because the file's own summary carries it. A restored row is inert
/// (see <see cref="QuestionRestore"/>), so it never needs either to answer again.
/// </summary>
/// <param name="FormId">The server's form id; the row's identity and the grouper's key.</param>
/// <param name="Status">The status the live row had when it was persisted.</param>
/// <param name="Title">The model's question, verbatim.</param>
/// <param name="FieldKey">The single field the reply would have addressed.</param>
/// <param name="Kind">Choice, boolean, or unsupported.</param>
/// <param name="Options">Every listed answer, in the server's order.</param>
/// <param name="ChosenValue">The option value that was sent, when one was.</param>
/// <param name="Notice">A display notice, when the row carried one.</param>
/// <param name="KindLabel">What an unsupported form required, when that is why it was inert.</param>
public sealed record StoredQuestion(
    string FormId,
    QuestionStatus Status,
    string Title,
    string FieldKey,
    QuestionKind Kind,
    IReadOnlyList<StoredOption> Options,
    string? ChosenValue = null,
    string? Notice = null,
    string? KindLabel = null)
{
    /// <summary>The persistable form of a display state; drops the session id and the flag.</summary>
    public static StoredQuestion From(QuestionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new StoredQuestion(
            state.FormId,
            state.Status,
            state.Title,
            state.FieldKey,
            state.Kind,
            state.Options.Select(option => new StoredOption(option.Value, option.Label, option.Description)).ToArray(),
            state.ChosenValue,
            state.Notice,
            state.KindLabel);
    }

    /// <summary>
    /// The display state, through the restore rule: pending and mid-submit rows read
    /// <c>no answer sent</c>, and every restored row is inert.
    /// </summary>
    public QuestionState ToState() => QuestionRestore.Apply(new QuestionState(
        FormId,
        string.Empty,
        Title,
        FieldKey,
        Kind,
        (Options ?? Array.Empty<StoredOption>())
            .Select(option => new QuestionOption(option.Value, option.Label, option.Description))
            .ToArray(),
        ChosenValue,
        Status,
        Notice,
        KindLabel));
}

/// <summary>
/// One persisted transcript row: the display <see cref="LogEntry"/> shape minus the raw
/// event line. The raw line is the full opencode JSON payload and never touches disk
/// (see <c>.scratch/security-pass/issues/01-tool-output-logging.md</c>); the fields kept
/// here are already truncated to the display limits <see cref="LogFormatter"/> applies.
/// </summary>
/// <param name="Timestamp">The clock label the renderer shows.</param>
/// <param name="Kind">Which row shape the grouper renders.</param>
/// <param name="Text">The display text, or the whole line for a plain marker.</param>
/// <param name="ToolInput">The tool call's arguments, already flattened and truncated.</param>
/// <param name="ToolOutput">The tool result, truncated to <see cref="MaxToolOutput"/>.</param>
/// <param name="Status">A tool call's progress state.</param>
/// <param name="ToolTitle">A tool call's distinguishing title, when it has one.</param>
/// <param name="Command">The shell command a tool call ran, when it ran one.</param>
/// <param name="ExitCode">The shell command's exit code, when it reported one.</param>
/// <param name="Question">A question row's display state; null for every other kind.</param>
public sealed record StoredEntry(
    string Timestamp,
    LogKind Kind,
    string Text,
    string? ToolInput = null,
    string? ToolOutput = null,
    StepStatus Status = StepStatus.None,
    string? ToolTitle = null,
    string? Command = null,
    int? ExitCode = null,
    StoredQuestion? Question = null)
{
    /// <summary>
    /// How much of a tool result goes to disk. The live view keeps the whole result so a
    /// reader can open it; a stored transcript only needs enough to recognise the call,
    /// and a file read or a command can otherwise put megabytes in one session file.
    /// </summary>
    public const int MaxToolOutput = 2000;

    /// <summary>
    /// The persisted form of a display entry. The raw line is dropped here and replaced
    /// with a stable synthetic key, so a restored transcript renders through the same
    /// grouper and presentation pipeline without keeping the payload. Plain lines are
    /// the exception: they are the climber's own markers and test summaries rather than
    /// opencode events, and the renderer prints <see cref="LogEntry.Raw"/> for them, so
    /// their text is kept verbatim. A question keeps only its display subset, never the
    /// transport's URL, password, header or raw frame.
    /// </summary>
    public static StoredEntry From(LogEntry entry) => new(
        entry.Timestamp,
        entry.Kind,
        entry.Kind == LogKind.Plain ? entry.Raw : entry.Text,
        entry.ToolInput,
        entry.ToolOutput is { } output ? Truncation.Cut(output, MaxToolOutput) : null,
        entry.Status,
        entry.ToolTitle,
        entry.Command,
        entry.ExitCode,
        entry.Question is { } question ? StoredQuestion.From(question) : null);

    /// <summary>
    /// The display form. <see cref="LogEntry.Raw"/> is a rendering key, not data: a
    /// synthetic line is rebuilt from the fields that were kept, so two identical
    /// restored rows still key apart by their position in the feed. A plain line keeps
    /// its text as the raw line, because that is what the renderer prints. A question is
    /// restored inert: a pending or mid-submit row can no longer be answered, so it reads
    /// <c>no answer sent</c> and offers no action.
    /// </summary>
    public LogEntry ToEntry(int index) => new(
        Kind == LogKind.Plain ? Text : $"[history:{index}]",
        Timestamp,
        Kind,
        Text,
        ToolInput,
        ToolOutput,
        Status,
        ToolTitle,
        Command,
        ExitCode,
        Question?.ToState());
}

/// <summary>A stored session: the rail summary plus what a resume needs.</summary>
/// <param name="Summary">The rail row and the totals every turn accumulates into.</param>
/// <param name="Entries">The persisted transcript, oldest first, display-truncated.</param>
/// <param name="TargetFolder">The folder the session's runs execute in.</param>
public sealed record SessionRecord(
    SessionSummary Summary,
    IReadOnlyList<StoredEntry> Entries,
    string TargetFolder)
{
    /// <summary>The session's id, forwarded from <see cref="Summary"/> for call sites that only key by it.</summary>
    [JsonIgnore]
    public string Id => Summary.Id;

    /// <summary>The rail title, forwarded from <see cref="Summary"/>.</summary>
    [JsonIgnore]
    public string Title => Summary.Title;
}

/// <summary>
/// The persistence seam for sessions. The UI and the coordinator talk to this interface
/// only, so fixture mode and tests can stay in memory and the filesystem stays behind one
/// adapter.
/// </summary>
public interface ISessionStore
{
    /// <summary>Every stored session, newest activity first. Never throws.</summary>
    IReadOnlyList<SessionSummary> List();

    /// <summary>One session with its transcript, or null when the id is unknown.</summary>
    SessionRecord? Load(string sessionId);

    /// <summary>Creates or replaces a session record. Safe to call repeatedly.</summary>
    void Save(SessionRecord record);

    /// <summary>Removes a session. A missing id is not an error.</summary>
    void Delete(string sessionId);
}

/// <summary>
/// A <see cref="ISessionStore"/> that keeps everything in memory. The adapter for tests
/// and for a playback host, whose runs are a display rather than real work and so never
/// persist.
/// </summary>
public sealed class MemorySessionStore : ISessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionRecord> _records = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<SessionSummary> List()
    {
        lock (_gate)
        {
            return _records.Values
                .Select(record => record.Summary)
                .OrderByDescending(summary => summary.UpdatedAt)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public SessionRecord? Load(string sessionId)
    {
        lock (_gate)
        {
            return _records.GetValueOrDefault(sessionId);
        }
    }

    /// <inheritdoc />
    public void Save(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _records[record.Id] = record;
        }
    }

    /// <inheritdoc />
    public void Delete(string sessionId)
    {
        lock (_gate)
        {
            _records.Remove(sessionId);
        }
    }
}

/// <summary>
/// A <see cref="ISessionStore"/> backed by one JSON file per session. Writes are atomic
/// (temp file, then rename) so a crash mid-write can never leave a half-written session;
/// reads are tolerant, so a corrupt or hand-edited file degrades to a missing session
/// rather than taking the rail down.
/// </summary>
public sealed class JsonSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // A session file is something a person may open and read; "Completed" beats 2.
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _folder;
    private readonly ILogger<JsonSessionStore>? _logger;

    /// <summary>Creates the store, creating <paramref name="folder"/> if it does not exist.</summary>
    public JsonSessionStore(string folder, ILogger<JsonSessionStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("A sessions folder is required.", nameof(folder));
        }

        _folder = folder;
        _logger = logger;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            // A store that cannot write is a degraded rail, not a dead app.
            _logger?.LogWarning(ex, "Cannot create the sessions folder {Folder}.", folder);
        }
    }

    /// <summary>Where the user's sessions live by default, under the roaming profile.</summary>
    public static string DefaultFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClimberWeb",
        "sessions");

    /// <summary>Resolves the configured folder, falling back to the default when unset.</summary>
    public static string ResolveFolder(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultFolder() : configured;

    private string PathFor(string sessionId) => Path.Combine(_folder, sessionId + ".json");

    /// <inheritdoc />
    public IReadOnlyList<SessionSummary> List()
    {
        var summaries = new List<SessionSummary>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
            {
                var record = TryRead(file);
                if (record is not null)
                {
                    summaries.Add(record.Summary);
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Cannot list sessions in {Folder}.", _folder);
        }

        return summaries.OrderByDescending(summary => summary.UpdatedAt).ToArray();
    }

    /// <inheritdoc />
    public SessionRecord? Load(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return TryRead(PathFor(sessionId));
    }

    /// <inheritdoc />
    public void Save(SessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var id = record.Id;
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return;
        }

        try
        {
            var target = PathFor(id);
            var temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(record, Options));
            // Replace, rather than delete-then-move: the session file is never absent, even
            // to a reader that opens it between the two writes.
            File.Move(temp, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.LogWarning(ex, "Cannot persist session {Id}.", id);
        }
    }

    /// <inheritdoc />
    public void Delete(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return;
        }

        try
        {
            File.Delete(PathFor(sessionId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Cannot delete session {Id}.", sessionId);
        }
    }

    /// <summary>
    /// Reads one file, swallowing anything that makes it unreadable. The file name is what
    /// <see cref="Load"/> and <see cref="Delete"/> address, so it wins over the copy of the id
    /// inside a hand-renamed file; missing collections from a hand-edited file degrade to
    /// empty ones rather than throwing on the first render.
    /// </summary>
    private SessionRecord? TryRead(string path)
    {
        try
        {
            if (JsonSerializer.Deserialize<SessionRecord>(File.ReadAllText(path), Options) is not { Summary: { } summary } record)
            {
                return null;
            }

            return record with
            {
                Summary = summary with { Id = Path.GetFileNameWithoutExtension(path) },
                Entries = record.Entries ?? Array.Empty<StoredEntry>(),
                TargetFolder = record.TargetFolder ?? string.Empty,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Cannot read session file {Path}.", path);
            return null;
        }
    }
}