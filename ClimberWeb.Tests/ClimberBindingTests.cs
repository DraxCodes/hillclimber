using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>The listen-URL policy that keeps the shipped default PIN off the LAN.</summary>
public class ClimberBindingTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePin_FallsBackToTheDefault(string? configured)
    {
        Assert.Equal(ClimberBinding.DefaultPin, ClimberBinding.ResolvePin(configured));
    }

    [Fact]
    public void ResolvePin_KeepsAConfiguredPin()
    {
        Assert.Equal("4321", ClimberBinding.ResolvePin("4321"));
    }

    [Fact]
    public void DefaultPin_OnAllInterfaces_DowngradesToLoopback()
    {
        var result = ClimberBinding.Resolve("http://0.0.0.0:5179", null);

        Assert.Equal(ClimberBinding.LoopbackUrl, result.Urls);
        Assert.True(result.UsedDefaultPin);
        Assert.True(result.DowngradedToLoopback);
    }

    [Fact]
    public void DefaultPin_WithNoConfiguredUrl_DowngradesToLoopback()
    {
        var result = ClimberBinding.Resolve(null, null);

        Assert.Equal(ClimberBinding.LoopbackUrl, result.Urls);
        Assert.True(result.DowngradedToLoopback);
    }

    [Fact]
    public void ExplicitDefaultPin_OnAllInterfaces_StillDowngrades()
    {
        var result = ClimberBinding.Resolve("http://0.0.0.0:5179", ClimberBinding.DefaultPin);

        Assert.Equal(ClimberBinding.LoopbackUrl, result.Urls);
        Assert.True(result.DowngradedToLoopback);
    }

    [Fact]
    public void RealPin_OnAllInterfaces_KeepsTheLanBind()
    {
        var result = ClimberBinding.Resolve("http://0.0.0.0:5179", "4321");

        Assert.Equal("http://0.0.0.0:5179", result.Urls);
        Assert.False(result.UsedDefaultPin);
        Assert.False(result.DowngradedToLoopback);
    }

    [Theory]
    [InlineData("http://localhost:5179")]
    [InlineData("http://127.0.0.1:5179")]
    [InlineData("http://[::1]:5179")]
    public void DefaultPin_OnLoopback_IsAllowed(string urls)
    {
        var result = ClimberBinding.Resolve(urls, null);

        Assert.Equal(urls, result.Urls);
        Assert.True(result.UsedDefaultPin);
        Assert.False(result.DowngradedToLoopback);
    }

    [Fact]
    public void DefaultPin_WithAnyNonLoopbackUrl_Downgrades()
    {
        var result = ClimberBinding.Resolve("http://localhost:5179;http://0.0.0.0:5179", null);

        Assert.Equal(ClimberBinding.LoopbackUrl, result.Urls);
        Assert.True(result.DowngradedToLoopback);
    }

    [Fact]
    public void DefaultPin_WithOnlyLoopbackUrls_IsAllowed()
    {
        var result = ClimberBinding.Resolve("http://localhost:5179;http://127.0.0.1:5179", null);

        Assert.Equal("http://localhost:5179;http://127.0.0.1:5179", result.Urls);
        Assert.False(result.DowngradedToLoopback);
    }

    [Fact]
    public void DefaultPin_WithAnUnparseableUrl_FailsClosed()
    {
        var result = ClimberBinding.Resolve("not a url", null);

        Assert.Equal(ClimberBinding.LoopbackUrl, result.Urls);
        Assert.True(result.DowngradedToLoopback);
    }

    [Theory]
    [InlineData("http://localhost:5179", true)]
    [InlineData("http://127.0.0.1:5179", true)]
    [InlineData("http://[::1]:5179", true)]
    [InlineData("http://0.0.0.0:5179", false)]
    [InlineData("http://example.com:5179", false)]
    [InlineData("", false)]
    public void IsLoopbackOnly_ClassifiesHosts(string urls, bool expected)
    {
        Assert.Equal(expected, ClimberBinding.IsLoopbackOnly(urls));
    }
}
