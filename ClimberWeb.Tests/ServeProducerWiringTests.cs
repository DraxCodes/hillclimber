using System.Net;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The invariant this lane exists for: one serve child per run, shared by the producer and the
/// question channel. A second child would never see the forms the first one raises, because a
/// form is process-local to the server that made it.
/// </summary>
public class ServeProducerWiringTests
{
    private const string Password = "not-a-real-password";

    [Fact]
    public async Task ABorrowedServerIsNeverDisposedWithTheChannel()
    {
        var process = new FakeServeProcess();
        var child = new ServeChild("http://127.0.0.1:4099", Password, process);

        var channel = new QuestionChannel(new SilentWire(), borrowed: child, options: Fast());
        await channel.StartAsync("ses_1", ".", default);
        await channel.DisposeAsync();

        Assert.False(process.CloseInputCalled);
        Assert.False(process.KillCalled);
        Assert.Equal(0, process.DisposeCount);
    }

    [Fact]
    public async Task TheOffFlagStartsNoProcessAtAll()
    {
        var factory = new FakeServeFactory(new FakeServeProcess());
        var transport = new QuestionTransport(
            QuestionTransportMode.Off,
            new ServeSupervisor(factory, TimeSpan.FromMilliseconds(200), () => Password));

        var server = await transport.StartServerAsync(".", default);

        Assert.Null(server);
        Assert.Equal(0, factory.Starts);
    }

    [Fact]
    public async Task TheServeFlagStartsTheRunsOneServer()
    {
        var factory = new FakeServeFactory(new FakeServeProcess("""{"url":"http://127.0.0.1:4099"}"""));
        var transport = new QuestionTransport(
            QuestionTransportMode.Serve,
            new ServeSupervisor(factory, TimeSpan.FromMilliseconds(500), () => Password));

        var server = await transport.StartServerAsync(@"C:\work\repo", default);

        Assert.NotNull(server);
        Assert.Equal(1, factory.Starts);
        Assert.Equal(@"C:\work\repo", factory.WorkingDirectory);
        Assert.Equal("http://127.0.0.1:4099", server!.Url);
        await server.DisposeAsync();
    }

    private static QuestionChannelOptions Fast() => new()
    {
        IdleTimeout = TimeSpan.FromMinutes(10),
        InitialBackoff = TimeSpan.FromMilliseconds(1),
    };

    private sealed class SilentWire : IFormWire
    {
        public Task<IReadOnlyList<QuestionState>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<QuestionState>>(Array.Empty<QuestionState>());

        public Task<QuestionState?> GetAsync(string sessionId, string formId, CancellationToken cancellationToken = default)
            => Task.FromResult<QuestionState?>(null);

        public Task<QuestionSubmitOutcome> ReplyAsync(
            string sessionId,
            string formId,
            string fieldKey,
            QuestionKind kind,
            string value,
            CancellationToken cancellationToken = default)
            => Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public Task<QuestionSubmitOutcome> CancelAsync(
            string sessionId,
            string formId,
            string message,
            CancellationToken cancellationToken = default)
            => Task.FromResult(QuestionSubmitOutcome.Disconnected);

        public async Task WatchAsync(
            Func<SseFrame, Task> onFrame,
            CancellationToken cancellationToken,
            Action? onOpen = null)
        {
            onOpen?.Invoke();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class FakeServeFactory : IServeProcessFactory
    {
        private readonly IServeProcess _process;

        public FakeServeFactory(IServeProcess process) => _process = process;

        public int Starts { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public IServeProcess Start(string workingDirectory, IReadOnlyDictionary<string, string> environment)
        {
            Starts++;
            WorkingDirectory = workingDirectory;
            return _process;
        }
    }

    private sealed class FakeServeProcess : IServeProcess
    {
        private readonly TextReader _output;

        public FakeServeProcess(string output = "")
        {
            _output = new StringReader(output);
        }

        public TextWriter Input { get; } = new StringWriter();

        public TextReader Output => _output;

        public TextReader Error { get; } = new StringReader(string.Empty);

        public bool HasExited { get; private set; }

        public bool CloseInputCalled { get; private set; }

        public bool KillCalled { get; private set; }

        public int DisposeCount { get; private set; }

        public void CloseInput()
        {
            CloseInputCalled = true;
            HasExited = true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            HasExited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancellationToken);

        public void Kill()
        {
            KillCalled = true;
            HasExited = true;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}