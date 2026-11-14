using ClimberWeb.Services;
using Microsoft.Extensions.Configuration;

namespace ClimberWeb.Tests;

/// <summary>
/// The provider registry: legacy synthesis, the explicit <c>Climber:Providers</c> array,
/// capability assignment and ordered fallback resolution.
/// </summary>
public class ProviderRegistryTests
{
    // ---- legacy synthesis ----------------------------------------------------

    [Fact]
    public void LegacyConfiguration_SynthesisesSurplusThenOpenRouter()
    {
        var registry = Registry();

        Assert.Equal(2, registry.Providers.Count);
        Assert.Equal(ProviderIds.Surplus, registry.Providers[0].Id);
        Assert.Equal(ProviderIds.OpenRouter, registry.Providers[1].Id);
        Assert.Equal("Surplus", registry.Providers[0].DisplayName);
        Assert.Equal(ProviderKinds.OpenAi, registry.Providers[0].Descriptor.Kind);
        Assert.Equal(ProviderKinds.OpenRouter, registry.Providers[1].Descriptor.Kind);
        Assert.Equal(SpendMode.Estimated, registry.Providers[0].SpendMode);
        Assert.Equal(SpendMode.Reported, registry.Providers[1].SpendMode);
        Assert.IsType<OpenAiCatalogueProbe>(registry.Providers[0].Catalogue);
        Assert.Null(registry.Providers[0].Spend);
        Assert.Null(registry.Providers[1].Catalogue);
        Assert.IsType<OpenRouterClient>(registry.Providers[1].Spend);
        Assert.Same(registry.Providers[0], registry.Primary);
    }

    [Fact]
    public void LegacyConfiguration_DefaultsTheModelsAndBaseUrls()
    {
        var registry = Registry();

        Assert.Equal(ProviderRegistry.SurplusDefaultModel, registry.Providers[0].Descriptor.Model);
        Assert.Equal(ProviderRegistry.SurplusDefaultBaseUrl, registry.Providers[0].Descriptor.BaseUrl);
        Assert.Equal(ProviderRegistry.OpenRouterDefaultModel, registry.Providers[1].Descriptor.Model);
        Assert.Equal(ProviderRegistry.OpenRouterDefaultBaseUrl, registry.Providers[1].Descriptor.BaseUrl);
        Assert.False(registry.Providers[0].Descriptor.HasKey);
        Assert.False(registry.Providers[1].Descriptor.HasKey);
    }

    [Fact]
    public void LegacyConfiguration_ReadsExplicitKeysAndEnvironmentAliases()
    {
        var registry = Registry(
            ("Climber:SurplusApiKey", "explicit-surplus"),
            ("SURPLUS_API_KEY", "env-surplus"),
            ("OPENROUTER_API_KEY", "env-openrouter"),
            ("OPENROUTER_MANAGEMENT_API_KEY", "env-management"));

        Assert.Equal("explicit-surplus", registry.Providers[0].Descriptor.ApiKey);
        Assert.Equal("env-openrouter", registry.Providers[1].Descriptor.ApiKey);
        Assert.Equal("env-management", registry.Providers[1].Descriptor.ManagementApiKey);
    }

    [Fact]
    public void LegacyConfiguration_NormalisesABareFallbackModelToOpenRouter()
    {
        var registry = Registry(("Climber:FallbackModel", "deepseek/deepseek-v4.1-flash"));

        Assert.Equal("openrouter/deepseek/deepseek-v4.1-flash", registry.Providers[1].Descriptor.Model);
    }

    [Fact]
    public void LegacyConfiguration_KeepsAPrefixedModelAndTrimsTheBaseUrl()
    {
        var registry = Registry(
            ("Climber:SurplusBaseUrl", "https://example.test/v1/"),
            ("Climber:SurplusModel", "surplus/other"),
            ("Climber:FallbackModel", "openrouter/other"));

        Assert.Equal("https://example.test/v1", registry.Providers[0].Descriptor.BaseUrl);
        Assert.Equal("surplus/other", registry.Providers[0].Descriptor.Model);
        Assert.Equal("openrouter/other", registry.Providers[1].Descriptor.Model);
    }

