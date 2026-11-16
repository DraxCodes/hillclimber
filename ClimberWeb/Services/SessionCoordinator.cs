namespace ClimberWeb.Services;

/// <summary>
/// Owns the durable conversation lifecycle: which session is active, what its transcript
/// is, and how runs bind to opencode's <c>--session</c>. The rail and the home page talk
/// to this module only; the run services keep their one-live-run-per-process model and
/// carry the session id the coordinator mints.
/// </summary>
/// <remarks>
/// Exactly one run may be live at a time (the services enforce that). Switching the
/// viewed session while a run is live does not stop it: the live session keeps updating
/// here and in the store, its rail row keeps its live status, and switching back
/// re-attaches to the freshest record. The coordinator subscribes to both run services
/// itself, so a background run persists even when no page is rendered.
/// </remarks>
public sealed class SessionCoordinator : IDisposable
{
    private readonly ISessionStore _store;
    private readonly IRunService _run;
    private readonly IHillClimberService _climber;
    private readonly ILogger<SessionCoordinator>? _logger;
    private readonly object _gate = new();
    private readonly LogFormatter _formatter = new();

    /// <summary>The session the page is viewing, or null for a fresh, unrun one.</summary>
    private string? _activeId;

    private SessionRecord? _active;

    /// <summary>The session a live run belongs to, or null when nothing is live.</summary>
    private string? _liveId;

    private SessionRecord? _live;

    /// <summary>The live session's entries from before the current run started.</summary>
    private IReadOnlyList<StoredEntry> _liveBase = Array.Empty<StoredEntry>();

    /// <summary>
    /// The totals the live session had before the current run started. Every run reports
    /// only its own spend and tokens, so the session totals are these plus the run's.
    /// </summary>
    private decimal _liveSpendBase;
    private decimal _liveEstimatedBase;
    private long _liveTokensBase;

    /// <summary>
    /// The live run's entries formatted so far. A run re-raises its whole feed on every
    /// line, so formatting is appended incrementally instead of re-parsed per event.
    /// </summary>
    private readonly List<StoredEntry> _liveEntries = new();

    /// <summary>The last live line already formatted, to detect a reset feed.</summary>
    private string? _liveLastLine;

    /// <summary>
    /// The session <see cref="Begin"/> bound, before its run has reported any state. It is
    /// promoted to the live binding by the first state change, and dropped by
    /// <see cref="Abandon"/> when the launch was rejected.
    /// </summary>
    private PendingBind? _pending;

    /// <summary>The store listing, rebuilt only after something writes to the store.</summary>
    private IReadOnlyList<SessionSummary>? _list;

    private DateTimeOffset _lastFlush;

    /// <summary>Raised whenever the active session or the stored list changes.</summary>
    public event Action? Changed;

    public SessionCoordinator(
        ISessionStore store,
        IRunService run,
        IHillClimberService climber,
        ILogger<SessionCoordinator>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _climber = climber ?? throw new ArgumentNullException(nameof(climber));
        _logger = logger;
        _run.StateChanged += OnRunStateChanged;
        _climber.StateChanged += OnClimbStateChanged;
    }

    /// <summary>The session the page is currently showing, or null when it shows a fresh one.</summary>
    public SessionRecord? Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>The id the next run binds to, or null when the next run starts a new session.</summary>
    public string? ActiveId
    {
        get
        {
            lock (_gate)
            {
                return _activeId;
            }
        }
    }

    /// <summary>The session a live run belongs to, or null when nothing is live.</summary>
    public string? LiveId
    {
        get
        {
            lock (_gate)
            {
                return _liveId;
            }
        }
    }

    /// <summary>Every stored session, newest activity first. Never throws.</summary>
    public IReadOnlyList<SessionSummary> List()
    {
        lock (_gate)
        {
            return _list ??= StoredList();
        }
    }

    /// <summary>
    /// The store's listing with one correction: a run cannot outlive the process, so a stored
    /// <see cref="RunStatus.Running"/> row with no live binding here is what a crash left
    /// behind. It reads as stopped rather than pulsing for a run that is not happening.
    /// </summary>
    private IReadOnlyList<SessionSummary> StoredList() => _store.List()
        .Select(summary => summary.Status == RunStatus.Running && summary.Id != _liveId
            ? summary with { Status = RunStatus.Stopped }
            : summary)
        .ToArray();

