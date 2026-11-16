using System.Text.Json;
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
    public void ReadsTheHumanTitleCommandAndExitCodeFromAShellCall()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"dotnet test\"},\"output\":\"ok\",\"title\":\"Run the test suite\",\"metadata\":{\"exit\":0}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal("Run the test suite", entry.ToolTitle);
        Assert.Equal("dotnet test", entry.Command);
        Assert.Equal(0, entry.ExitCode);
        Assert.Equal(StepStatus.None, entry.Status);
    }

    [Fact]
    public void ToolTitleEqualToTheToolNameIsDropped()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"echo hi\"},\"output\":\"hi\",\"title\":\"shell\"}}}");

        Assert.Null(LogFormatter.Parse(line).ToolTitle);
    }

    [Fact]
    public void InputDescriptionBecomesTheTitleWhenStateHasNone()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"dotnet test\",\"description\":\"Run the tests\"},\"output\":\"ok\"}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal("Run the tests", entry.ToolTitle);
        Assert.Equal("command=dotnet test", entry.ToolInput);
    }

    [Fact]
    public void ShellCommandKeepsItsFullTextWhenTheInputSummaryCannot()
    {
        var command = "dotnet test --filter \"Category=Unit · Slow\"\n  --nologo";
        var payload = "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":"
            + JsonSerializer.Serialize(command)
            + "},\"output\":\"ok\"}}}";

        var entry = LogFormatter.Parse(Stamped(payload));

        Assert.Equal(command, entry.Command);
        Assert.DoesNotContain("\n", entry.ToolInput);
    }

    [Fact]
    public void NonZeroExitCodeMarksTheCallFailed()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"completed\",\"input\":{\"command\":\"dotnet build\"},\"output\":\"error CS1002\",\"metadata\":{\"exit\":1}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(StepStatus.Failed, entry.Status);
        Assert.Equal(1, entry.ExitCode);
    }

    [Fact]
    public void NonShellToolHasNoCommand()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"read\",\"state\":{\"status\":\"completed\",\"input\":{\"filePath\":\"src/Foo.cs\"},\"output\":\"contents\",\"metadata\":{\"exit\":1}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Null(entry.Command);
    }

    [Theory]
    [InlineData("\"completed\"", StepStatus.None)]
    [InlineData("\"running\"", StepStatus.Running)]
    [InlineData("\"pending\"", StepStatus.Running)]
    [InlineData("\"in_progress\"", StepStatus.Running)]
    [InlineData("\"error\"", StepStatus.Failed)]
    [InlineData("\"failed\"", StepStatus.Failed)]
    [InlineData("\"mystery\"", StepStatus.None)]
    public void MapsToolStateStatus(string stateStatus, StepStatus expected)
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":"
            + stateStatus
            + ",\"input\":{\"command\":\"echo hi\"}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Tool, entry.Kind);
        Assert.Equal(expected, entry.Status);
    }

    [Fact]
    public void MissingStepStatusReadsAsNone()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"input\":{\"command\":\"echo hi\"}}}}");

        Assert.Equal(StepStatus.None, LogFormatter.Parse(line).Status);
    }

    [Fact]
    public void FailedToolUsesErrorTextWhenOutputIsMissing()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"error\",\"input\":{\"command\":\"dotnet build\"},\"error\":\"error CS1002\"}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(StepStatus.Failed, entry.Status);
        Assert.Equal("error CS1002", entry.ToolOutput);
    }

    [Fact]
    public void ToolOutputWinsOverErrorText()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"shell\",\"state\":{\"status\":\"error\",\"output\":\"out\",\"error\":\"err\"}}}");

        Assert.Equal("out", LogFormatter.Parse(line).ToolOutput);
    }

    [Fact]
    public void ObjectShapedToolErrorFallsBackToRawJson()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"read\",\"state\":{\"status\":\"error\",\"input\":{\"filePath\":\"src/Foo.cs\"},\"error\":{\"message\":\"denied\"}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Tool, entry.Kind);
        Assert.Equal(StepStatus.Failed, entry.Status);
        Assert.Contains("\"message\":\"denied\"", entry.ToolOutput);
    }

    [Fact]
    public void FailedSubagentCarriesStatusAndErrorText()
    {
        var line = Stamped(
            "{\"type\":\"tool_use\",\"part\":{\"type\":\"tool\",\"tool\":\"subagent\",\"state\":{\"status\":\"error\",\"input\":{\"agent\":\"plan-architect\",\"prompt\":\"Plan.\"},\"error\":\"boom\"}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Subagent, entry.Kind);
        Assert.Equal(StepStatus.Failed, entry.Status);
        Assert.Equal("boom", entry.ToolOutput);
    }

    [Fact]
    public void ParsesSubagentCall_WithAgentPromptAndUnwrappedResult()
    {
        var line = Stamped("""
            {"type":"tool_use","part":{"type":"tool","tool":"subagent","state":{"status":"completed","input":{"agent":"plan-architect","description":"Two-step plan","prompt":"Produce a two-step plan."},"output":"<subagent sessionID=\"ses_child\" state=\"completed\">\n1. Do the thing.\n</subagent>","title":"subagent"}}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Subagent, entry.Kind);
        Assert.Equal("plan-architect", entry.Text);
        Assert.Equal("Produce a two-step plan.", entry.ToolInput);
        Assert.Equal("1. Do the thing.", entry.ToolOutput);
        Assert.DoesNotContain("subagent sessionID", entry.ToolOutput);
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
        Assert.Contains("17 reasoning", finish.Text);
        Assert.DoesNotContain("step finish", finish.Text);
        Assert.Contains("cache 7296 read", finish.Text);
    }

    [Fact]
    public void ShowsCacheWriteTokensWhenPresent()
    {
        var line = Stamped("{\"type\":\"step_finish\",\"part\":{\"type\":\"step-finish\",\"reason\":\"stop\",\"tokens\":{\"input\":1,\"output\":2,\"cache\":{\"read\":3,\"write\":4}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Contains("cache 3 read / 4 write", entry.Text);
    }

    [Fact]
    public void KeepsFullErrorTextWithoutTruncating()
    {
        var message = new string('x', 1200);
        var line = Stamped($"{{\"type\":\"error\",\"part\":{{\"type\":\"error\",\"error\":\"{message}\"}}}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Error, entry.Kind);
        Assert.Equal(message, entry.Text);
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
    public void ParsesReasoningEvent()
    {
        var line = Stamped("""
            {"type":"reasoning","part":{"type":"reasoning","text":"first line\nsecond line"}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Reasoning, entry.Kind);
        Assert.Equal("first line\nsecond line", entry.Text);
    }

    [Fact]
    public void EmptyReasoningKeepsItsKindSoTheGrouperCanSkipIt()
    {
        var line = Stamped("{\"type\":\"reasoning\",\"part\":{\"type\":\"reasoning\",\"text\":\"\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Reasoning, entry.Kind);
        Assert.Equal(string.Empty, entry.Text);
        Assert.Equal(line, entry.Raw);
    }

    [Fact]
    public void ReasoningStripsBidiOverridesAndKeepsLineBreaks()
    {
        var line = Stamped("{\"type\":\"reasoning\",\"part\":{\"type\":\"reasoning\",\"text\":\"safe \u202Eevil\u202C\\nnext\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Reasoning, entry.Kind);
        Assert.DoesNotContain('\u202E', entry.Text);
        Assert.DoesNotContain('\u202C', entry.Text);
        Assert.Equal("safe evil\nnext", entry.Text);
    }

    [Fact]
    public void ParsesTheShippedErrorShapeWithoutAPart()
    {
        var line = Stamped("""
            {"type":"error","timestamp":1,"sessionID":"ses_a","error":{"name":"APIError","data":{"message":"Rate limit exceeded; retrying in 5s","statusCode":429,"isRetryable":true}}}
            """);

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Error, entry.Kind);
        Assert.Equal("Rate limit exceeded; retrying in 5s", entry.Text);
    }

    [Fact]
    public void UnknownOpencodeEventKeepsItsPayloadInsteadOfShowingJson()
    {
        var line = Stamped("{\"type\":\"session.updated\",\"part\":{\"id\":\"ses_a\",\"type\":\"session\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Unrecognized, entry.Kind);
        Assert.Equal("session.updated", entry.Text);
        Assert.Equal(line, entry.Raw);
    }

    [Fact]
    public void UnknownEventPayloadDropsTheLogTimestamp()
    {
        var line = Stamped("{\"type\":\"session.updated\",\"part\":{\"id\":\"ses_a\"}}");

        var entry = LogFormatter.Parse(line);

        Assert.NotNull(entry.ToolOutput);
        Assert.DoesNotContain("[12:34:56]", entry.ToolOutput);
        Assert.Contains("\"type\":\"session.updated\"", entry.ToolOutput);
    }

    [Fact]
    public void SessionDiffCarriesTheFilesItNames()
    {
        var line = Stamped("{\"type\":\"session.diff\",\"part\":{\"files\":[\"a.cs\",\"b.cs\"]}}");

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Unrecognized, entry.Kind);
        Assert.Equal("session.diff", entry.Text);
        Assert.Equal("a.cs\nb.cs", entry.ToolInput);
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
