using System.Text.Json;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class QuestionChannelTests
{
    // -------------------------------------------------------------------- dispatch

    [Fact]
    public async Task APermissionEventProducesNothing()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("permission.asked", """
            {"id":"per_1","sessionID":"ses_1","permission":"bash","patterns":["rm"]}
            """));

        Assert.Empty(run.Events());
    }

    [Fact]
    public async Task AnUnknownEventProducesNothing()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("message.part.updated", """{"part":{"type":"text"}}"""));

        Assert.Empty(run.Events());
    }

    [Fact]
    public async Task ACreatedFormIsPublishedAsPending()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        var state = Assert.Single(run.Events());
        Assert.Equal(QuestionStatus.Pending, state.Status);
        Assert.Equal("Which colour?", state.Title);
        Assert.Equal(QuestionKind.Choice, state.Kind);
    }

    [Fact]
    public async Task ACreatedFormForAnotherSessionIsIgnored()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson(sessionId: "ses_other") + "}"));

        Assert.Empty(run.Events());
    }

    [Fact]
    public async Task ARepliedFormBecomesAnsweredWithTheServersValue()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));
        await run.Wire.Emit!(Event("form.replied", """{"id":"frm_1","sessionID":"ses_1","answer":{"colour":"violet"}}"""));

        var state = run.Events()[^1];
        Assert.Equal(QuestionStatus.Answered, state.Status);
        Assert.Equal("violet", state.ChosenValue);
    }

    [Fact]
    public async Task ACancelledFormBecomesCancelledAndCarriesNoNotice()
    {
        var run = await Started();
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));
        await run.Wire.Emit!(Event("form.cancelled", """{"id":"frm_1","sessionID":"ses_1"}"""));

        var state = run.Events()[^1];
        Assert.Equal(QuestionStatus.Cancelled, state.Status);
        Assert.Null(state.Notice);
    }

    // ------------------------------------------------------------------- reconcile

    [Fact]
    public async Task StartListsPendingFormsAsStale()
    {
        var run = await Started(Choice());
        await using var channel = run.Channel;

        var state = Assert.Single(run.Events());
        Assert.Equal(QuestionStatus.Stale, state.Status);
    }

    [Fact]
    public async Task StartListsAnUnsupportedFormWithItsNotice()
    {
        var unsupported = Choice() with
        {
            Kind = QuestionKind.Unsupported,
            Options = Array.Empty<QuestionOption>(),
            Status = QuestionStatus.Unsupported,
            KindLabel = "number answer required",
        };

        var run = await Started(unsupported);
        await using var channel = run.Channel;

        var state = Assert.Single(run.Events());
        Assert.Equal(QuestionStatus.Unsupported, state.Status);
        Assert.Equal("number answer required", state.KindLabel);
    }

    [Fact]
    public async Task AReconnectListsAnUnseenFormAsStale()
    {
        var run = await Started();
        await using var channel = run.Channel;

        // The stream closes, and the next list finds a form that was never shown.
        run.Wire.ListResult.Add(Choice());
        run.Wire.CloseStream();

        await WaitFor(() => run.Events().Any(state => state.Status == QuestionStatus.Stale));
        Assert.True(run.Wire.ListCalls >= 3);
    }

    [Fact]
    public async Task TheLiveStreamListsFormsCreatedBeforeItOpened()
    {
        // The list taken before the stream opened was empty. A form that appears in the list the
        // channel reads once the stream is live must still become a row, or a form the model
        // raised in that gap would wait for a reconnect.
        var run = await Started(configure: wire => wire.BeforeList = () =>
        {
            if (wire.ListCalls == 3)
            {
                wire.ListResult.Add(Choice());
            }
        });
        await using var channel = run.Channel;

        await WaitFor(() => run.Events().Any(state => state.Status == QuestionStatus.Stale));
        Assert.True(run.Wire.ListCalls >= 3);
    }

    [Fact]
    public async Task AFormAlreadyOnScreenKeepsItsStateAcrossAReconnect()
    {
        var run = await Started(Choice());
        await using var channel = run.Channel;

        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));
        Assert.Equal(QuestionStatus.Pending, run.Events()[^1].Status);

        run.Wire.CloseStream();
        await WaitFor(() => run.Wire.ListCalls >= 3);

        Assert.DoesNotContain(run.Events().Skip(1), state => state.Status == QuestionStatus.Stale);
    }

    [Fact]
    public async Task FiveFailuresInTheWindowDisableTheChannel()
    {
        var now = DateTimeOffset.UnixEpoch;
        var run = await Started(
            Choice(),
            clock: () => now,
            configure: wire =>
            {
                wire.FailOnWatch = true;
                wire.OnWatchFailed = () => now += TimeSpan.FromSeconds(2);
            });
        await using var channel = run.Channel;

        await WaitFor(() => run.Events().Any(state => state.Status == QuestionStatus.Unavailable));
        Assert.True(run.Wire.WatchCalls >= 5);
    }

    // --------------------------------------------------------------------- submit

    [Fact]
    public async Task SubmitSendsTheOptionValueAndFoldsToAnswered()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        var outcome = await channel.SubmitAsync("frm_1", "colour", "violet");

        Assert.Equal(QuestionSubmitOutcome.Accepted, outcome);
        var reply = Assert.Single(run.Wire.Replies);
        Assert.Equal(("frm_1", "colour", QuestionKind.Choice, "violet"), reply);

        var events = run.Events();
        Assert.Equal(QuestionStatus.Submitting, events[^2].Status);
        Assert.Equal(QuestionStatus.Answered, events[^1].Status);
        Assert.Equal("violet", events[^1].ChosenValue);
    }

    [Fact]
    public async Task SubmitRejectsAValueThatIsNotAnOption()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        var outcome = await channel.SubmitAsync("frm_1", "colour", "magenta");

        Assert.Equal(QuestionSubmitOutcome.InvalidAnswer, outcome);
        Assert.Empty(run.Wire.Replies);
    }

    [Fact]
    public async Task SubmitOnAnUnsupportedFormNeverReachesTheWire()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """
            {"form":{"id":"frm_1","sessionID":"ses_1","title":"How many?",
                     "fields":[{"key":"count","type":"number"}]}}
            """));

        var outcome = await channel.SubmitAsync("frm_1", "count", "3");

        Assert.Equal(QuestionSubmitOutcome.InvalidAnswer, outcome);
        Assert.Empty(run.Wire.Replies);
    }

    [Fact]
    public async Task A409IsRecheckedAndNeverLeftPending()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.ReplyOutcome = QuestionSubmitOutcome.AlreadySettled;
        run.Wire.GetResult = Choice() with { Status = QuestionStatus.Answered, ChosenValue = "amber" };

        Assert.Equal(QuestionSubmitOutcome.AlreadySettled, await channel.SubmitAsync("frm_1", "colour", "violet"));
        Assert.Equal(QuestionStatus.Answered, run.Events()[^1].Status);
        Assert.Equal("amber", run.Events()[^1].ChosenValue);
    }

    [Fact]
    public async Task A409WithAPendingFormIsReportedAsCancelled()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.ReplyOutcome = QuestionSubmitOutcome.AlreadySettled;
        run.Wire.GetResult = Choice() with { Status = QuestionStatus.Pending };

        await channel.SubmitAsync("frm_1", "colour", "violet");

        Assert.Equal(QuestionStatus.Cancelled, run.Events()[^1].Status);
    }

    [Fact]
    public async Task A400PublishesRefused()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.ReplyOutcome = QuestionSubmitOutcome.InvalidAnswer;

        Assert.Equal(QuestionSubmitOutcome.InvalidAnswer, await channel.SubmitAsync("frm_1", "colour", "violet"));
        Assert.Equal(QuestionStatus.Refused, run.Events()[^1].Status);
    }

    [Fact]
    public async Task AnUnconfirmedSubmitIsNotRetried()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.ReplyOutcome = QuestionSubmitOutcome.Unconfirmed;

        await channel.SubmitAsync("frm_1", "colour", "violet");

        Assert.Single(run.Wire.Replies);
        Assert.Equal(QuestionStatus.Unconfirmed, run.Events()[^1].Status);
    }

    [Fact]
    public async Task ASecondSubmitOnASettledFormIsRefusedLocally()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        await channel.SubmitAsync("frm_1", "colour", "violet");
        var second = await channel.SubmitAsync("frm_1", "colour", "amber");

        Assert.Equal(QuestionSubmitOutcome.AlreadySettled, second);
        Assert.Single(run.Wire.Replies);
    }

    // --------------------------------------------------------------------- dismiss

    [Fact]
    public async Task CancelSettlesToCancelledWithTheFixedMessage()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        Assert.Equal(QuestionSubmitOutcome.Accepted, await channel.CancelAsync("frm_1"));

        Assert.Equal("no answer sent", run.Wire.CancelMessage);
        Assert.Equal(QuestionStatus.Cancelled, run.Events()[^1].Status);
    }

    [Fact]
    public async Task CancelRetriesOnceThenKeepsThePendingRow()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.CancelOutcome = QuestionSubmitOutcome.Disconnected;

        Assert.Equal(QuestionSubmitOutcome.Disconnected, await channel.CancelAsync("frm_1"));
        Assert.Equal(2, run.Wire.CancelCalls);
        Assert.Equal(QuestionStatus.Pending, run.Events()[^1].Status);
    }

    [Fact]
    public async Task CancelThatSettlesElsewhereIsRechecked()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.CancelOutcome = QuestionSubmitOutcome.AlreadySettled;
        run.Wire.GetResult = Choice() with { Status = QuestionStatus.Answered, ChosenValue = "violet" };

        await channel.CancelAsync("frm_1");

        Assert.Equal(QuestionStatus.Answered, run.Events()[^1].Status);
    }

    // --------------------------------------------------------------------- recheck

    [Fact]
    public async Task RecheckPublishesWhatTheServerReports()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.GetResult = Choice() with { Status = QuestionStatus.Answered, ChosenValue = "amber" };

        Assert.Equal(QuestionSubmitOutcome.Accepted, await channel.RecheckAsync("frm_1"));
        Assert.Equal(QuestionStatus.Answered, run.Events()[^1].Status);
    }

    [Fact]
    public async Task RecheckOfAStillPendingFormIsUnconfirmed()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.GetResult = Choice();

        Assert.Equal(QuestionSubmitOutcome.Unconfirmed, await channel.RecheckAsync("frm_1"));
        Assert.Equal(QuestionStatus.Pending, run.Events()[^1].Status);
    }

    [Fact]
    public async Task RecheckOfAGoneFormIsStale()
    {
        var run = await Started();
        await using var channel = run.Channel;
        await run.Wire.Emit!(Event("form.created", """{"form":""" + ChoiceFormJson() + "}"));

        run.Wire.GetResult = null;

        Assert.Equal(QuestionSubmitOutcome.AlreadySettled, await channel.RecheckAsync("frm_1"));
        Assert.Equal(QuestionStatus.Stale, run.Events()[^1].Status);
    }

    // --------------------------------------------------------------------- helpers

    private sealed class Run
    {
        private readonly List<QuestionState> _events = new();

        public required QuestionChannel Channel { get; init; }

        public required FakeWire Wire { get; init; }

        public QuestionState[] Events()
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }

        public void Record(QuestionState state)
        {
            lock (_events)
            {
                _events.Add(state);
            }
        }
    }

    private static Task<Run> Started(
        QuestionState? listed = null,
        Func<DateTimeOffset>? clock = null,
        Action<FakeWire>? configure = null)
    {
        return StartedCore(listed, clock, configure);
    }

    private static async Task<Run> StartedCore(
        QuestionState? listed,
        Func<DateTimeOffset>? clock,
        Action<FakeWire>? configure)
    {
        var wire = new FakeWire();
        if (listed is not null)
        {
            wire.ListResult.Add(listed);
        }

        configure?.Invoke(wire);

        var channel = new QuestionChannel(
            wire,
            new QuestionChannelOptions { IdleTimeout = TimeSpan.FromMinutes(10) },
            (_, token) => Task.Delay(1, token),
            clock ?? (() => DateTimeOffset.UnixEpoch));

        var run = new Run { Channel = channel, Wire = wire };
        channel.Changed += run.Record;

        await channel.StartAsync("ses_1", ".", default);
        await wire.Watched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        return run;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The expected state did not arrive.");
            }

            await Task.Delay(10);
        }
    }

    private static QuestionState Choice(string formId = "frm_1", string sessionId = "ses_1") =>
        new(
            formId,
            sessionId,
            "Which colour?",
            "colour",
            QuestionKind.Choice,
            new[]
            {
                new QuestionOption("amber", "Amber"),
                new QuestionOption("violet", "Violet"),
            },
            null,
            QuestionStatus.Pending);

    private static string ChoiceFormJson(string formId = "frm_1", string sessionId = "ses_1") =>
        $$"""
        {"id":"{{formId}}","sessionID":"{{sessionId}}","title":"Which colour?",
         "fields":[{"key":"colour","type":"string","title":"Colour",
                    "options":[{"value":"amber","label":"Amber"},{"value":"violet","label":"Violet"}]}]}
        """;

    private static SseFrame Event(string type, string data) =>
        new(null, "{\"type\":" + JsonSerializer.Serialize(type) + ",\"data\":" + data + "}");

    private sealed class FakeWire : IFormWire
    {
        private int _watchCalls;
        private TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<QuestionState> ListResult { get; } = new();

        public int ListCalls { get; private set; }

        public int WatchCalls => Volatile.Read(ref _watchCalls);

        public int CancelCalls { get; private set; }

        public TaskCompletionSource<bool> Watched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<SseFrame, Task>? Emit { get; private set; }

        public bool FailOnWatch { get; set; }

        public Action? OnWatchFailed { get; set; }

        /// <summary>Runs inside every list call, after it has been counted.</summary>
        public Action? BeforeList { get; set; }

        public QuestionSubmitOutcome ReplyOutcome { get; set; } = QuestionSubmitOutcome.Accepted;

        public QuestionSubmitOutcome CancelOutcome { get; set; } = QuestionSubmitOutcome.Accepted;

        public QuestionState? GetResult { get; set; }

        public string? CancelMessage { get; private set; }

        public List<(string FormId, string Key, QuestionKind Kind, string Value)> Replies { get; } = new();

        /// <summary>Ends the current watch as if the server had closed the stream.</summary>
        public void CloseStream()
        {
            var release = _release;
            _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            release.TrySetResult(true);
        }

        public Task<IReadOnlyList<QuestionState>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ListCalls++;
            BeforeList?.Invoke();
            lock (ListResult)
            {
                return Task.FromResult<IReadOnlyList<QuestionState>>(ListResult.ToArray());
            }
        }

        public Task<QuestionState?> GetAsync(string sessionId, string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(GetResult);

        public Task<QuestionSubmitOutcome> ReplyAsync(
            string sessionId,
            string formId,
            string fieldKey,
            QuestionKind kind,
            string value,
            CancellationToken cancellationToken = default)
        {
            lock (Replies)
            {
                Replies.Add((formId, fieldKey, kind, value));
            }

            return Task.FromResult(ReplyOutcome);
        }

        public Task<QuestionSubmitOutcome> CancelAsync(
            string sessionId,
            string formId,
            string message,
            CancellationToken cancellationToken = default)
        {
            CancelCalls++;
            CancelMessage = message;
            return Task.FromResult(CancelOutcome);
        }

        public async Task WatchAsync(
            Func<SseFrame, Task> onFrame,
            CancellationToken cancellationToken,
            Action? onOpen = null)
        {
            Interlocked.Increment(ref _watchCalls);
            Emit = onFrame;
            Watched.TrySetResult(true);
            onOpen?.Invoke();

            if (FailOnWatch)
            {
                OnWatchFailed?.Invoke();
                throw new HttpRequestException("the stream is down");
            }

            var release = _release;
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The channel is done; end the stream.
            }
        }
    }
}