using System.Net;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>Per-climb spend accumulation and the OpenRouter connection/spend client.</summary>
public class SpendTests
{
    // ---- opencode event usage ------------------------------------------------

    [Fact]
    public void StepUsage_ReadsCostAndTokensFromAStampedStepFinish()
    {
        const string line = """[07:49:26] {"type":"step_finish","part":{"reason":"stop","cost":0.005448525,"tokens":{"input":2069,"output":149,"reasoning":73,"cache":{"read":9067,"write":4}}}}""";

        var usage = OpenCodeEvents.TryReadStepUsage(line);

        Assert.NotNull(usage);
        Assert.Equal(0.005448525, usage!.Cost, 9);
        Assert.Equal(2069, usage.Input);
        Assert.Equal(149, usage.Output);
        Assert.Equal(73, usage.Reasoning);
        Assert.Equal(9067, usage.CacheRead);
        Assert.Equal(4, usage.CacheWrite);
    }

    [Theory]
    [InlineData("""{"type":"text","part":{"text":"hi"}}""")]
    [InlineData("""{"type":"step_finish"}""")]
    [InlineData("not json")]
    [InlineData("{ truncated")]
    [InlineData("")]
    public void StepUsage_IgnoresAnythingThatIsNotAStepFinish(string line)
    {
        Assert.Null(OpenCodeEvents.TryReadStepUsage(line));
    }

    [Fact]
    public void StepUsage_DefaultsMissingNumbersToZero()
    {
        var usage = OpenCodeEvents.ReadUsage(System.Text.Json.JsonDocument.Parse("""{"reason":"stop"}""").RootElement);

        Assert.Equal(0d, usage.Cost);
        Assert.Equal(0, usage.Input);
        Assert.Equal(0, usage.Output);
    }

    [Fact]
    public async Task Climb_AccumulatesSpendFromStepFinishEvents()
    {
        var lines = string.Join('\n',
            """{"type":"step_finish","part":{"reason":"stop","cost":0.001,"tokens":{"input":10,"output":2}}}""",
            """{"type":"text","part":{"text":"no cost here"}}""",
            """{"type":"step_finish","part":{"reason":"tool-calls","cost":0.0025,"tokens":{"input":20,"output":4}}}""");

        var runner = new FakeCommandRunner((request, _) => request.FileName switch
        {
            "opencode" => CannedResults.Raw(lines),
            "dotnet" => CannedResults.Tests(3),
            _ => CannedResults.Git(),
        });

        var service = new HillClimberService(runner);
        await service.StartAsync(new ClimberOptions(@"C:\work\repo", "goal", 1));

        Assert.Equal(0.0035m, service.State.SpendUsd);
        Assert.Contains(service.State.Logs, line => line.Contains("Spend this run"));
    }

    // ---- OpenRouter parsing --------------------------------------------------

    [Fact]
    public void ParseKey_ReadsLabelUsageAndLimit()
    {
        const string json = """{"data":{"label":"sk-or-v1-au7...890","is_free_tier":false,"usage":25.5,"limit":100,"limit_remaining":74.5}}""";

        var key = OpenRouterParser.ParseKey(json);

        Assert.NotNull(key);
        Assert.Equal("sk-or-v1-au7...890", key!.Label);
        Assert.False(key.IsFreeTier);
        Assert.Equal(25.5, key.Usage);
        Assert.Equal(74.5, key.LimitRemaining);
    }

    [Fact]
    public void ParseCredits_ReadsTotals()
    {
        var account = OpenRouterParser.ParseCredits("""{"data":{"total_credits":100.5,"total_usage":25.75}}""");

        Assert.NotNull(account);
        Assert.Equal(100.5, account!.TotalCredits);
        Assert.Equal(25.75, account.TotalUsage);
        Assert.Equal(74.75, account.Remaining, 6);
    }

    [Fact]
    public void ParseActivity_ReadsRowsOldestFirstAndSkipsMalformed()
    {
        const string json = """
            {"data":[
              {"date":"2026-11-13","model":"b/model","requests":2,"prompt_tokens":10,"completion_tokens":5,"usage":0.02},
              {"date":"not-a-date","model":"x","usage":1},
              {"date":"2026-11-12","model":"a/model","requests":1,"prompt_tokens":3,"completion_tokens":1,"usage":0.01}
            ]}
            """;

        var rows = OpenRouterParser.ParseActivity(json);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 11, 12), rows[0].Date);
        Assert.Equal("a/model", rows[0].Model);
        Assert.Equal(0.02, rows[1].Usage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("""{"data":{}}""")]
    public void ParseActivity_UnusableInput_IsEmpty(string? json)
    {
        Assert.Empty(OpenRouterParser.ParseActivity(json));
    }

    // ---- OpenRouter client ---------------------------------------------------

    [Fact]
    public async Task Client_WithoutKey_ReportsNotConfigured()
    {
        var client = new OpenRouterClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not call"))), Options(apiKey: null));

        var report = await client.GetSpendAsync();

        Assert.False(report.Connection.Configured);
        Assert.False(report.Connection.Connected);
        Assert.Null(report.Account);
    }

    [Fact]
    public async Task Client_Connected_ReadsAccountAndActivityWithManagementKey()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/key" => Json("""{"data":{"label":"key-1","is_free_tier":false,"usage":1.5,"limit":10,"limit_remaining":8.5}}"""),
            "/api/v1/credits" => Json("""{"data":{"total_credits":100,"total_usage":30}}"""),
            "/api/v1/activity" => Json("""{"data":[{"date":"2026-11-13","model":"m","requests":1,"prompt_tokens":1,"completion_tokens":1,"usage":0.5}]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        var client = new OpenRouterClient(new HttpClient(handler), Options("sk-normal", "sk-management"));
        var report = await client.GetSpendAsync();

        Assert.True(report.Connection.Connected);
        Assert.Equal("key-1", report.Connection.KeyLabel);
        Assert.Equal(30, report.Account!.TotalUsage);
        Assert.Single(report.Activity);
    }

    [Fact]
    public async Task Client_KeyRejected_ReportsNotConnected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new OpenRouterClient(new HttpClient(handler), Options("sk-bad"));

        var report = await client.GetSpendAsync();

        Assert.True(report.Connection.Configured);
        Assert.False(report.Connection.Connected);
        Assert.Contains("rejected", report.Connection.Message!);
    }

    [Fact]
    public async Task Client_NetworkFailure_DoesNotThrow()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no dns"));
        var client = new OpenRouterClient(new HttpClient(handler), Options("sk-any"));

        var report = await client.GetSpendAsync();

        Assert.False(report.Connection.Connected);
        Assert.Contains("Could not reach", report.Connection.Message!);
    }

    [Fact]
    public async Task Client_ManagementKeyFailure_NotesItAndStillConnects()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath == "/api/v1/key"
            ? Json("""{"data":{"label":"k","is_free_tier":true,"usage":0.1}}""")
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        var client = new OpenRouterClient(new HttpClient(handler), Options("sk-normal", "sk-mgmt"));
        var report = await client.GetSpendAsync();

        Assert.True(report.Connection.Connected);
        Assert.True(report.Connection.IsFreeTier);
        Assert.Null(report.Account);
        Assert.NotEmpty(report.Notes);
    }

    // ---- helpers -------------------------------------------------------------

    private static OpenRouterOptions Options(string? apiKey, string? managementKey = null) =>
        new(apiKey, managementKey, OpenRouterOptions.DefaultBaseUrl);

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
