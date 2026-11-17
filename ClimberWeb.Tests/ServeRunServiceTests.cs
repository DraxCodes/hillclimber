using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The run loop's serve branch: the states a reader can actually reach when the child is the
/// producer. Every producer here is a stub, so these are cheap and deterministic; the real one is
/// covered by <see cref="ServeRunRunnerTests"/>.
/// </summary>
public class ServeRunServiceTests
{
    [Fact]
    public async Task AServerInterruptFailsTheRunAndNamesTheServer()
    {
        var service = new RunService(
            new NeverRunner(),
            questions: new Factory(),
            serveRunner: (_, _) => new StubRunner((_, _) => Task.FromException<CommandResult>(new ServeInterruptedException("shutdown"))));

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.Equal("the run was interrupted (shutdown)", service.State.Message);
        Assert.Contains(service.State.Logs, line => line.Contains("[serve: the run was interrupted (shutdown)]", StringComparison.Ordinal));
        Assert.False(service.State.FallbackAvailable);
        // A server-side interruption is not the reader's stop, and must not say it was.
        Assert.DoesNotContain(service.State.Logs, line => line.Contains("Stopped by user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServeFailureBandsTheServersOwnWordsNotAnExitCode()
    {
        var service = new RunService(
            new NeverRunner(),
            questions: new Factory(),
            serveRunner: (_, _) => new StubRunner((_, _) => new CommandResult(1, string.Empty, "quota exhausted")));

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Failed, service.State.Status);
        Assert.Equal("quota exhausted", service.State.Message);
        // No process exited, so no row may claim one did.
        Assert.DoesNotContain(service.State.Logs, line => line.Contains("opencode exited with code", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStoppedServeRunIsTheReadersStopAndStopsByUser()
    {
        var serve = new StubRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return new CommandResult(0, string.Empty, string.Empty);
        });
        var service = new RunService(new NeverRunner(), questions: new Factory(), serveRunner: (_, _) => serve);

        var run = service.StartAsync(Options());
        await serve.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();
        await run;

        Assert.Equal(RunStatus.Stopped, service.State.Status);
        Assert.Contains(service.State.Logs, line => line.Contains("Stopped by user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheServeChildIsDisposedOnceAfterTheChannel()
    {
        var process = new RecordingProcess();
        var factory = new Factory(new ServeChild("http://127.0.0.1:4099", "not-a-real-password", process));
        var channel = new RecordingChannel();
        factory.Channel = channel;

        var service = new RunService(
            new NeverRunner(),
            questions: factory,
            serveRunner: (_, _) => new StubRunner((_, _) => new CommandResult(0, string.Empty, string.Empty)));

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Equal(1, factory.Starts);
        Assert.Same(factory.Server, channel.StartedWith);
        Assert.True(channel.Disposed);
        Assert.Equal(1, process.DisposeCount);
        Assert.True(process.CloseInputCalled);
    }

    [Fact]
    public async Task WithTheFlagOffNoChildIsStartedAndNoServeRunnerIsBuilt()
    {
        var factory = new Factory { Enabled = false };
        var service = new RunService(
            new FixedRunner(new CommandResult(0, string.Empty, string.Empty)),
            questions: factory,
            serveRunner: (_, _) => throw new InvalidOperationException("the serve runner must not be built"));

        await service.StartAsync(Options());

        Assert.Equal(RunStatus.Completed, service.State.Status);
        Assert.Equal(0, factory.Starts);
    }

    private static RunOptions Options() => new("C:\\work", "do the thing", SessionId: "ses_1");

    /// <summary>A producer that is never expected to run.</summary>
    private sealed class NeverRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(
            CommandRequest request,
            Action<string>? onOutputLine,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the CLI runner must not be used in serve mode");
    }

    private sealed class FixedRunner : ICommandRunner
    {
        private readonly CommandResult _result;

        public FixedRunner(CommandResult result) => _result = result;

        public Task<CommandResult> RunAsync(
            CommandRequest request,
            Action<string>? onOutputLine,
            CancellationToken cancellationToken) =>
            Task.FromResult(_result);
    }

    private sealed class StubRunner : ICommandRunner
    {
        private readonly Func<Action<string>?, CancellationToken, Task<CommandResult>> _run;

        public StubRunner(Func<Action<string>?, CancellationToken, Task<CommandResult>> run) => _run = run;

        public StubRunner(Func<Action<string>?, CancellationToken, CommandResult> run)
            : this((emit, token) => Task.FromResult(run(emit, token)))
        {
        }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CommandResult> RunAsync(
            CommandRequest request,
            Action<string>? onOutputLine,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _run(onOutputLine, cancellationToken);
        }
    }

    private sealed class Factory : IQuestionChannelFactory
    {
        public Factory(ServeChild? server = null) =>
            Server = server ?? new ServeChild("http://127.0.0.1:4099", "not-a-real-password", new RecordingProcess());

        public bool Enabled { get; set; } = true;

        public RecordingChannel Channel { get; set; } = new();

        public ServeChild Server { get; }

        public int Starts { get; private set; }

        public IQuestionChannel? Create() => Channel;

        public Task<ServeChild?> StartServerAsync(string targetFolder, CancellationToken cancellationToken)
        {
            Starts++;
            return Task.FromResult<ServeChild?>(Enabled ? Server : null);
        }
    }

    private sealed class RecordingChannel : IQuestionChannel
    {
        public event Action<QuestionState>? Changed
        {
            add { }
            remove { }
        }

        public ServeChild? StartedWith { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartAsync(
            string sessionId,
            string targetFolder,
            CancellationToken cancellationToken,
            ServeChild? server = null)
        {
            StartedWith = server;
            return Task.CompletedTask;
        }

        public Task<QuestionSubmitOutcome> SubmitAsync(string formId, string fieldKey, string value) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public Task<QuestionSubmitOutcome> CancelAsync(string formId) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public Task<QuestionSubmitOutcome> RecheckAsync(string formId) =>
            Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingProcess : IServeProcess
    {
        public TextWriter Input { get; } = new StringWriter();

        public TextReader Output { get; } = new StringReader(string.Empty);

        public TextReader Error { get; } = new StringReader(string.Empty);

        public bool HasExited { get; private set; }

        public bool CloseInputCalled { get; private set; }

        public int DisposeCount { get; private set; }

        public void CloseInput()
        {
            CloseInputCalled = true;
            HasExited = true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            HasExited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancellationToken);

        public void Kill() => HasExited = true;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}