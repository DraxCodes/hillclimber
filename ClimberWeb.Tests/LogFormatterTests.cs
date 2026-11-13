using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class LogFormatterTests
{
    private static string Stamped(string payload) => $"[12:34:56] {payload}";

    [Fact]
    public void PlainLinePassesThroughUnchanged()
    {
        var line = "[12:34:56] ===== Iteration 1/3 =====";

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Plain, entry.Kind);
        Assert.Equal(line, entry.Raw);
        Assert.Equal(line, entry.Text);
    }

    [Fact]
    public void ParsesTextEvent()
    {
        var line = Stamped("""
            {"type":"text","timestamp":1794553341357,"sessionID":"ses_a","part":{"id":"prt_a","sessionID":"ses_a","messageID":"msg_a","type":"text","text":"pong","time":{"start":1,"end":2}}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Text, entry.Kind);
        Assert.Equal("pong", entry.Text);
        Assert.Equal("12:34:56", entry.Timestamp);
    }

    [Fact]
    public void ParsesTextEventPreservingNewlines()
    {
        var line = Stamped("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"line one\\nline two\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Text, entry.Kind);
        Assert.Equal("line one\nline two", entry.Text);
    }

    [Fact]
    public void ParsesToolUseEvent()
    {
        var line = Stamped("""
            {"type":"tool_use","timestamp":1794553345894,"part":{"partID":"prt_t","sessionID":"ses_a","messageID":"msg_a","type":"tool","id":"call_1","tool":"shell","state":{"status":"completed","input":{"command":"Get-ChildItem -Force"},"output":"total 4\ndir A\n","title":"shell","metadata":{"rawInput":"{\"command\": \"Get-ChildItem -Force\"}","content":[{"type":"text","text":"total 4\ndir A\n"}]},"time":{"start":1,"end":2}}}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Tool, entry.Kind);
        Assert.Equal("shell", entry.Text);
        Assert.Contains("command=Get-ChildItem -Force", entry.ToolInput);
        Assert.Equal("total 4\ndir A\n", entry.ToolOutput);
    }

    [Fact]
    public void ToolUseEventDoesNotExposeJsonBoilerplate()
    {
        var line = Stamped("""
            {"type":"tool_use","part":{"type":"tool","tool":"shell","state":{"status":"completed","input":{"command":"echo hi"},"output":"hi","metadata":{"rawInput":"{\"command\": \"echo hi\"}","content":[{"type":"text","text":"hi"}],"providerCall":{"executed":false}}}}}
            """);

        var entry = LogFormatter.Parse(line);

        var displayed = string.Concat(entry.Text, entry.ToolInput, entry.ToolOutput);
        Assert.DoesNotContain("metadata", displayed);
        Assert.DoesNotContain("rawInput", displayed);
        Assert.DoesNotContain("partID", displayed);
        Assert.DoesNotContain("sessionID", displayed);
    }

    [Fact]
    public void ParsesStepStartAndStepFinish()
    {
        var start = LogFormatter.Parse(Stamped("{\"type\":\"step_start\",\"part\":{\"type\":\"step-start\"}}"));
        var finish = LogFormatter.Parse(Stamped("""
            {"type":"step_finish","part":{"type":"step-finish","reason":"stop","cost":0.000208371,"tokens":{"input":221,"output":116,"reasoning":17,"cache":{"read":7296,"write":0}}}}
            """));

        Assert.Equal(LogKind.StepStart, start.Kind);
        Assert.NotEmpty(start.Text);
        Assert.Equal(LogKind.StepFinish, finish.Kind);
        Assert.Contains("stop", finish.Text);
        Assert.Contains("221", finish.Text);
        Assert.Contains("116", finish.Text);
        Assert.Contains("reasoning 17", finish.Text);
    }

    [Fact]
    public void ToolUseWithOnlyNestedInputLeavesSummaryEmpty()
    {
        var line = Stamped("""
            {"type":"tool_use","part":{"type":"tool","tool":"task","state":{"input":{"options":{"a":1},"list":[1,2]}}}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Tool, entry.Kind);
        Assert.Equal("task", entry.Text);
        Assert.Equal(string.Empty, entry.ToolInput);
    }

    [Fact]
    public void ParsesErrorEvent()
    {
        var line = Stamped("{\"type\":\"error\",\"part\":{\"type\":\"error\",\"error\":\"boom\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Error, entry.Kind);
        Assert.Equal("boom", entry.Text);
    }

    [Fact]
    public void ParsesErrorObjectAtTheRoot()
    {
        var line = Stamped("{\"type\":\"error\",\"part\":{\"type\":\"error\"},\"error\":{\"message\":\"root failure\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Error, entry.Kind);
        Assert.Contains("root failure", entry.Text);
    }

    [Theory]
    [InlineData("[12:34:56] { not json")]
    [InlineData("[12:34:56] {")]
    [InlineData("[12:34:56] [1,2,3]")]
    [InlineData("[12:34:56] {\"foo\":1}")]
    [InlineData("[12:34:56] {\"type\":\"text\"}")]
    [InlineData("[12:34:56] {\"type\":\"text\",\"part\":{}}")]
    [InlineData("[12:34:56] {\"type\":\"mystery\",\"part\":{\"type\":\"mystery\"}}")]
    [InlineData("[12:34:56] {\"type\":\"tool\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\"}}")]
    [InlineData("[12:34:56] completely plain")]
    [InlineData("")]
    public void FallsBackToPlainForAnythingNotAnOpencodeEvent(string line)
    {
        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Plain, entry.Kind);
        Assert.Equal(line, entry.Raw);
    }

    [Fact]
    public void DoesNotThrowOnNull()
    {
        var entry = LogFormatter.Parse(null!);

        Assert.Equal(LogKind.Plain, entry.Kind);
    }

    [Fact]
    public void ExtractsStampEvenWhenPayloadIsStructured()
    {
        var line = Stamped("{\"type\":\"step_start\",\"part\":{\"type\":\"step-start\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal("12:34:56", entry.Timestamp);
        Assert.Equal(line, entry.Raw);
    }

    [Fact]
    public void FormatCachesResults()
    {
        var formatter = new LogFormatter();
        var line = Stamped("{\"type\":\"text\",\"part\":{\"type\":\"text\",\"text\":\"hello\"}}");

        var first = formatter.Format(line);
        var second = formatter.Format(line);

        Assert.Equal(first, second);
    }
}
