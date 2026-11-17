using ClimberWeb.Components.Ui.Ai;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The session lifecycle: minting, resuming, persisting terminal states, and the rule that
/// a live run keeps working while the page looks at older history.
/// </summary>
public class SessionCoordinatorTests
{
    private const string Folder = @"C:\work\repo";

    private static SessionCoordinator Create(
        out FakeRunService run,
        out FakeClimbService climb,
        out MemorySessionStore store)
    {
        run = new FakeRunService();
        climb = new FakeClimbService();
        store = new MemorySessionStore();
        return new SessionCoordinator(store, run, climb);
    }

    [Fact]
    public void Begin_MintsAnId_AndWritesNothingUntilTheRunReports()
    {
        using var coordinator = Create(out _, out _, out var store);

        var id = coordinator.Begin("explain the code", RunMethod.Run, Folder);

        Assert.StartsWith("ses_", id);
        Assert.Equal(id, coordinator.ActiveId);
        Assert.Equal(id, coordinator.Active?.Summary.Id);
        Assert.True(coordinator.Active?.Summary.Status == RunStatus.Running);
        Assert.Empty(store.List());
        Assert.Equal(
            new[] { "explain the code" },
            coordinator.StoredEntries().Select(entry => entry.Text));
    }

    [Fact]
    public void Begin_TitlesTheSessionByItsFirstPromptCutToTheRailWidth()
    {
        using var coordinator = Create(out _, out _, out _);
        var prompt = new string('a', 80);

        coordinator.Begin(prompt, RunMethod.Run, Folder);

        var title = coordinator.Active!.Summary.Title;
        Assert.Equal(48, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Begin_UsesOnlyTheFirstLineOfAPromptAsTheTitle()
    {
        using var coordinator = Create(out _, out _, out _);

        coordinator.Begin("fix the build\nand the tests", RunMethod.Run, Folder);

        Assert.Equal("fix the build", coordinator.Active!.Summary.Title);
    }

    [Fact]
    public void TerminalState_FlushesTheSessionWithItsPromptAndEachLogLineOnce()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var id = coordinator.Begin("explain", RunMethod.Run, Folder);

        run.Report(id, RunStatus.Running, "first", "second");
        run.Report(id, RunStatus.Running, "first", "second", "third");
        run.Report(id, RunStatus.Completed, "first", "second", "third");

        var record = store.Load(id);
        Assert.NotNull(record);
        Assert.Equal(RunStatus.Completed, record.Summary.Status);
        Assert.Equal(4, record.Entries.Count);
        Assert.Equal(LogKind.UserPrompt, record.Entries[0].Kind);
        Assert.Equal("explain", record.Entries[0].Text);
        Assert.Equal(new[] { "first", "second", "third" }, record.Entries.Skip(1).Select(entry => entry.Text));

        // The stored view after a run is the finished transcript, not that plus the feed again.
        Assert.Equal(4, coordinator.StoredEntries().Count);
        Assert.Null(coordinator.LiveId);
    }

    [Fact]
    public void Begin_OnTheActiveSession_ContinuesTheSameConversation()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");

        var second = coordinator.Begin("and the tests?", RunMethod.Run, Folder);
        run.Report(second, RunStatus.Completed, "second answer");

        Assert.Equal(first, second);
        var record = store.Load(first)!;
        Assert.Equal(
            new[] { "explain", "answer", "and the tests?", "second answer" },
            record.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void SpendAndTokensAccumulateAcrossTheSessionsTurns()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, spend: 0.10m, tokens: 1000, "answer");

        var second = coordinator.Begin("and the tests?", RunMethod.Run, Folder);
        // The running total is the session's: the previous turn plus this one so far.
        run.Report(second, RunStatus.Running, spend: 0.02m, tokens: 200, "working");

        Assert.Equal(0.12m, coordinator.Active!.Summary.SpendUsd);
        Assert.Equal(1200, coordinator.Active.Summary.TotalTokens);

        run.Report(second, RunStatus.Completed, spend: 0.05m, tokens: 500, "second answer");

        var record = store.Load(first)!;
        Assert.Equal(0.15m, record.Summary.SpendUsd);
        Assert.Equal(1500, record.Summary.TotalTokens);
    }

