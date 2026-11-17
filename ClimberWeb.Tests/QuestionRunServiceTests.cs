using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class QuestionRunServiceTests
{
    // -------------------------------------------------------------------- the lines

    [Fact]
    public async Task PendingThenAnsweredWritesExactlyTwoQuestionLines()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        factory.Channel.Emit(Choice(QuestionStatus.Pending));
        factory.Channel.Emit(Choice(QuestionStatus.Answered, "violet"));

        runner.Release.TrySetResult(true);
        await run;

        var lines = QuestionLinesIn(service);
        Assert.Equal(2, lines.Count);
        Assert.Equal(QuestionStatus.Pending, lines[0].Status);
        Assert.Equal(QuestionStatus.Answered, lines[1].Status);
        Assert.Equal("violet", lines[1].ChosenValue);
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task TheQuestionLineCarriesNoTransportData()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        factory.Channel.Emit(Choice(QuestionStatus.Pending));
        runner.Release.TrySetResult(true);
        await run;

        var logs = string.Join('\n', service.State.Logs);
        Assert.DoesNotContain("OPENCODE_PASSWORD", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", logs, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", logs, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ starting

    [Fact]
    public async Task AFailingChannelIsOneLineAndTheRunStillCompletes()
    {
        var factory = new FakeFactory { Channel = new FakeChannel { ThrowOnStart = true } };
        var (service, runner) = Create(factory);
        runner.Release.TrySetResult(true);

        await service.StartAsync(Options());

        Assert.Contains(
            service.State.Logs,
            line => line.Contains("[questions unavailable: the run's server did not start]", StringComparison.Ordinal));
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task ARunWithNoSessionGetsNoChannel()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);
        runner.Release.TrySetResult(true);

        await service.StartAsync(Options(sessionId: null));

        Assert.Equal(0, factory.Created);
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    [Fact]
    public async Task ADisabledFactoryIsNeverAskedForAChannel()
    {
        var factory = new FakeFactory { Enabled = false };
        var (service, runner) = Create(factory);
        runner.Release.TrySetResult(true);

        await service.StartAsync(Options());

        Assert.Equal(0, factory.Created);
    }

    [Fact]
    public async Task TheChannelIsDisposedWhenTheRunFinishes()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);
        runner.Release.TrySetResult(true);

        await service.StartAsync(Options());

        await factory.Channel.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TheChannelIsDisposedOnStop()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();
        await run;

        await factory.Channel.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RunStatus.Stopped, service.State.Status);
    }

    // ------------------------------------------------------------------ answering

    [Fact]
    public async Task AnswerRoutesToTheRunsChannel()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var outcome = await service.AnswerQuestionAsync("frm_1", "colour", "violet");

        Assert.Equal(QuestionSubmitOutcome.Accepted, outcome);
        Assert.Equal(("frm_1", "colour", "violet"), Assert.Single(factory.Channel.Submits));

        runner.Release.TrySetResult(true);
        await run;
    }

    [Fact]
    public async Task DismissAndCheckRouteToTheRunsChannel()
    {
        var factory = new FakeFactory();
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.DismissQuestionAsync("frm_1");
        await service.CheckQuestionAsync("frm_1");

        Assert.Equal(new[] { "frm_1" }, factory.Channel.Cancels);
        Assert.Equal(new[] { "frm_1" }, factory.Channel.Checks);

        runner.Release.TrySetResult(true);
        await run;
    }

    [Fact]
    public async Task AnsweringWithNoChannelIsAQuietDisconnected()
    {
        var (service, runner) = Create(factory: null);
        runner.Release.TrySetResult(true);
        await service.StartAsync(Options());

        Assert.Equal(QuestionSubmitOutcome.Disconnected, await service.AnswerQuestionAsync("frm_1", "colour", "violet"));
        Assert.Equal(QuestionSubmitOutcome.Disconnected, await service.DismissQuestionAsync("frm_1"));
        Assert.Equal(QuestionSubmitOutcome.Disconnected, await service.CheckQuestionAsync("frm_1"));
    }

    [Fact]
    public async Task AThrowingChannelCannotChangeTheRunStatus()
    {
        var channel = new FakeChannel { ThrowOnAnswer = true };
        var factory = new FakeFactory { Channel = channel };
        var (service, runner) = Create(factory);

        var run = service.StartAsync(Options());
        await runner.Running.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(QuestionSubmitOutcome.Disconnected, await service.AnswerQuestionAsync("frm_1", "colour", "violet"));
        Assert.Equal(RunStatus.Running, service.State.Status);

        runner.Release.TrySetResult(true);
        await run;
        Assert.Equal(RunStatus.Completed, service.State.Status);
    }

    // --------------------------------------------------------------------- helpers

    private static (RunService Service, GatedRunner Runner) Create(IQuestionChannelFactory? factory)
    {
        var runner = new GatedRunner();
        return (new RunService(runner, questions: factory), runner);
    }

    private static RunOptions Options(string? sessionId = "ses_1") =>
        new("C:\\work", "do the thing", SessionId: sessionId);

    private static List<QuestionState> QuestionLinesIn(RunService service)
    {
        var states = new List<QuestionState>();
        foreach (var line in service.State.Logs)
        {
            var entry = LogFormatter.Parse(line);
            if (entry.Kind == LogKind.Question && entry.Question is { } state)
            {
                states.Add(state);
            }
        }

        return states;
    }

    private static QuestionState Choice(QuestionStatus status, string? chosen = null) =>
        new(
            "frm_1",
            "ses_1",
            "Which colour?",
            "colour",
            QuestionKind.Choice,
            new[]
            {
                new QuestionOption("amber", "Amber"),
                new QuestionOption("violet", "Violet"),
            },
            chosen,
            status);

    /// <summary>A runner that holds the run open until the test releases it.</summary>
    private sealed class GatedRunner : ICommandRunner
    {
        public TaskCompletionSource<bool> Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CommandResult> RunAsync(
            CommandRequest request,
            Action<string>? onOutputLine,
            CancellationToken cancellationToken)
        {
            Running.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return CannedResults.OpenCode();
        }
    }

    private sealed class FakeFactory : IQuestionChannelFactory
    {
        public FakeChannel Channel { get; set; } = new();

        public bool Enabled { get; set; } = true;

        public int Created { get; private set; }

        public IQuestionChannel? Create()
        {
            Created++;
            return Channel;
        }
    }

    private sealed class FakeChannel : IQuestionChannel
    {
        public event Action<QuestionState>? Changed;

        public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ThrowOnStart { get; set; }

        public bool ThrowOnAnswer { get; set; }

        public List<(string FormId, string Key, string Value)> Submits { get; } = new();

        public List<string> Cancels { get; } = new();

        public List<string> Checks { get; } = new();

        public void Emit(QuestionState state) => Changed?.Invoke(state);

        public Task StartAsync(string sessionId, string targetFolder, CancellationToken cancellationToken)
        {
            if (ThrowOnStart)
            {
                throw new ServeStartException("the run's server did not start");
            }

            return Task.CompletedTask;
        }

        public Task<QuestionSubmitOutcome> SubmitAsync(string formId, string fieldKey, string value)
        {
            if (ThrowOnAnswer)
            {
                throw new InvalidOperationException("the channel exploded");
            }

            Submits.Add((formId, fieldKey, value));
            return Task.FromResult(QuestionSubmitOutcome.Accepted);
        }

        public Task<QuestionSubmitOutcome> CancelAsync(string formId)
        {
            Cancels.Add(formId);
            return Task.FromResult(QuestionSubmitOutcome.Accepted);
        }

        public Task<QuestionSubmitOutcome> RecheckAsync(string formId)
        {
            Checks.Add(formId);
            return Task.FromResult(QuestionSubmitOutcome.Accepted);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }
}