using System.Net;
using ClimberWeb.Services;
using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Tests;

/// <summary>Surplus configuration, pricing parsing, spend estimates and the catalogue client.</summary>
public class SurplusTests
{
    private const string Catalogue =
        """
        {"data":[
          {"id":"other-model","pricing":{"prompt":"1","completion":"2","input_cache_read":"3","input_cache_write":"4"}},
          {"id":"deepseek-v4.1-flash-1017","pricing":{"prompt":"0.0000000380","completion":"0.0000005000","input_cache_read":"0.0000000075","input_cache_write":"0.0000000380"}}
        ]}
        """;

    // ---- options -------------------------------------------------------------

    [Fact]
    public void Options_DefaultToSurplusAndTheOpenRouterFallback()
    {
        var options = SurplusOptions.FromConfiguration(Config());

        Assert.False(options.HasKey);
        Assert.Equal(SurplusOptions.DefaultBaseUrl, options.BaseUrl);
        Assert.Equal("surplus/deepseek-v4.1-flash-1017", options.Model);
        Assert.Equal("openrouter/deepseek/deepseek-v4.1-flash", options.FallbackModel);
    }

    [Fact]
    public void Options_ReadEnvironmentStyleConfigurationAndNormalizeTheModel()
    {
        var configuration = Config(("SURPLUS_API_KEY", "sk-surplus"), ("Climber:SurplusModel", "deepseek-v4.1-flash-1017"));

        var options = SurplusOptions.FromConfiguration(configuration);

        Assert.True(options.HasKey);
        Assert.Equal("surplus/deepseek-v4.1-flash-1017", options.Model);
    }

    [Fact]
    public void Options_NormalizeABareFallbackModelToOpenRouter()
    {
        var configuration = Config(("Climber:FallbackModel", "deepseek/deepseek-v4.1-flash"));

        var options = SurplusOptions.FromConfiguration(configuration);

        Assert.Equal("openrouter/deepseek/deepseek-v4.1-flash", options.FallbackModel);
    }

    [Fact]
    public void Options_PreferExplicitClimberKeysAndKeepAPrefixedModel()
    {
        var configuration = Config(
            ("Climber:SurplusApiKey", "explicit"),
            ("SURPLUS_API_KEY", "env"),
            ("Climber:SurplusBaseUrl", "https://example.test/v1/"),
            ("Climber:SurplusModel", "surplus/other"),
            ("Climber:FallbackModel", "openrouter/other"));

        var options = SurplusOptions.FromConfiguration(configuration);

        Assert.Equal("explicit", options.ApiKey);
        Assert.Equal("https://example.test/v1", options.BaseUrl);
        Assert.Equal("surplus/other", options.Model);
        Assert.Equal("openrouter/other", options.FallbackModel);
    }

    // ---- parsing and estimates ----------------------------------------------

    [Fact]
    public void Parser_FindsTheModelAndReadsPerTokenPricing()
    {
        var pricing = SurplusParser.ParsePricing(Catalogue, "surplus/deepseek-v4.1-flash-1017#low");

        Assert.NotNull(pricing);
        Assert.Equal(0.000000038, pricing.Prompt);
        Assert.Equal(0.0000005, pricing.Completion);
        Assert.Equal(0.0000000075, pricing.CacheRead);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"data":[{"id":"different"}]}""")]
    public void Parser_ReturnsNullWhenTheModelIsNotUsable(string json)
    {
        Assert.Null(SurplusParser.ParsePricing(json, "surplus/deepseek-v4.1-flash-1017"));
    }

    [Fact]
    public void Estimate_PricesPromptCompletionAndCacheTokens()
    {
        var pricing = new SurplusPricing(0.000000038, 0.0000005, 0.0000000075, 0.000000038);

        var estimate = pricing.Estimate(input: 1_000_000, output: 1_000_000, cacheRead: 1_000_000, cacheWrite: 0);

        Assert.Equal(0.5455m, estimate);
    }

    [Fact]
    public void NormalizeModelId_StripsProviderAndVariant()
    {
        Assert.Equal("deepseek-v4.1-flash-1017", SurplusParser.NormalizeModelId("surplus/deepseek-v4.1-flash-1017#low"));
        Assert.Equal("deepseek-v4.1-flash-1017", SurplusParser.NormalizeModelId("deepseek-v4.1-flash-1017"));
    }

    [Fact]
    public void Report_LabelsTheConnectionState()
    {
        Assert.Equal("not configured", new SurplusReport(false, false, false, "m", "f", null, null, Array.Empty<string>()).StateLabel);
        Assert.Equal("connected", new SurplusReport(true, true, true, "m", "f", null, null, Array.Empty<string>()).StateLabel);
        Assert.Equal("model unavailable", new SurplusReport(true, true, false, "m", "f", null, null, Array.Empty<string>()).StateLabel);
        Assert.Equal("unreachable", new SurplusReport(true, false, false, "m", "f", null, null, Array.Empty<string>()).StateLabel);
    }

    // ---- client --------------------------------------------------------------

    [Fact]
    public async Task Client_WithoutKey_ReportsNotConfiguredAndDoesNotCallTheApi()
    {
        var client = new SurplusClient(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not call"))), Options(null));

        var report = await client.GetReportAsync();

        Assert.False(report.Configured);
        Assert.Equal("not configured", report.StateLabel);
        Assert.NotEmpty(report.Notes);
    }

    [Fact]
    public async Task Client_ReadsTheCatalogueAndPricesTheModel()
    {
        var handler = new StubHandler(_ => Json(Catalogue));
        var client = new SurplusClient(new HttpClient(handler), Options("sk-surplus"));

        var report = await client.GetReportAsync();

        Assert.True(report.Reachable);
        Assert.True(report.ModelAvailable);
        Assert.Equal("connected", report.StateLabel);
        Assert.NotNull(report.Pricing);
        Assert.Contains("models", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task Client_MissingModel_ReportsModelUnavailable()
    {
        var handler = new StubHandler(_ => Json("""{"data":[{"id":"different","pricing":{"prompt":"1"}}]}"""));
        var client = new SurplusClient(new HttpClient(handler), Options("sk-surplus"));

        var report = await client.GetReportAsync();

        Assert.True(report.Reachable);
        Assert.False(report.ModelAvailable);
        Assert.Null(report.Pricing);
        Assert.Equal("model unavailable", report.StateLabel);
    }

    [Fact]
    public async Task Client_NetworkFailure_DoesNotThrow()
    {
        var client = new SurplusClient(new HttpClient(new StubHandler(_ => throw new HttpRequestException("no dns"))), Options("sk-surplus"));

        var report = await client.GetReportAsync();

        Assert.False(report.Reachable);
        Assert.Contains("Could not reach Surplus", report.Message!);
        Assert.Equal("unreachable", report.StateLabel);
    }

    [Fact]
    public async Task Client_HttpFailure_ReportsUnreachable()
    {
        var client = new SurplusClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))), Options("sk-surplus"));

        var report = await client.GetReportAsync();

        Assert.False(report.Reachable);
        Assert.Contains("502", report.Message!);
    }

    // ---- helpers -------------------------------------------------------------

    private static SurplusOptions Options(string? apiKey) =>
        new(apiKey, SurplusOptions.DefaultBaseUrl, SurplusOptions.DefaultModel, SurplusOptions.DefaultFallbackModel);

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_handler(request));
        }
    }
}