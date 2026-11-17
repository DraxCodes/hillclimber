using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class OpenCodeFormClientTests
{
    // ------------------------------------------------------------------ the answer

    [Fact]
    public void AChoiceSendsTheOptionValueAsAString()
    {
        Assert.Equal(
            """{"answer":{"colour":"amber"}}""",
            OpenCodeFormClient.BuildAnswer(QuestionKind.Choice, "colour", "amber"));
    }

    [Theory]
    [InlineData("true", """{"answer":{"ok":true}}""")]
    [InlineData("false", """{"answer":{"ok":false}}""")]
    public void ABooleanSendsJsonTrueOrFalse(string value, string expected)
    {
        Assert.Equal(expected, OpenCodeFormClient.BuildAnswer(QuestionKind.Boolean, "ok", value));
    }

    [Fact]
    public void AnUnsupportedShapeCannotBeTurnedIntoAnAnswer()
    {
        Assert.Throws<InvalidOperationException>(
            () => OpenCodeFormClient.BuildAnswer(QuestionKind.Unsupported, "n", "1"));
    }

    [Fact]
    public void ABooleanWithANonBooleanValueIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => OpenCodeFormClient.BuildAnswer(QuestionKind.Boolean, "ok", "yes"));
    }

    [Theory]
    [InlineData("", "value")]
    [InlineData("key", "")]
    public void AnAnswerNeedsBothAKeyAndAValue(string key, string value)
    {
        Assert.Throws<ArgumentException>(
            () => OpenCodeFormClient.BuildAnswer(QuestionKind.Choice, key, value));
    }

    // ------------------------------------------------------------------ outcomes

    [Theory]
    [InlineData(200, QuestionSubmitOutcome.Accepted)]
    [InlineData(201, QuestionSubmitOutcome.Accepted)]
    [InlineData(204, QuestionSubmitOutcome.Accepted)]
    [InlineData(404, QuestionSubmitOutcome.AlreadySettled)]
    [InlineData(409, QuestionSubmitOutcome.AlreadySettled)]
    [InlineData(400, QuestionSubmitOutcome.InvalidAnswer)]
    [InlineData(422, QuestionSubmitOutcome.InvalidAnswer)]
    [InlineData(401, QuestionSubmitOutcome.Disconnected)]
    [InlineData(500, QuestionSubmitOutcome.Disconnected)]
    [InlineData(503, QuestionSubmitOutcome.Disconnected)]
    public void StatusCodesMapToOutcomes(int status, QuestionSubmitOutcome expected)
    {
        Assert.Equal(expected, OpenCodeFormClient.MapStatus((HttpStatusCode)status));
    }

    [Fact]
    public async Task ReplyMapsTheServersStatus()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));

        var outcome = await client.ReplyAsync("ses_1", "frm_1", "colour", QuestionKind.Choice, "amber");

        Assert.Equal(QuestionSubmitOutcome.AlreadySettled, outcome);
    }

    [Fact]
    public async Task AServerErrorLeavesTheSubmitDisconnected()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var outcome = await client.ReplyAsync("ses_1", "frm_1", "colour", QuestionKind.Choice, "amber");

        Assert.Equal(QuestionSubmitOutcome.Disconnected, outcome);
    }

    [Fact]
    public async Task ARefusedConnectionLeavesTheSubmitDisconnected()
    {
        var client = Client((_, _) => throw new HttpRequestException("connection refused"));

        var outcome = await client.ReplyAsync("ses_1", "frm_1", "colour", QuestionKind.Choice, "amber");

        Assert.Equal(QuestionSubmitOutcome.Disconnected, outcome);
    }

    [Fact]
    public async Task ASubmitPastItsBudgetIsUnconfirmed()
    {
        var client = Client(
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            },
            submitTimeout: TimeSpan.FromMilliseconds(40));

        var outcome = await client.ReplyAsync("ses_1", "frm_1", "colour", QuestionKind.Choice, "amber");

        Assert.Equal(QuestionSubmitOutcome.Unconfirmed, outcome);
    }

    [Fact]
    public async Task ACallerCancellationIsNotReportedAsAnOutcome()
    {
        var client = Client(
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            });
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ReplyAsync("ses_1", "frm_1", "colour", QuestionKind.Choice, "amber", cancelled.Token));
    }

    // -------------------------------------------------------------------- requests

    [Fact]
    public async Task CancelSendsTheMessageAsAQueryValue()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var client = new OpenCodeFormClient(new HttpClient(handler) { BaseAddress = Address() });

        var outcome = await client.CancelAsync("ses 1", "frm/1", "no answer sent");

        Assert.Equal(QuestionSubmitOutcome.Accepted, outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/api/session/ses%201/form/frm%2F1?message=no%20answer%20sent", request.PathAndQuery);
    }

    [Fact]
    public async Task ReplyPostsTheBuiltAnswer()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var client = new OpenCodeFormClient(new HttpClient(handler) { BaseAddress = Address() });

        await client.ReplyAsync("ses_1", "frm_1", "ok", QuestionKind.Boolean, "true");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/session/ses_1/form/frm_1/reply", request.PathAndQuery);
        Assert.Equal("""{"answer":{"ok":true}}""", request.Body);
    }

    // --------------------------------------------------------------------- parsing

    [Fact]
    public void ListReadsFormsFromTheDataEnvelope()
    {
        var states = OpenCodeFormClient.ParseForms("""
            {"data":[
              {"id":"frm_1","sessionID":"ses_1","title":"Which colour?",
               "fields":[{"key":"colour","type":"string","title":"Colour",
                          "options":[{"value":"amber","label":"Amber"},{"value":"violet","label":"Violet"}]}]}
            ]}
            """);

        var state = Assert.Single(states);
        Assert.Equal("frm_1", state.FormId);
        Assert.Equal("Which colour?", state.Title);
        Assert.Equal(QuestionKind.Choice, state.Kind);
        Assert.Equal(QuestionStatus.Pending, state.Status);
        Assert.Equal(new[] { "amber", "violet" }, state.Options.Select(option => option.Value));
    }

    [Fact]
    public void ListAlsoReadsABareArray()
    {
        var states = OpenCodeFormClient.ParseForms("""
            [{"id":"frm_1","sessionID":"ses_1","fields":[{"key":"ok","type":"boolean"}]}]
            """);

        Assert.Single(states);
        Assert.Equal(QuestionKind.Boolean, states[0].Kind);
    }

    [Fact]
    public void AnEmptyListIsEmpty()
    {
        Assert.Empty(OpenCodeFormClient.ParseForms("""{"data":[]}"""));
        Assert.Empty(OpenCodeFormClient.ParseForms(""));
    }

    [Theory]
    [InlineData("answered", QuestionStatus.Answered, "violet")]
    [InlineData("cancelled", QuestionStatus.Cancelled, null)]
    [InlineData("pending", QuestionStatus.Pending, null)]
    [InlineData("something-new", QuestionStatus.Stale, null)]
    public void AFormStateOnTheWireBecomesARowState(string wire, QuestionStatus status, string? chosen)
    {
        var body = """
            {"data":[{"id":"frm_1","sessionID":"ses_1",
              "fields":[{"key":"colour","type":"string","options":[{"value":"violet","label":"Violet"}]}],
              "state":{"status":"WIRE","answer":{"colour":"violet"}}}]}
            """.Replace("WIRE", wire);

        var states = OpenCodeFormClient.ParseForms(body);

        var state = Assert.Single(states);
        Assert.Equal(status, state.Status);
        Assert.Equal(chosen, state.ChosenValue);
    }

    [Fact]
    public void AnAnsweredBooleanCarriesItsValueAsText()
    {
        var states = OpenCodeFormClient.ParseForms("""
            {"data":[{"id":"frm_1","sessionID":"ses_1","fields":[{"key":"ok","type":"boolean"}],
                      "state":{"status":"answered","answer":{"ok":true}}}]}
            """);

        Assert.Equal(QuestionStatus.Answered, Assert.Single(states).Status);
        Assert.Equal("true", states[0].ChosenValue);
    }

    [Fact]
    public async Task GetReturnsNullWhenTheFormIsGone()
    {
        var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        Assert.Null(await client.GetAsync("ses_1", "frm_1"));
    }

    [Fact]
    public async Task GetReadsTheSettledStateFromTheEnvelope()
    {
        var client = Client((_, _) => Task.FromResult(Json(HttpStatusCode.OK, """
            {"data":{"id":"frm_1","sessionID":"ses_1",
             "fields":[{"key":"colour","type":"string","options":[{"value":"violet","label":"Violet"}]}],
             "state":{"status":"answered","answer":{"colour":"violet"}}}}
            """)));

        var state = await client.GetAsync("ses_1", "frm_1");

        Assert.NotNull(state);
        Assert.Equal(QuestionStatus.Answered, state!.Status);
        Assert.Equal("violet", state.ChosenValue);
    }

    // ------------------------------------------------------------------ frame parser

    [Fact]
    public void AFrameWithASpaceAfterTheColonIsRead()
    {
        var parser = new SseParser();

        Assert.Null(parser.Push("data: hello"));
        Assert.Equal("hello", parser.Push(string.Empty)?.Data);
    }

    [Fact]
    public void AFrameWithoutASpaceIsRead()
    {
        var parser = new SseParser();

        parser.Push("data:{\"a\":1}");
        var frame = parser.Push(string.Empty);

        Assert.Equal("""{"a":1}""", frame?.Data);
    }

    [Fact]
    public void MultiLineDataIsJoinedWithNewlines()
    {
        var parser = new SseParser();

        parser.Push("data: one");
        parser.Push("data: two");
        var frame = parser.Push(string.Empty);

        Assert.Equal("one\ntwo", frame?.Data);
    }

    [Fact]
    public void CommentsAreIgnored()
    {
        var parser = new SseParser();

        Assert.Null(parser.Push(": keep-alive"));
        Assert.Null(parser.Push("retry: 250"));
        Assert.Null(parser.Push(string.Empty));
    }

    [Fact]
    public void AnEventNameIsCapturedAndCleared()
    {
        var parser = new SseParser();

        parser.Push("event: form.created");
        parser.Push("data: {}");
        Assert.Equal("form.created", parser.Push(string.Empty)?.Event);

        parser.Push("data: {}");
        Assert.Null(parser.Push(string.Empty)?.Event);
    }

    [Fact]
    public void AnEmptyFrameIsNotEmitted()
    {
        var parser = new SseParser();

        Assert.Null(parser.Push(string.Empty));
        Assert.Null(parser.Push(null));
    }

    [Fact]
    public void TheServerEventIsParsedFromItsTypeAndData()
    {
        var ok = OpenCodeFormClient.TryParseEvent(
            """{"id":"evt_1","type":"form.created","data":{"form":{"id":"frm_1"}}}""",
            out var frame);

        Assert.True(ok);
        Assert.Equal("form.created", frame.Type);
        Assert.Equal("frm_1", frame.Data.GetProperty("form").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"id":"evt_1"}""")]
    [InlineData("""{"type":null}""")]
    public void AJunkFrameIsNotAnEvent(string payload)
    {
        Assert.False(OpenCodeFormClient.TryParseEvent(payload, out _));
    }

    // ------------------------------------------------------------------------ live

    /// <summary>
    /// Creates real forms through the child's own API and drives the client against them: no
    /// model turn and no tokens. Returns early when <c>opencode</c> is not on this machine.
    /// </summary>
    [Fact]
    [Trait("Live", "true")]
    public async Task LiveCreateReply409AndCancel()
    {
        if (ProcessCommandRunner.FindOnPath("opencode") is null)
        {
            return;
        }

        var supervisor = new ServeSupervisor(new ProcessServeFactory(), TimeSpan.FromSeconds(30));
        await using var child = await supervisor.StartAsync(Directory.GetCurrentDirectory(), default);
        var client = new OpenCodeFormClient(child.Client);

        var sessionId = await CreateSession(child.Client);

        var formId = await CreateForm(
            child.Client,
            sessionId,
            new
            {
                title = "Which colour?",
                fields = new[]
                {
                    new
                    {
                        key = "colour",
                        type = "string",
                        title = "Colour",
                        options = new[]
                        {
                            new { value = "amber", label = "Amber" },
                            new { value = "violet", label = "Violet" },
                        },
                    },
                },
            });

        var listed = Assert.Single(await client.ListAsync(sessionId));
        Assert.Equal(formId, listed.FormId);
        Assert.Equal(QuestionKind.Choice, listed.Kind);

        Assert.Equal(
            QuestionSubmitOutcome.Accepted,
            await client.ReplyAsync(sessionId, formId, "colour", QuestionKind.Choice, "violet"));

        var settled = await client.GetAsync(sessionId, formId);
        Assert.NotNull(settled);
        Assert.Equal(QuestionStatus.Answered, settled!.Status);
        Assert.Equal("violet", settled.ChosenValue);

        Assert.Equal(
            QuestionSubmitOutcome.AlreadySettled,
            await client.ReplyAsync(sessionId, formId, "colour", QuestionKind.Choice, "amber"));

        var booleanForm = await CreateForm(
            child.Client,
            sessionId,
            new
            {
                title = "Proceed?",
                fields = new[] { new { key = "ok", type = "boolean" } },
            });

        // A string where the server wants a boolean is refused, and the form stays pending.
        Assert.Equal(
            QuestionSubmitOutcome.InvalidAnswer,
            await client.ReplyAsync(sessionId, booleanForm, "ok", QuestionKind.Choice, "true"));

        var cancelForm = await CreateForm(
            child.Client,
            sessionId,
            new
            {
                title = "Dismiss me",
                fields = new[]
                {
                    new
                    {
                        key = "colour",
                        type = "string",
                        options = new[] { new { value = "amber", label = "Amber" } },
                    },
                },
            });

        Assert.Equal(
            QuestionSubmitOutcome.Accepted,
            await client.CancelAsync(sessionId, cancelForm, "no answer sent"));

        var cancelled = await client.GetAsync(sessionId, cancelForm);
        Assert.NotNull(cancelled);
        Assert.Equal(QuestionStatus.Cancelled, cancelled!.Status);
    }

    // --------------------------------------------------------------------- helpers

    private static Uri Address() => new("http://127.0.0.1:4096/");

    private static OpenCodeFormClient Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        TimeSpan? submitTimeout = null) =>
        new(
            new HttpClient(new StubHandler(handler)) { BaseAddress = Address() },
            submitTimeout);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private static async Task<string> CreateSession(HttpClient http)
    {
        using var response = await http.PostAsJsonAsync("/api/session", new { title = "form client test" });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateForm(HttpClient http, string sessionId, object form)
    {
        using var response = await http.PostAsJsonAsync($"/api/session/{sessionId}/form", form);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    private sealed record RecordedRequest(HttpMethod Method, string PathAndQuery, string? Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            _handler = handler;

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!.PathAndQuery, body));
            return await _handler(request, cancellationToken);
        }
    }
}