    // ---- explicit array ------------------------------------------------------

    [Fact]
    public void ProvidersArray_ReadsAnOpenAiCompatibleProvider()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "local"),
            ("Climber:Providers:0:Name", "Local Llama"),
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Prefix", "local"),
            ("Climber:Providers:0:Model", "llama-3.1"),
            ("Climber:Providers:0:BaseUrl", "http://127.0.0.1:11434/v1/"),
            ("Climber:Providers:0:RequiresKey", "false"),
            ("Climber:Providers:0:SendKey", "false"));

        var provider = Assert.Single(registry.Providers);
        Assert.Equal("local", provider.Id);
        Assert.Equal("Local Llama", provider.DisplayName);
        Assert.Equal("local/llama-3.1", provider.Descriptor.Model);
        Assert.Equal("http://127.0.0.1:11434/v1", provider.Descriptor.BaseUrl);
        Assert.Equal(SpendMode.Estimated, provider.SpendMode);
        Assert.False(provider.Descriptor.RequiresKey);
        Assert.False(provider.Descriptor.SendKey);
        Assert.IsType<OpenAiCatalogueProbe>(provider.Catalogue);
        Assert.Null(provider.Spend);
        Assert.Empty(registry.Notes);
    }

    [Fact]
    public void ProvidersArray_ReadsAnOpenRouterStyleProvider()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "aggregator"),
            ("Climber:Providers:0:Kind", "OpenRouter"),
            ("Climber:Providers:0:Model", "openrouter/deepseek/deepseek-v4.1-flash"),
            ("Climber:Providers:0:BaseUrl", "https://aggregator.test/api/v1"),
            ("Climber:Providers:0:ApiKey", "sk-inference"),
            ("Climber:Providers:0:ManagementApiKey", "sk-management"));

        var provider = Assert.Single(registry.Providers);
        Assert.Equal(SpendMode.Reported, provider.SpendMode);
        Assert.Null(provider.Catalogue);
        Assert.IsType<OpenRouterClient>(provider.Spend);
        Assert.Equal("sk-inference", provider.Descriptor.ApiKey);
        Assert.Equal("sk-management", provider.Descriptor.ManagementApiKey);
    }

    [Fact]
    public void ProvidersArray_NoKind_RunsWithoutAnApiSurface()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "opencode-only"),
            ("Climber:Providers:0:Model", "provider/model"));

        var provider = Assert.Single(registry.Providers);
        Assert.Null(provider.Catalogue);
        Assert.Null(provider.Spend);
        Assert.Equal(SpendMode.None, provider.SpendMode);
        Assert.Equal("provider/model", provider.Descriptor.Model);
    }

    [Fact]
    public void ProvidersArray_UnknownKind_IsNotedAndRunsWithoutASurface()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "mystery"),
            ("Climber:Providers:0:Kind", "telepathy"));

        var provider = Assert.Single(registry.Providers);
        Assert.Null(provider.Catalogue);
        Assert.Null(provider.Spend);
        Assert.Equal(SpendMode.None, provider.SpendMode);
        Assert.Contains(registry.Notes, note => note.Contains("unknown kind 'telepathy'"));
    }

    [Fact]
    public void ProvidersArray_SpendCanBeOverridden()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "local"),
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Model", "m"),
            ("Climber:Providers:0:BaseUrl", "http://local/v1"),
            ("Climber:Providers:0:RequiresKey", "false"),
            ("Climber:Providers:0:Spend", "none"));

        Assert.Equal(SpendMode.None, Assert.Single(registry.Providers).SpendMode);
        Assert.IsType<OpenAiCatalogueProbe>(registry.Providers[0].Catalogue);
    }

    [Fact]
    public void ProvidersArray_ReadsProviderSpecificEnvironmentKeys()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "acme"),
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Model", "m"),
            ("Climber:Providers:0:BaseUrl", "https://acme.test/v1"),
            ("ACME_API_KEY", "sk-acme"),
            ("ACME_MANAGEMENT_API_KEY", "sk-acme-mgmt"));

        Assert.Equal("sk-acme", registry.Providers[0].Descriptor.ApiKey);
        Assert.Equal("sk-acme-mgmt", registry.Providers[0].Descriptor.ManagementApiKey);
    }

    [Fact]
    public void ProvidersArray_MissingId_IsNotedAndSkipped()
    {
        var registry = Registry(
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Model", "m"),
            ("Climber:Providers:1:Id", "kept"),
            ("Climber:Providers:1:Model", "m"));

        var provider = Assert.Single(registry.Providers);
        Assert.Equal("kept", provider.Id);
        Assert.Contains(registry.Notes, note => note.Contains("no Id"));
    }

    [Fact]
    public void ProvidersArray_DuplicateId_KeepsTheFirstAndNotesIt()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "acme"),
            ("Climber:Providers:0:Name", "First"),
            ("Climber:Providers:0:Kind", "none"),
            ("Climber:Providers:1:Id", "acme"),
            ("Climber:Providers:1:Name", "Second"),
            ("Climber:Providers:1:Kind", "none"));

        var provider = Assert.Single(registry.Providers);
        Assert.Equal("First", provider.DisplayName);
        Assert.Contains(registry.Notes, note => note.Contains("more than once"));
    }

    [Fact]
    public void ProvidersArray_MissingKey_IsNotedButStillListed()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "acme"),
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Model", "m"),
            ("Climber:Providers:0:BaseUrl", "https://acme.test/v1"));

        Assert.Single(registry.Providers);
        Assert.Contains(registry.Notes, note => note.Contains("no API key"));
    }

    // ---- ordering and model normalisation -----------------------------------

    [Fact]
    public void FallbacksAfter_ReturnsEveryLaterProviderInOrder()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "first"),
            ("Climber:Providers:1:Id", "second"),
            ("Climber:Providers:2:Id", "third"));

        var fallbacks = registry.FallbacksAfter("first");

        Assert.Equal(["second", "third"], fallbacks.Select(selection => selection.Id));
        Assert.Empty(registry.FallbacksAfter("third"));
        Assert.Empty(registry.FallbacksAfter("missing"));
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndFallsBackToThePrimary()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "first"),
            ("Climber:Providers:1:Id", "second"));

        Assert.Equal("second", registry.Find("SECOND")?.Id);
        Assert.Equal("first", registry.Find(null)?.Id);
        Assert.Null(registry.Find("missing"));
    }

    [Fact]
    public void NormalizeModel_ReplacesAKnownServingPrefixAndKeepsAModelFamilyPrefix()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "first"),
            ("Climber:Providers:1:Id", "second"));

        Assert.Equal("first/deepseek/x", registry.NormalizeModel("first", "second/deepseek/x"));
        Assert.Equal("first/deepseek/x", registry.NormalizeModel("first", "deepseek/x"));
        Assert.Equal("first/deepseek/x", registry.NormalizeModel("first", "first/deepseek/x"));
        Assert.Null(registry.NormalizeModel("first", " "));
    }

    [Fact]
    public void Selection_From_CarriesTheProviderModelSpendModeAndPricing()
    {
        var registry = Registry(
            ("Climber:Providers:0:Id", "local"),
            ("Climber:Providers:0:Kind", "openai"),
            ("Climber:Providers:0:Model", "m"),
            ("Climber:Providers:0:BaseUrl", "http://local/v1"),
            ("Climber:Providers:0:RequiresKey", "false"));
        var pricing = new ProviderPricing(0.000001, 0.000002, 0, 0);

        var selection = ProviderSelection.From(registry.Providers[0], pricing);

        Assert.Equal("local", selection.Id);
        Assert.Equal("local/m", selection.Model);
        Assert.Equal(SpendMode.Estimated, selection.SpendMode);
        Assert.Same(pricing, selection.Pricing);
    }

    // ---- helpers -------------------------------------------------------------

    private static ProviderRegistry Registry(params (string Key, string Value)[] values) =>
        ProviderRegistry.FromConfiguration(Config(values), new HttpClient());

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
}