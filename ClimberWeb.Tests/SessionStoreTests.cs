using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The session store's two adapters: the filesystem one must round-trip a transcript,
/// survive a corrupt or hand-edited file, replace atomically and list newest first; the
/// transcript projection must keep the display shape and drop what may not reach disk.
/// </summary>
public class SessionStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "climber-session-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not a test failure.
        }
    }

    private JsonSessionStore Store() => new(_folder);

    private static SessionRecord Record(
        string id,
        string title = "explain the code",
        RunStatus status = RunStatus.Completed,
        DateTimeOffset? updatedAt = null,
        params StoredEntry[] entries) => new(
        new SessionSummary(
            id,
            title,
            RunMethod.Run,
            status,
            StartedAt: new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
            FinishedAt: new DateTimeOffset(2026, 11, 16, 10, 5, 0, TimeSpan.Zero),
            UpdatedAt: updatedAt ?? new DateTimeOffset(2026, 11, 16, 10, 5, 0, TimeSpan.Zero),
            SpendUsd: 0.25m,
            EstimatedSpendUsd: 0m,
            TotalTokens: 1200),
        entries.Length > 0
            ? entries
            : new[]
            {
                new StoredEntry("10:00:01", LogKind.UserPrompt, "explain the code"),
                new StoredEntry("10:00:02", LogKind.Tool, "shell", Command: "dotnet test", ExitCode: 0, Status: StepStatus.None),
                new StoredEntry("10:00:03", LogKind.Text, "The suite is green."),
            },
        @"C:\work\repo");

    [Fact]
    public void SaveThenLoad_RoundTripsTheTranscriptAndSummary()
    {
        var store = Store();
        store.Save(Record("ses_1"));

        var loaded = store.Load("ses_1");

        Assert.NotNull(loaded);
        Assert.Equal("ses_1", loaded.Id);
        Assert.Equal("explain the code", loaded.Title);
        Assert.Equal(RunMethod.Run, loaded.Summary.Method);
        Assert.Equal(RunStatus.Completed, loaded.Summary.Status);
        Assert.Equal(0.25m, loaded.Summary.SpendUsd);
        Assert.Equal(1200, loaded.Summary.TotalTokens);
        Assert.Equal(@"C:\work\repo", loaded.TargetFolder);
        Assert.Equal(
            new[] { LogKind.UserPrompt, LogKind.Tool, LogKind.Text },
            loaded.Entries.Select(entry => entry.Kind));
        Assert.Equal("dotnet test", loaded.Entries[1].Command);
    }

    [Fact]
    public void Save_ReplacesTheSameSessionInsteadOfAppending()
    {
        var store = Store();
        store.Save(Record("ses_1", title: "first title"));
        store.Save(Record("ses_1", title: "second title"));

        var summary = Assert.Single(store.List());
        Assert.Equal("second title", summary.Title);
        Assert.Single(Directory.GetFiles(_folder, "*.json"));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void Delete_RemovesTheSession_AndAMissingIdIsNotAnError()
    {
        var store = Store();
        store.Save(Record("ses_1"));

        store.Delete("ses_1");
        store.Delete("ses_1");
        store.Delete("ses_missing");

        Assert.Empty(store.List());
        Assert.Null(store.Load("ses_1"));
    }

    [Fact]
    public void List_OrdersNewestActivityFirst()
    {
        var store = Store();
        store.Save(Record("ses_old", title: "old", updatedAt: new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero)));
        store.Save(Record("ses_new", title: "new", updatedAt: new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero)));
        store.Save(Record("ses_mid", title: "mid", updatedAt: new DateTimeOffset(2026, 11, 15, 21, 0, 0, TimeSpan.Zero)));

        Assert.Equal(new[] { "ses_new", "ses_mid", "ses_old" }, store.List().Select(summary => summary.Id));
    }

    [Fact]
    public void List_SkipsACorruptFile_AndStillListsTheRest()
    {
        var store = Store();
        store.Save(Record("ses_good"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "ses_broken.json"), "{ this is not json");

        var summary = Assert.Single(store.List());
        Assert.Equal("ses_good", summary.Id);
        Assert.Null(store.Load("ses_broken"));
    }

    [Fact]
    public void Load_DegradesAHandEditedFileInsteadOfThrowing()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "ses_hand.json"), """
            {
              "Summary": {
                "Id": "ses_hand",
                "Title": "hand edited",
                "Method": "Hillclimb",
                "Status": "Stopped",
                "StartedAt": "2026-11-16T10:00:00+00:00",
                "FinishedAt": "2026-11-16T10:05:00+00:00",
                "UpdatedAt": "2026-11-16T10:05:00+00:00",
                "SpendUsd": 0.10,
                "EstimatedSpendUsd": 0,
                "TotalTokens": 10
              }
            }
            """);

        var loaded = Store().Load("ses_hand");

        Assert.NotNull(loaded);
        Assert.Equal("hand edited", loaded.Title);
        Assert.Empty(loaded.Entries);
        Assert.Equal(string.Empty, loaded.TargetFolder);
    }

    [Fact]
    public void Load_TrustsTheFileNameOverTheIdInsideTheFile()
    {
        var store = Store();
        store.Save(Record("ses_original"));
        File.Move(
            Path.Combine(_folder, "ses_original.json"),
            Path.Combine(_folder, "ses_renamed.json"));

        Assert.Equal("ses_renamed", Assert.Single(store.List()).Id);
        Assert.NotNull(store.Load("ses_renamed"));
        Assert.Null(store.Load("ses_original"));
    }

    [Fact]
    public void Load_RejectsAnIdThatIsNotAFileName()
    {
        var store = Store();

        Assert.Null(store.Load(@"..\escape"));
        Assert.Null(store.Load(string.Empty));
    }

    [Fact]
    public void DefaultFolder_IsUnderTheRoamingProfile()
    {
        var folder = JsonSessionStore.DefaultFolder();

        Assert.EndsWith(Path.Combine("ClimberWeb", "sessions"), folder);
        Assert.Equal(folder, JsonSessionStore.ResolveFolder(configured: null));
        Assert.Equal(folder, JsonSessionStore.ResolveFolder("  "));
        Assert.Equal(@"D:\sessions", JsonSessionStore.ResolveFolder(@"D:\sessions"));
    }

    [Fact]
    public void From_DropsTheRawEventLineButKeepsTheDisplayText()
    {
        var entry = new LogEntry(
            """[10:00:00] {"type":"tool_use","part":{"tool":"shell","state":{"output":"ok"}}}""",
            "10:00:00",
            LogKind.Tool,
            "shell",
            ToolInput: "command=dotnet test",
            ToolOutput: "ok",
            Status: StepStatus.None,
            Command: "dotnet test");

        var stored = StoredEntry.From(entry);

        Assert.Equal("shell", stored.Text);
        Assert.DoesNotContain("tool_use", stored.Text);
        Assert.Equal("dotnet test", stored.Command);

        // The restored row keys by position, never by the payload that was dropped.
        var restored = stored.ToEntry(7);
        Assert.Equal("[history:7]", restored.Raw);
        Assert.Equal("shell", restored.Text);
    }

    [Fact]
    public void From_KeepsAPlainLineVerbatim_BecauseTheRendererPrintsItRaw()
    {
        var entry = new LogEntry("[10:00:00] ===== Iteration 1/5 =====", "10:00:00", LogKind.Plain, "[10:00:00] ===== Iteration 1/5 =====");

        var restored = StoredEntry.From(entry).ToEntry(0);

        Assert.Equal("[10:00:00] ===== Iteration 1/5 =====", restored.Raw);
    }

    [Fact]
    public void From_CapsToolOutputSoOneSessionFileStaysSmall()
    {
        var entry = new LogEntry("raw", "10:00:00", LogKind.Tool, "read", ToolOutput: new string('x', StoredEntry.MaxToolOutput + 500));

        var stored = StoredEntry.From(entry);

        Assert.NotNull(stored.ToolOutput);
        Assert.Equal(StoredEntry.MaxToolOutput + 1, stored.ToolOutput!.Length);
        Assert.EndsWith("…", stored.ToolOutput);
    }

    [Fact]
    public void From_LeavesAShortToolOutputAlone()
    {
        var entry = new LogEntry("raw", "10:00:00", LogKind.Tool, "read", ToolOutput: "short");

        Assert.Equal("short", StoredEntry.From(entry).ToolOutput);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAQuestionRowThroughTheDisplayShape()
    {
        var state = Question(QuestionStatus.Answered, chosen: "violet");
        var stored = StoredEntry.From(new LogEntry(
            "raw",
            "10:00:03",
            LogKind.Question,
            state.Title,
            Question: state));
        var store = Store();
        store.Save(Record("ses_question", entries: new[] { stored }));

        var loaded = Assert.Single(store.Load("ses_question")!.Entries);
        Assert.Equal(LogKind.Question, loaded.Kind);
        Assert.Equal("Which colour should the status band use?", loaded.Text);

        var question = loaded.Question;
        Assert.NotNull(question);
        Assert.Equal("frm_1", question!.FormId);
        Assert.Equal(QuestionStatus.Answered, question.Status);
        Assert.Equal(QuestionKind.Choice, question.Kind);
        Assert.Equal("colour", question.FieldKey);
        Assert.Equal("violet", question.ChosenValue);
        Assert.Equal(new[] { "Amber", "Violet", "Slate" }, question.Options.Select(option => option.Label));
        Assert.Equal("the current accent", question.Options[0].Description);

        var restored = loaded.ToEntry(0).Question;
        Assert.NotNull(restored);
        Assert.True(restored!.Restored);
        Assert.Equal(QuestionStatus.Answered, restored.Status);
        Assert.Equal("violet", restored.ChosenValue);
        Assert.Equal(string.Empty, restored.SessionId);
    }

    [Theory]
    [InlineData(QuestionStatus.Pending)]
    [InlineData(QuestionStatus.Submitting)]
    [InlineData(QuestionStatus.Stale)]
    public void ToEntry_RestoresAnUnsettledQuestionAsNoAnswerSent(QuestionStatus status)
    {
        var stored = StoredEntry.From(new LogEntry(
            "raw",
            "10:00:03",
            LogKind.Question,
            "Which colour should the status band use?",
            Question: Question(status)));

        var restored = stored.ToEntry(3).Question;

        Assert.NotNull(restored);
        Assert.True(restored!.Restored);
        Assert.Equal(QuestionStatus.Stale, restored.Status);
        Assert.Null(restored.ChosenValue);
    }

    [Fact]
    public void ToEntry_KeepsAnUnsupportedRowsNoticeAndKindLabel()
    {
        var state = Question(
            QuestionStatus.Unsupported,
            kind: QuestionKind.Unsupported,
            kindLabel: "several answers required") with
        {
            Notice = "the form asked for more than one answer",
        };
        var stored = StoredEntry.From(new LogEntry("raw", "10:00:03", LogKind.Question, state.Title, Question: state));

        var restored = stored.ToEntry(0).Question;

        Assert.NotNull(restored);
        Assert.Equal("the form asked for more than one answer", restored!.Notice);
        Assert.Equal("several answers required", restored.KindLabel);
    }

    [Fact]
    public void Load_ReadsAnEntryWrittenBeforeQuestionsWerePersisted()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "ses_legacy.json"), """
            {
              "Summary": {
                "Id": "ses_legacy",
                "Title": "older session",
                "Method": "Run",
                "Status": "Completed",
                "StartedAt": "2026-11-15T10:00:00+00:00",
                "FinishedAt": "2026-11-15T10:05:00+00:00",
                "UpdatedAt": "2026-11-15T10:05:00+00:00",
                "SpendUsd": 0.10,
                "EstimatedSpendUsd": 0,
                "TotalTokens": 10
              },
              "Entries": [
                {
                  "Timestamp": "10:00:01",
                  "Kind": "Text",
                  "Text": "The suite is green.",
                  "Status": "None"
                }
              ],
              "TargetFolder": "C:\\work\\repo"
            }
            """);

        var loaded = Store().Load("ses_legacy");

        Assert.NotNull(loaded);
        var entry = Assert.Single(loaded!.Entries);
        Assert.Equal("The suite is green.", entry.Text);
        Assert.Null(entry.Question);
        Assert.Null(entry.ToEntry(0).Question);
    }

    [Fact]
    public void AStoredQuestionWritesOnlyDisplayData()
    {
        var state = Question(QuestionStatus.Answered, chosen: "violet");
        Store().Save(Record(
            "ses_question",
            entries: new[]
            {
                StoredEntry.From(new LogEntry("raw", "10:00:03", LogKind.Question, state.Title, Question: state)),
            }));

        var json = File.ReadAllText(Path.Combine(_folder, "ses_question.json"));

        Assert.Contains("\"Question\"", json);
        Assert.Contains("\"ChosenValue\": \"violet\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SessionId", json, StringComparison.OrdinalIgnoreCase);
    }

    private static QuestionState Question(
        QuestionStatus status,
        string? chosen = null,
        QuestionKind kind = QuestionKind.Choice,
        string? kindLabel = null) =>
        new(
            "frm_1",
            "ses_1",
            "Which colour should the status band use?",
            "colour",
            kind,
            kind == QuestionKind.Unsupported
                ? Array.Empty<QuestionOption>()
                : new[]
                {
                    new QuestionOption("amber", "Amber", "the current accent"),
                    new QuestionOption("violet", "Violet"),
                    new QuestionOption("slate", "Slate"),
                },
            chosen,
            status,
            null,
            kindLabel);
}