    [Fact]
    public void Abandon_PutsTheViewBack_AndLeavesNoTurnBehind()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(id, RunStatus.Completed, "answer");

        var rejected = coordinator.Begin("and the tests?", RunMethod.Run, Folder);
        coordinator.Abandon(rejected);

        Assert.Equal(id, coordinator.ActiveId);
        Assert.Equal(2, coordinator.Active!.Entries.Count);
        Assert.Equal(2, coordinator.StoredEntries().Count);
        Assert.Equal(2, store.Load(id)!.Entries.Count);
    }

    [Fact]
    public void Abandon_OnAFreshLaunch_LeavesNoSessionOnScreen()
    {
        using var coordinator = Create(out _, out _, out var store);

        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        coordinator.Abandon(id);

        Assert.Null(coordinator.ActiveId);
        Assert.Null(coordinator.Active);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Abandon_AfterTheRunReported_IsTooLate()
    {
        using var coordinator = Create(out var run, out _, out _);
        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(id, RunStatus.Running, "first");

        coordinator.Abandon(id);

        Assert.Equal(id, coordinator.ActiveId);
        Assert.Equal(id, coordinator.LiveId);

        // The prompt is the stored base; the live run's own line comes from the service.
        Assert.Single(coordinator.StoredEntries());
    }

    [Fact]
    public void Switch_ShowsStoredHistory_WhileTheLiveSessionKeepsPersisting()
    {
        using var coordinator = Create(out var run, out _, out var store);

        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");
        coordinator.Archive();

        var second = coordinator.Begin("build a page", RunMethod.Run, Folder);
        run.Report(second, RunStatus.Running, "working");

        Assert.True(coordinator.Switch(first));
        Assert.Equal(first, coordinator.ActiveId);
        Assert.True(coordinator.IsLive(second));
        Assert.False(coordinator.IsLive(first));

        // The background run finishes while the page is looking at the first session.
        run.Report(second, RunStatus.Completed, "working", "done");

        Assert.Equal(first, coordinator.ActiveId);
        Assert.Equal(RunStatus.Completed, coordinator.Active!.Summary.Status);
        Assert.Equal(2, coordinator.StoredEntries().Count);
        Assert.Equal(3, store.Load(second)!.Entries.Count);
        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void Switch_ReAttachesToTheLiveRecord_NotTheDebouncedStore()
    {
        using var coordinator = Create(out var run, out _, out _);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");
        coordinator.Archive();
        var second = coordinator.Begin("build a page", RunMethod.Run, Folder);

        // The live record holds a line the store has not been asked to write yet.
        run.Report(second, RunStatus.Running, "working");
        coordinator.Switch(first);

        Assert.True(coordinator.Switch(second));
        Assert.Equal(
            new[] { "build a page", "working" },
            coordinator.Active!.Entries.Select(entry => entry.Text));
        Assert.Single(coordinator.StoredEntries());
    }

    [Fact]
    public void Switch_FlushesTheOutgoingLiveRecord_SoTheStoreHoldsItsLatestFeed()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");
        coordinator.Archive();
        var second = coordinator.Begin("build a page", RunMethod.Run, Folder);

        // The debounce makes the store lag behind the in-memory live record.
        run.Report(second, RunStatus.Running, "working");
        coordinator.Switch(first);

        var stored = store.Load(second);
        Assert.NotNull(stored);
        Assert.Equal(
            new[] { "build a page", "working" },
            stored.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void List_ReportsAStoredRunningRowWithNoLiveRun_AsStopped()
    {
        var store = new MemorySessionStore();
        store.Save(new SessionRecord(
            new SessionSummary(
                "ses_stale",
                "left running by a crash",
                RunMethod.Run,
                RunStatus.Running,
                StartedAt: DateTimeOffset.Now,
                FinishedAt: null,
                UpdatedAt: DateTimeOffset.Now,
                SpendUsd: 0m,
                EstimatedSpendUsd: 0m,
                TotalTokens: 0),
            Array.Empty<StoredEntry>(),
            Folder));

        using var coordinator = new SessionCoordinator(store, new FakeRunService(), new FakeClimbService());

        Assert.Equal(RunStatus.Stopped, Assert.Single(coordinator.List()).Status);
    }

    [Fact]
    public void Archive_ShowsAFreshSession_AndTheOldOneStaysStored()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(id, RunStatus.Completed, "answer");

        coordinator.Archive();

        Assert.Null(coordinator.ActiveId);
        Assert.Null(coordinator.Active);
        Assert.Empty(coordinator.StoredEntries());
        Assert.Single(store.List());
        Assert.Equal(id, store.List()[0].Id);
    }

    [Fact]
    public void Delete_RefusesTheSessionOnScreenAndTheLiveOne()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");
        coordinator.Archive();
        var second = coordinator.Begin("build a page", RunMethod.Run, Folder);
        run.Report(second, RunStatus.Running, "working");

        // A live run would write its record straight back.
        Assert.False(coordinator.Delete(second));

        // The session on screen is not deletable either.
        coordinator.Switch(first);
        Assert.False(coordinator.Delete(first));
        Assert.False(coordinator.Delete(second));

        run.Report(second, RunStatus.Completed, "working", "done");

        Assert.True(coordinator.Delete(second));
        Assert.Single(store.List());
    }

    [Fact]
    public void Delete_RemovesAStoredSession()
    {
        using var coordinator = Create(out var run, out _, out var store);
        var first = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(first, RunStatus.Completed, "answer");
        coordinator.Archive();
        coordinator.Begin("build a page", RunMethod.Run, Folder);

        Assert.True(coordinator.Delete(first));
        Assert.Empty(store.List());
    }

    [Fact]
    public void Begin_RefusesToStealALiveRunFromAnotherSession()
    {
        using var coordinator = Create(out var run, out _, out _);
        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(id, RunStatus.Running, "working");
        coordinator.Archive();

        var error = Assert.Throws<InvalidOperationException>(() => coordinator.Begin("something else", RunMethod.Run, Folder));
        Assert.Contains("already live", error.Message);
        Assert.Equal(id, coordinator.LiveId);
    }

    [Fact]
    public void Changed_FiresForEveryLifecycleStep()
    {
        using var coordinator = Create(out var run, out _, out _);
        var changes = 0;
        coordinator.Changed += () => changes++;

        var id = coordinator.Begin("explain", RunMethod.Run, Folder);
        run.Report(id, RunStatus.Completed, "answer");
        coordinator.Archive();

        Assert.Equal(3, changes);
    }

    [Fact]
    public void ClimbsPersistTheirSpendAndEndOnAForbiddenTerminalStatus()
    {
        using var coordinator = Create(out _, out var climb, out var store);
        var id = coordinator.Begin("raise coverage", RunMethod.Hillclimb, Folder);

        climb.Report(id, RunStatus.Running, spend: 0.10m, tokens: 500, "iteration 1");
        climb.Report(id, RunStatus.Completed, spend: 0.30m, tokens: 1400, "iteration 1", "done");

        var record = store.Load(id)!;
        Assert.Equal(RunMethod.Hillclimb, record.Summary.Method);
        Assert.Equal(0.30m, record.Summary.SpendUsd);
        Assert.Equal(1400, record.Summary.TotalTokens);
        Assert.Equal(RunStatus.Completed, record.Summary.Status);
    }

    [Fact]
    public void ARestoredPendingQuestionIsOneClosedNonInteractiveRow()
    {
        using var coordinator = Create(out var run, out _, out _);
        var id = coordinator.Begin("ask me", RunMethod.Run, Folder);

        run.Report(id, RunStatus.Completed, QuestionLine.Write(Question(QuestionStatus.Pending)));

        var stored = coordinator.StoredEntries();
        var entry = Assert.Single(stored, row => row.Kind == LogKind.Question);
        Assert.True(entry.Question!.Restored);

        var item = Assert.Single(LogGrouper.Group(stored), grouped => grouped.Kind == OutputItemKind.Question);
        Assert.Equal(OutputItemKind.Question, item.Kind);

        var view = QuestionView.For(item.Entries[^1].Question!);
        Assert.False(view.Interactive);
        Assert.False(view.Busy);
        Assert.False(view.ShowPrefix);
        Assert.False(view.IsFold);
        Assert.False(view.AwaitsAnswer);
        Assert.Equal(QuestionView.Copy.Stale, view.StatusLine);
        Assert.Null(view.Hint);
        Assert.Null(view.Escape);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    [Fact]
    public void APendingAndAnsweredLineForOneFormRestoreAsOneAnsweredRow()
    {
        using var coordinator = Create(out var run, out _, out _);
        var id = coordinator.Begin("ask me", RunMethod.Run, Folder);
        var lines = new[]
        {
            QuestionLine.Write(Question(QuestionStatus.Pending)),
            QuestionLine.Write(Question(QuestionStatus.Answered, chosen: "violet")),
        };

        run.Report(id, RunStatus.Running, lines);
        run.Report(id, RunStatus.Completed, lines);

        var stored = coordinator.StoredEntries();

        // Both lines persist; the grouper is what keeps the transcript to one row per form.
        Assert.Equal(2, stored.Count(row => row.Kind == LogKind.Question));
        var item = Assert.Single(LogGrouper.Group(stored), grouped => grouped.Kind == OutputItemKind.Question);
        Assert.Equal(OutputItemKind.Question, item.Kind);

        var state = item.Entries[^1].Question!;
        Assert.True(state.Restored);
        Assert.Equal(QuestionStatus.Answered, state.Status);

        var view = QuestionView.For(state);
        Assert.True(view.IsFold);
        Assert.False(view.FoldOpenByDefault);
        Assert.Equal("answered: Violet", view.FoldedLabel);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    private static QuestionState Question(QuestionStatus status, string? chosen = null) =>
        new(
            "frm_1",
            "ses_1",
            "Which colour should the status band use?",
            "colour",
            QuestionKind.Choice,
            new[]
            {
                new QuestionOption("amber", "Amber", "the current accent"),
                new QuestionOption("violet", "Violet"),
                new QuestionOption("slate", "Slate"),
            },
            chosen,
            status);

    /// <summary>A run service the test drives by hand, one snapshot at a time.</summary>
    private sealed class FakeRunService : IRunService
    {
        public RunState State { get; private set; } = new();

        public event Action<RunState>? StateChanged;

        public bool IsRunning => State.Status is RunStatus.Running or RunStatus.AwaitingApproval;

        public Task StartAsync(RunOptions options) => Task.CompletedTask;

        public void Stop()
        {
        }

        public Task ApproveFallbackAsync() => Task.CompletedTask;

        public Task<QuestionSubmitOutcome> AnswerQuestionAsync(string formId, string fieldKey, string value) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public Task<QuestionSubmitOutcome> DismissQuestionAsync(string formId) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public Task<QuestionSubmitOutcome> CheckQuestionAsync(string formId) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public void Report(string sessionId, RunStatus status, params string[] logs) =>
            Report(new RunState { SessionId = sessionId, Status = status, Logs = logs });

        public void Report(string sessionId, RunStatus status, decimal spend, long tokens, params string[] logs) =>
            Report(new RunState
            {
                SessionId = sessionId,
                Status = status,
                Logs = logs,
                Metrics = new RunMetrics(spend, tokens / 2, tokens - (tokens / 2), 0, 0),
            });

        private void Report(RunState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }

    /// <summary>A climb service the test drives by hand, one snapshot at a time.</summary>
    private sealed class FakeClimbService : IHillClimberService
    {
        public ClimberState State { get; private set; } = new();

        public event Action<ClimberState>? StateChanged;

        public bool IsRunning => State.Status is RunStatus.Running or RunStatus.AwaitingApproval;

        public Task StartAsync(ClimberOptions options) => Task.CompletedTask;

        public void Stop()
        {
        }

        public void ApproveFallback()
        {
        }

        public void Report(string sessionId, RunStatus status, decimal spend, long tokens, params string[] logs)
        {
            State = new ClimberState
            {
                SessionId = sessionId,
                Status = status,
                Logs = logs,
                SpendUsd = spend,
                InputTokens = tokens / 2,
                OutputTokens = tokens - (tokens / 2),
            };
            StateChanged?.Invoke(State);
        }
    }
}