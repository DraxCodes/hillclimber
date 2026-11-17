using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClimberWeb.Services;

/// <summary>The run's question server could not be started. The message is safe to log.</summary>
public sealed class ServeStartException : Exception
{
    public ServeStartException(string message, bool notFound = false, Exception? inner = null)
        : base(message, inner)
    {
        NotFound = notFound;
    }

    /// <summary>True when the <c>opencode</c> executable itself was not on <c>PATH</c>.</summary>
    public bool NotFound { get; }
}

/// <summary>
/// A spawned <c>opencode serve</c> process. Abstracted so the supervisor's URL line, timeout
/// and kill behaviour can be tested without a real child.
/// </summary>
public interface IServeProcess : IAsyncDisposable
{
    TextWriter Input { get; }

    TextReader Output { get; }

    TextReader Error { get; }

    bool HasExited { get; }

    /// <summary>Closes stdin. For a <c>--stdio</c> child this ends its lifetime.</summary>
    void CloseInput();

    Task WaitForExitAsync(CancellationToken cancellationToken);

    void Kill();
}

/// <summary>Starts the process behind an <see cref="IServeProcess"/>.</summary>
public interface IServeProcessFactory
{
    /// <summary>
    /// Starts <c>opencode serve --stdio --port 0</c> in <paramref name="workingDirectory"/>.
    /// <paramref name="environment"/> is added to the child's environment, never the parent's.
    /// </summary>
    IServeProcess Start(string workingDirectory, IReadOnlyDictionary<string, string> environment);
}

/// <summary>The default process factory: a real <c>opencode</c> child.</summary>
public sealed class ProcessServeFactory : IServeProcessFactory
{
    public IServeProcess Start(string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(environment);

        var executable = ProcessCommandRunner.FindOnPath("opencode");
        if (executable is null)
        {
            throw new ServeStartException("opencode not found — is it installed and on PATH?", notFound: true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("--stdio");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("0");

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new ServeStartException("the run's server did not start");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new ServeStartException("the run's server did not start", inner: exception);
        }

        return new ProcessServe(process);
    }

    private sealed class ProcessServe : IServeProcess
    {
        private readonly Process _process;

        public ProcessServe(Process process) => _process = process;

        public TextWriter Input => _process.StandardInput;

        public TextReader Output => _process.StandardOutput;

        public TextReader Error => _process.StandardError;

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public void CloseInput()
        {
            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // The child may already be gone.
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            catch (NotSupportedException)
            {
                try
                {
                    _process.Kill();
                }
                catch
                {
                    // Ignore.
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A started question server: the loopback URL it reported, an authenticated client, and the
/// handle that ends its life. The password is reachable only from the client's header and is
/// never part of <see cref="ToString"/> or any exception text.
/// </summary>
public sealed class ServeChild : IAsyncDisposable
{
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);

    private readonly IServeProcess _process;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _drain = new();
    private int _disposed;

    internal ServeChild(string url, string password, IServeProcess process)
    {
        Url = url;
        _process = process;

        var uri = new Uri(url);
        Host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

        _client = new HttpClient
        {
            BaseAddress = uri,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));

        // A child that writes more than the URL line must not block on a full pipe.
        _ = DrainAsync(process.Output, _drain.Token);
        _ = DrainAsync(process.Error, _drain.Token);
    }

    /// <summary>The loopback URL the child reported, e.g. <c>http://127.0.0.1:4096</c>.</summary>
    public string Url { get; }

    /// <summary>The URL's host and port, for a quiet log line.</summary>
    public string Host { get; }

    /// <summary>An authenticated client rooted at <see cref="Url"/>. Owned by this child.</summary>
    public HttpClient Client => _client;

    /// <summary>Never contains the password.</summary>
    public override string ToString() => $"serve --stdio ({Host})";

    /// <summary>Closes stdin, waits briefly, then kills the tree. Never throws.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _drain.Cancel();
        _process.CloseInput();

        using (var grace = new CancellationTokenSource(ExitGrace))
        {
            try
            {
                await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _process.Kill();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                // Already gone.
            }
        }

        _process.Kill();
        _client.Dispose();
        await _process.DisposeAsync().ConfigureAwait(false);
        _drain.Dispose();
    }

    private static async Task DrainAsync(TextReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested
                   && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // The pipe closed with the child.
        }
    }
}

/// <summary>
/// Starts <c>opencode serve --stdio --port 0</c>, reads the one URL line from its stdout and
/// returns an authenticated child. The per-run password is generated here, passed only in the
/// child's environment, and held only in the client's header.
/// </summary>
public sealed class ServeSupervisor
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);

    private readonly IServeProcessFactory _factory;
    private readonly TimeSpan _startupTimeout;
    private readonly Func<string> _password;

    public ServeSupervisor(
        IServeProcessFactory factory,
        TimeSpan? startupTimeout = null,
        Func<string>? passwordFactory = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _startupTimeout = startupTimeout ?? DefaultStartupTimeout;
        _password = passwordFactory ?? (() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>
    /// Starts a child and waits up to the startup timeout for its URL line. On any failure the
    /// child is killed before the exception leaves; the message never contains the password.
    /// </summary>
    public async Task<ServeChild> StartAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var password = _password();
        IServeProcess? process = null;
        try
        {
            process = _factory.Start(
                workingDirectory,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["OPENCODE_PASSWORD"] = password });

            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startup.CancelAfter(_startupTimeout);

            string? url = null;
            while (url is null)
            {
                string? line;
                try
                {
                    line = await process.Output.ReadLineAsync(startup.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ServeStartException("the run's server did not start");
                }

                if (line is null)
                {
                    throw new ServeStartException("the run's server stopped before it was ready");
                }

                url = ReadUrl(line);
            }

            var uri = new Uri(url);
            if (!IsLoopback(uri))
            {
                throw new ServeStartException("the run's server did not bind loopback");
            }

            // Hand the child's output to the drain in the constructor; stop reading here.
            return new ServeChild(url, password, process);
        }
        catch (ServeStartException)
        {
            await StopAsync(process).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await StopAsync(process).ConfigureAwait(false);
            throw new ServeStartException("the run's server did not start", inner: exception);
        }
    }

    /// <summary>The URL from one stdout line, or null when the line is not the URL announcement.</summary>
    internal static string? ReadUrl(string line)
    {
        var start = line.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line[start..]);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("url", out var value)
                || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { Length: > 0 } text
                || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.Port <= 0)
            {
                return null;
            }

            return text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

    private static async Task StopAsync(IServeProcess? process)
    {
        if (process is null)
        {
            return;
        }

        process.CloseInput();
        process.Kill();
        await process.DisposeAsync().ConfigureAwait(false);
    }
}