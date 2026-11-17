using System.Net;
using System.Text;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class OpenCodeServeTests
{
    private const string Password = "climber-test-credential";

    // ---------------------------------------------------------------- URL line

    [Fact]
    public void ReadsTheUrlFromTheAnnouncementLine()
    {
        Assert.Equal("http://127.0.0.1:4096", ServeSupervisor.ReadUrl("""{"url":"http://127.0.0.1:4096"}"""));
    }

    [Fact]
    public void ReadsTheUrlWhenTheLineCarriesAPrefix()
    {
        Assert.Equal(
            "http://127.0.0.1:51234",
            ServeSupervisor.ReadUrl("""opencode serve listening: {"url":"http://127.0.0.1:51234"}"""));
    }

    [Theory]
    [InlineData("opencode v2.3.0")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"url":null}""")]
    [InlineData("""{"url":""}""")]
    [InlineData("""{"url":"file:///tmp/socket"}""")]
    [InlineData("""{"url":"http://127.0.0.1:0"}""")]
    [InlineData("""{"url":}""")]
    public void IgnoresLinesThatAreNotAUsableUrl(string line)
    {
        Assert.Null(ServeSupervisor.ReadUrl(line));
    }

    // ------------------------------------------------------------------ startup

    [Fact]
    public async Task StartReturnsAChildWithTheReportedUrlAndAHostForLogs()
    {
        var process = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        var factory = new FakeServeFactory(process);

        await using var child = await Supervisor(factory).StartAsync(".", default);

        Assert.Equal("http://127.0.0.1:4096", child.Url);
        Assert.Equal("127.0.0.1:4096", child.Host);
        Assert.Equal("http://127.0.0.1:4096/", child.Client.BaseAddress?.ToString());
    }

    [Fact]
    public async Task StartPassesThePasswordOnlyInTheChildEnvironment()
    {
        var process = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        var factory = new FakeServeFactory(process);

        await using var child = await Supervisor(factory).StartAsync(".", default);

        Assert.NotNull(factory.Environment);
        Assert.Equal(Password, factory.Environment!["OPENCODE_PASSWORD"]);
        Assert.DoesNotContain(Password, factory.WorkingDirectory ?? string.Empty);
    }

    [Fact]
    public async Task StartSendsThePasswordAsBasicAuthForUserOpencode()
    {
        var process = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        var factory = new FakeServeFactory(process);

        await using var child = await Supervisor(factory).StartAsync(".", default);

        var header = child.Client.DefaultRequestHeaders.Authorization;
        Assert.NotNull(header);
        Assert.Equal("Basic", header!.Scheme);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!));
        Assert.Equal("opencode:" + Password, decoded);
    }

    [Fact]
    public async Task ANonLoopbackUrlIsRefusedAndTheChildIsStopped()
    {
        var process = new FakeServeProcess("""{"url":"http://10.0.0.5:4096"}""");
        var factory = new FakeServeFactory(process);

        var error = await Assert.ThrowsAsync<ServeStartException>(
            () => Supervisor(factory).StartAsync(".", default));

        Assert.Contains("loopback", error.Message);
        Assert.True(process.KillCalled);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task AStartupTimeoutStopsTheChild()
    {
        var process = new FakeServeProcess(block: true);
        var factory = new FakeServeFactory(process);

        var error = await Assert.ThrowsAsync<ServeStartException>(
            () => Supervisor(factory).StartAsync(".", default));

        Assert.Contains("did not start", error.Message);
        Assert.True(process.CloseInputCalled);
        Assert.True(process.KillCalled);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task AChildThatExitsBeforeTheUrlIsStopped()
    {
        var process = new FakeServeProcess(string.Empty);
        var factory = new FakeServeFactory(process);

        var error = await Assert.ThrowsAsync<ServeStartException>(
            () => Supervisor(factory).StartAsync(".", default));

        Assert.Contains("stopped before it was ready", error.Message);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task NoExceptionOrDisplayTextCarriesThePassword()
    {
        var process = new FakeServeProcess("""{"url":"http://10.0.0.5:4096"}""");
        var factory = new FakeServeFactory(process);

        // The URL is refused, so the exception is the only text produced by startup.
        var error = await Assert.ThrowsAsync<ServeStartException>(
            () => Supervisor(factory).StartAsync(".", default));

        Assert.DoesNotContain(Password, error.Message);
        Assert.DoesNotContain(Password, error.ToString());

        var healthy = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        await using var child = await Supervisor(new FakeServeFactory(healthy)).StartAsync(".", default);
        Assert.DoesNotContain(Password, child.ToString());
        Assert.DoesNotContain(Password, child.Url);
        Assert.DoesNotContain(Password, child.Host);
    }

    // ------------------------------------------------------------------ shutdown

    [Fact]
    public async Task DisposeClosesStdinAndDisposesTheProcess()
    {
        var process = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        var child = await Supervisor(new FakeServeFactory(process)).StartAsync(".", default);

        await child.DisposeAsync();

        Assert.True(process.CloseInputCalled);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var process = new FakeServeProcess("""{"url":"http://127.0.0.1:4096"}""");
        var child = await Supervisor(new FakeServeFactory(process)).StartAsync(".", default);

        await child.DisposeAsync();
        await child.DisposeAsync();

        Assert.Equal(1, process.DisposeCount);
    }

    // -------------------------------------------------------------------- live

    /// <summary>
    /// Starts a real <c>opencode serve --stdio</c> child, without a model call: the server must
    /// bind loopback, refuse an unauthenticated request and accept the generated password.
    /// Returns early when <c>opencode</c> is not on this machine's PATH.
    /// </summary>
    [Fact]
    [Trait("Live", "true")]
    public async Task LiveChildBindsLoopbackAndRequiresItsPassword()
    {
        if (ProcessCommandRunner.FindOnPath("opencode") is null)
        {
            return;
        }

        var supervisor = new ServeSupervisor(new ProcessServeFactory(), TimeSpan.FromSeconds(30));
        await using var child = await supervisor.StartAsync(Directory.GetCurrentDirectory(), default);

        using var anonymous = new HttpClient { BaseAddress = new Uri(child.Url), Timeout = TimeSpan.FromSeconds(20) };
        var refused = await anonymous.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var allowed = await child.Client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    // -------------------------------------------------------------------- doubles

    private static ServeSupervisor Supervisor(IServeProcessFactory factory) =>
        new(factory, TimeSpan.FromMilliseconds(200), () => Password);

    private sealed class FakeServeFactory : IServeProcessFactory
    {
        private readonly IServeProcess _process;

        public FakeServeFactory(IServeProcess process) => _process = process;

        public string? WorkingDirectory { get; private set; }

        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public IServeProcess Start(string workingDirectory, IReadOnlyDictionary<string, string> environment)
        {
            WorkingDirectory = workingDirectory;
            Environment = environment;
            return _process;
        }
    }

    private sealed class FakeServeProcess : IServeProcess
    {
        private readonly TextReader _output;

        public FakeServeProcess(string output = "", bool block = false)
        {
            _output = block ? new BlockingReader() : new StringReader(output);
        }

        public TextWriter Input { get; } = new StringWriter();

        public TextReader Output => _output;

        public TextReader Error { get; } = new StringReader(string.Empty);

        public bool HasExited { get; private set; }

        public bool CloseInputCalled { get; private set; }

        public bool KillCalled { get; private set; }

        public int DisposeCount { get; private set; }

        public bool Disposed => DisposeCount > 0;

        /// <summary>A <c>--stdio</c> child ends when its stdin closes; the double does too.</summary>
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

    private sealed class BlockingReader : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }
}