using System.Net;
using System.Text;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The serve producer against a scripted HTTP server and a scripted event stream: no process, no
/// tokens, and every request it makes is an assertion. The shapes come from the server's own
/// OpenAPI contract (v2.3.0), captured in `.scratch/interactive-questions/verification.md`.
/// </summary>
public class ServeRunRunnerTests
{
    private const string Session = "ses_2b9f4c1a";

    private static string Own(string json) => json.Replace("{session}", Session, StringComparison.Ordinal);

    private static readonly string Started =
        Own("""{"id":"e1","created":1,"type":"session.execution.started","data":{"sessionID":"{session}"}}""");

    private static readonly string TextEnded = Own(
        """{"id":"e2","created":2,"type":"session.text.ended","data":{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"All done."}}""");

    private static readonly string Succeeded =
        Own("""{"id":"e3","created":3,"type":"session.execution.succeeded","data":{"sessionID":"{session}"}}""");

    [Fact]
    public async Task AnAbsentSessionIsCreatedByItsOwnIdThenTheTurnIsAdmitted()
    {
        var handler = new ScriptedHandler(request => request.Method.Method switch
        {
            "GET" => Response(HttpStatusCode.NotFound),
            "POST" when request.RequestUri!.AbsolutePath == "/api/session" => Response(HttpStatusCode.OK),
            _ => Response(HttpStatusCode.OK),
        });
        var (runner, lines) = Build(handler, [Started, TextEnded, Succeeded]);

        var result = await runner.RunAsync(lines.Add, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(ServeTurnEnd.Succeeded, runner.End);

        var created = Assert.Single(handler.Calls, call => call.Path == "/api/session" && call.Method == "POST");
        Assert.Contains(Session, created.Body);
        Assert.Contains("climber", created.Body);

        var prompt = Assert.Single(handler.Calls, call => call.Path.EndsWith("/prompt", StringComparison.Ordinal));
        Assert.Contains("explain the code", prompt.Body);
        // The contract schedules execution unless `resume` is false, so it must be absent.
        Assert.DoesNotContain("resume", prompt.Body);

        // The mapped line is the one the renderer already knows.
        Assert.Contains(lines, line => LogFormatter.Parse(line).Text == "All done.");
        Assert.All(lines, line => Assert.DoesNotContain("http", line));
    }

    [Fact]
    public async Task AnExistingSessionIsReusedNotRecreated()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK));
        var (runner, _) = Build(handler, [Started, Succeeded]);

        await runner.RunAsync(null, CancellationToken.None);

        Assert.DoesNotContain(handler.Calls, call => call is { Method: "POST", Path: "/api/session" });
        Assert.Contains(handler.Calls, call => call.Method == "GET" && call.Path == $"/api/session/{Session}");
    }

    [Fact]
    public async Task AStopInterruptsTheTurnBeforeItEnds()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK));
        var (runner, _) = Build(handler, [Started], holdOpen: true);

        using var stop = new CancellationTokenSource();
        var run = runner.RunAsync(null, stop.Token);
        await WaitForAsync(() => handler.Calls.Any(call => call.Path.EndsWith("/prompt", StringComparison.Ordinal)));

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var interrupt = Assert.Single(handler.Calls, call => call.Path.Contains("interrupt", StringComparison.Ordinal));
        Assert.Contains("resume=false", interrupt.Path);
        Assert.Equal(ServeTurnEnd.Interrupted, runner.End);
    }

    [Fact]
    public async Task AStreamThatEndsWithoutATerminalFailsTheRun()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK));
        var (runner, lines) = Build(handler, events: [], holdOpen: false);

        var result = await runner.RunAsync(lines.Add, CancellationToken.None);

        Assert.Equal(ServeRunRunner.StreamClosedExitCode, result.ExitCode);
        Assert.Equal(ServeTurnEnd.Failed, runner.End);
        Assert.Contains(lines, line => line.Contains("closed the stream", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStreamThatNeverOpensFailsBeforeThePrompt()
    {
        var handler = new ScriptedHandler(_ => Response(HttpStatusCode.OK));
        var (runner, _) = Build(handler, events: [], open: false);

        await Assert.ThrowsAsync<ServeStartException>(() => runner.RunAsync(null, CancellationToken.None));
        Assert.DoesNotContain(handler.Calls, call => call.Path.EndsWith("/prompt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARefusedPromptNamesTheStatusAndOffersNoFallback()
    {
        var handler = new ScriptedHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/prompt", StringComparison.Ordinal)
                ? Response(HttpStatusCode.BadRequest)
                : Response(HttpStatusCode.OK));
        var (runner, _) = Build(handler, [Started]);

        var failure = await Assert.ThrowsAsync<ServeStartException>(() => runner.RunAsync(null, CancellationToken.None));
        Assert.Equal("the prompt was refused (400)", failure.Message);
    }

    private static (ServeRunRunner Runner, List<string> Lines) Build(
        ScriptedHandler handler,
        string[] events,
        bool holdOpen = false,
        bool open = true)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4099") };
        var wire = new ScriptedWire(events, holdOpen, open);
        var provider = new ProviderSelection("surplus", "Surplus", "surplus/deepseek-v4.1", SpendMode.Reported);
        var runner = new ServeRunRunner(
            client,
            wire,
            Session,
            "explain the code",
            "climber",
            provider,
            streamOpenTimeout: TimeSpan.FromMilliseconds(100));

        return (runner, new List<string>());
    }

    private static HttpResponseMessage Response(HttpStatusCode status) =>
        new(status) { Content = new StringContent("{\"data\":{}}", Encoding.UTF8, "application/json") };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("the runner did not reach the expected step");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<Call> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add(new Call(request.Method.Method, request.RequestUri!.PathAndQuery, body ?? string.Empty));
            return _respond(request);
        }
    }

    public sealed record Call(string Method, string Path, string Body);

    /// <summary>A wire whose frames are a fixed script; it opens only when told to.</summary>
    private sealed class ScriptedWire : IFormWire
    {
        private readonly string[] _events;
        private readonly bool _holdOpen;
        private readonly bool _open;

        public ScriptedWire(string[] events, bool holdOpen, bool open)
        {
            _events = events;
            _holdOpen = holdOpen;
            _open = open;
        }

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
            if (!_open)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }

            onOpen?.Invoke();

            foreach (var payload in _events)
            {
                await onFrame(new SseFrame(null, payload));
            }

            if (_holdOpen)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }
}