    /// <summary>True when a run is live and belongs to <paramref name="sessionId"/>.</summary>
    public bool IsLive(string sessionId)
    {
        lock (_gate)
        {
            return _liveId == sessionId;
        }
    }

    /// <summary>
    /// Binds a run that is about to start to a session, continuing the active one when
    /// there is one and minting a fresh id otherwise. The returned id is also the
    /// <c>--session</c> value opencode continues, so it never changes for a session.
    /// The record stays in memory until the run's first state change: a rejected launch
    /// leaves no row in the store, and <see cref="Abandon"/> puts the view back.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A run is live in a different session, so the next start must be refused rather than
    /// silently stealing that run's binding.
    /// </exception>
    public string Begin(string prompt, RunMethod method, string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("A prompt is required to begin a session.", nameof(prompt));
        }

        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            throw new ArgumentException("A target folder is required to begin a session.", nameof(targetFolder));
        }

        var now = DateTimeOffset.Now;
        var promptEntry = new StoredEntry(now.ToString("HH:mm:ss"), LogKind.UserPrompt, prompt);

        string id;
        SessionRecord record;
        IReadOnlyList<StoredEntry> liveBase;

        lock (_gate)
        {
            if (_liveId is { } liveId && liveId != _activeId)
            {
                _logger?.LogWarning("Refused to bind a session while {LiveId} is live.", liveId);
                throw new InvalidOperationException(
                    "A run is already live in another session. Stop it before starting one here.");
            }

            if (_active is { } active && _activeId is { } activeId)
            {
                // A follow-up turn in the active session: the conversation continues, so
                // the id is reused and the prompt joins the transcript as a new turn. The
                // session totals carry over; the run's own numbers are added to them.
                id = activeId;
                var summary = active.Summary with
                {
                    Method = method,
                    Status = RunStatus.Running,
                    FinishedAt = null,
                    UpdatedAt = now,
                };
                liveBase = active.Entries.Append(promptEntry).ToArray();
                record = new SessionRecord(summary, liveBase, targetFolder);
            }
            else
            {
                // A fresh session: the first prompt is its title and its first turn.
                id = "ses_" + Guid.NewGuid().ToString("N");
                var summary = new SessionSummary(
                    id,
                    TitleFor(prompt),
                    method,
                    RunStatus.Running,
                    StartedAt: now,
                    FinishedAt: null,
                    UpdatedAt: now,
                    SpendUsd: 0m,
                    EstimatedSpendUsd: 0m,
                    TotalTokens: 0);
                liveBase = new[] { promptEntry };
                record = new SessionRecord(summary, liveBase, targetFolder);
            }

            _pending = new PendingBind(id, record, liveBase, _activeId, _active);
            _activeId = id;
            _active = record;
            _lastFlush = now;
        }

        RaiseChanged();
        return id;
    }

    /// <summary>
    /// Undoes the most recent <see cref="Begin"/> when the run it bound never started, so a
    /// rejected launch leaves neither a phantom session on screen nor an extra turn in a
    /// real one. Does nothing once the run has reported any state.
    /// </summary>
    public void Abandon(string sessionId)
    {
        lock (_gate)
        {
            if (_pending is not { } pending || pending.Id != sessionId)
            {
                return;
            }

            _pending = null;
            if (_activeId == sessionId)
            {
                _activeId = pending.PreviousActiveId;
                _active = pending.PreviousActive;
            }
        }

        RaiseChanged();
    }

    /// <summary>
    /// Switches the page to a stored session: its transcript becomes the active view and
    /// the next run continues its conversation. A live run is untouched; its session keeps
    /// updating in the background, and switching back re-attaches to the freshest record.
    /// The outgoing live session is flushed first, so a restart right after a switch cannot
    /// lose the last second of its feed.
    /// </summary>
    public bool Switch(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        SessionRecord? live;
        lock (_gate)
        {
            if (_activeId == sessionId)
            {
                return true;
            }

            live = _live;

            // The live record is fresher than the debounced store; prefer it.
            if (live is { } running && running.Id == sessionId)
            {
                _activeId = sessionId;
                _active = running;
            }
            else
            {
                live = null;
            }
        }

        if (live is not null)
        {
            Persist(live);
            RaiseChanged();
            return true;
        }

        // The outgoing live session keeps running, but the store should already hold the
        // second of feed the debounce has not written yet.
        PersistLive();

        var loaded = _store.Load(sessionId);
        if (loaded is null)
        {
            // The flush may have changed the listing even though the target is gone.
            RaiseChanged();
            return false;
        }

        lock (_gate)
        {
            _activeId = sessionId;
            _active = loaded;
        }

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Archives the current session: it stays in the rail from the store, but the page
    /// shows a fresh, unrun session, and the next run mints a new id. A live run is
    /// untouched and keeps updating in the background. The live record is flushed first so
    /// the row is in the store the moment it leaves the page.
    /// </summary>
    public void Archive()
    {
        lock (_gate)
        {
            if (_activeId is null)
            {
                return;
            }

            _activeId = null;
            _active = null;
        }

        PersistLive();
        RaiseChanged();
    }

    /// <summary>
    /// Removes a session from the store. The session on screen and the one a run is live in
    /// cannot be deleted: a live run would write its record straight back, and the page
    /// would keep showing a transcript the rail no longer lists.
    /// </summary>
    public bool Delete(string sessionId)
    {
        lock (_gate)
        {
            if (sessionId == _activeId || sessionId == _liveId)
            {
                return false;
            }
        }

        _store.Delete(sessionId);
        InvalidateList();

        RaiseChanged();
        return true;
    }

    /// <summary>
    /// The display entries for the active session's stored transcript: the persisted rows
    /// restored through the same <see cref="LogEntry"/> shape the live feed uses, with the
    /// reader's prompts already stored as <see cref="LogKind.UserPrompt"/> rows so history
    /// reads as a conversation. The current run's live entries are appended by the page,
    /// which renders them from the run service directly.
    /// </summary>
    public IReadOnlyList<LogEntry> StoredEntries()
    {
        lock (_gate)
        {
            if (_active is not { } record)
            {
                return Array.Empty<LogEntry>();
            }

            // While the bound run is live, its own entries are the page's to render from
            // the service; the stored part is everything before it, prompt included.
            var count = _liveId == record.Id ? _liveBase.Count : record.Entries.Count;
            return record.Entries
                .Take(count)
                .Select((stored, index) => stored.ToEntry(index))
                .ToArray();
        }
    }

    private void OnRunStateChanged(RunState state)
    {
        if (state.SessionId is not { } id)
        {
            return;
        }

        Record(new SessionUpdate(
            id,
            state.Logs,
            state.Status,
            state.Metrics.SpendUsd,
            state.Metrics.EstimatedSpendUsd,
            state.Metrics.TotalTokens,
            state.FinishedAt));
    }

    private void OnClimbStateChanged(ClimberState state)
    {
        if (state.SessionId is not { } id)
        {
            return;
        }

        // A climb reports its own total and no finish time, so a terminal state stamps now.
        Record(new SessionUpdate(
            id,
            state.Logs,
            state.Status,
            state.SpendUsd,
            state.EstimatedSpendUsd,
            state.InputTokens + state.OutputTokens,
            state.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Stopped
                ? DateTimeOffset.Now
                : null));
    }

    /// <summary>
    /// Records a state change of the live session's run: appends the run's display entries
    /// to the session's stored base and updates the summary, adding the run's spend and
    /// tokens to the session's running totals. Flushes to the store at most once per second
    /// while live, and always on a terminal state, which also clears the live binding.
    /// </summary>
    private void Record(SessionUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update.Logs);

        SessionRecord? toFlush = null;
        lock (_gate)
        {
            if (_pending is { } pending && pending.Id == update.SessionId)
            {
                // The bound run has started: promote the pending bind to the live one. Its
                // summary still holds the session totals from before this run.
                _pending = null;
                _liveId = update.SessionId;
                _live = pending.Record;
                _liveBase = pending.LiveBase;
                _liveSpendBase = pending.Record.Summary.SpendUsd;
                _liveEstimatedBase = pending.Record.Summary.EstimatedSpendUsd;
                _liveTokensBase = pending.Record.Summary.TotalTokens;
                _liveEntries.Clear();
                _liveLastLine = null;
            }

            if (_live is not { } live || live.Id != update.SessionId)
            {
                return;
            }

            AppendLive(update.Logs);
            var terminal = update.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Stopped;
            var summary = live.Summary with
            {
                Status = update.Status,
                FinishedAt = update.FinishedAt,
                UpdatedAt = DateTimeOffset.Now,
                SpendUsd = _liveSpendBase + update.Spend,
                EstimatedSpendUsd = _liveEstimatedBase + update.EstimatedSpend,
                TotalTokens = _liveTokensBase + update.Tokens,
            };

            var allEntries = _liveBase.Concat(_liveEntries).ToArray();
            var updated = new SessionRecord(summary, allEntries, live.TargetFolder);
            _live = terminal ? null : updated;
            if (_activeId == update.SessionId)
            {
                _active = updated;
            }

            if (terminal)
            {
                _liveId = null;
                toFlush = updated;
                _lastFlush = DateTimeOffset.Now;
            }
            else if (DateTimeOffset.Now - _lastFlush >= TimeSpan.FromSeconds(1))
            {
                toFlush = updated;
                _lastFlush = DateTimeOffset.Now;
            }
        }

        if (toFlush is not null)
        {
            Persist(toFlush);
        }

        RaiseChanged();
    }

    /// <summary>Writes the live record, if any, so a switch or archive leaves the store current.</summary>
    private void PersistLive()
    {
        SessionRecord? live;
        lock (_gate)
        {
            live = _live;
        }

        if (live is not null)
        {
            Persist(live);
        }
    }

    /// <summary>
    /// Saves a record and drops the cached listing after the write, so a reader never caches
    /// the list from just before the session it is about to render.
    /// </summary>
    private void Persist(SessionRecord record)
    {
        _store.Save(record);
        InvalidateList();
    }

    private void InvalidateList()
    {
        lock (_gate)
        {
            _list = null;
        }
    }

    /// <summary>
    /// Brings <see cref="_liveEntries"/> up to date with the run's feed. The feed only grows
    /// (it is cleared for a new run, and trimmed once it is long), so the tail is formatted
    /// and the rest is kept; a shrink or a moved last line means the feed was reset and the
    /// whole list is rebuilt.
    /// </summary>
    private void AppendLive(IReadOnlyList<string> logs)
    {
        var reset = logs.Count < _liveEntries.Count
            || (_liveEntries.Count > 0 && logs[_liveEntries.Count - 1] != _liveLastLine);
        if (reset)
        {
            _liveEntries.Clear();
        }

        for (var index = _liveEntries.Count; index < logs.Count; index++)
        {
            _liveEntries.Add(StoredEntry.From(_formatter.Format(logs[index])));
        }

        _liveLastLine = logs.Count > 0 ? logs[^1] : null;
    }

    /// <summary>The first line of the prompt, cut to the rail's title width.</summary>
    private static string TitleFor(string prompt) =>
        Truncation.Cut(prompt.Split('\n')[0].Trim(), 47);

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _run.StateChanged -= OnRunStateChanged;
        _climber.StateChanged -= OnClimbStateChanged;
    }

    /// <summary>One run's state change, in the shape the session's totals are updated from.</summary>
    /// <param name="SessionId">The session the change belongs to.</param>
    /// <param name="Logs">The run's whole feed, oldest first.</param>
    /// <param name="Status">The run's lifecycle state.</param>
    /// <param name="Spend">Exact cost this run has accumulated.</param>
    /// <param name="EstimatedSpend">Token-based estimate this run has accumulated.</param>
    /// <param name="Tokens">Prompt plus completion tokens this run has accumulated.</param>
    /// <param name="FinishedAt">When the run finished, or null while it is live.</param>
    private sealed record SessionUpdate(
        string SessionId,
        IReadOnlyList<string> Logs,
        RunStatus Status,
        decimal Spend,
        decimal EstimatedSpend,
        long Tokens,
        DateTimeOffset? FinishedAt);

    /// <summary>A bound session whose run has not reported state yet.</summary>
    /// <param name="Id">The id the launch was bound to.</param>
    /// <param name="Record">The record the first state change should adopt.</param>
    /// <param name="LiveBase">The entries the run's own feed is appended to.</param>
    /// <param name="PreviousActiveId">What the page was showing before the launch.</param>
    /// <param name="PreviousActive">The record the page was showing before the launch.</param>
    private sealed record PendingBind(
        string Id,
        SessionRecord Record,
        IReadOnlyList<StoredEntry> LiveBase,
        string? PreviousActiveId,
        SessionRecord? PreviousActive);
}