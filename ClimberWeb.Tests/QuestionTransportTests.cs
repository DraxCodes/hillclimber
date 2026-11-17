using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class QuestionTransportTests
{
    [Theory]
    [InlineData(null, QuestionTransportMode.Off)]
    [InlineData("", QuestionTransportMode.Off)]
    [InlineData("   ", QuestionTransportMode.Off)]
    [InlineData("off", QuestionTransportMode.Off)]
    [InlineData("garbage", QuestionTransportMode.Off)]
    [InlineData("serve ", QuestionTransportMode.Serve)]
    [InlineData("SERVE", QuestionTransportMode.Serve)]
    [InlineData("Serve", QuestionTransportMode.Serve)]
    public void OnlyServeEnablesTheTransport(string? value, QuestionTransportMode expected)
    {
        Assert.Equal(expected, QuestionTransport.Parse(value));
    }

    [Fact]
    public void TheDefaultIsOff()
    {
        var transport = new QuestionTransport(
            QuestionTransportMode.Off,
            new ServeSupervisor(new ProcessServeFactory()));

        Assert.False(transport.Enabled);
        Assert.Null(transport.Create());
    }

    [Fact]
    public void ServeCreatesAChannel()
    {
        var transport = new QuestionTransport(
            QuestionTransportMode.Serve,
            new ServeSupervisor(new ProcessServeFactory()));

        Assert.True(transport.Enabled);
        Assert.NotNull(transport.Create());
    }
}