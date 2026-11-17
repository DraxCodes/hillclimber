using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The serve producer's whole contract with the renderer: the served events must come out as the
/// same JSON lines <c>opencode run --format json</c> prints, so nothing downstream can tell which
/// producer fed it. Every case is a synthesized payload — no process, no tokens.
/// </summary>
public class ServeEventMapperTests
{
    private const string Session = "ses_2b9f4c1a";

    private static OpenCodeEvent Event(string type, string data)
    {
        var payload = $$"""{"id":"evt_1","created":1794553340000,"type":"{{type}}","data":{{data}}}""";
        Assert.True(OpenCodeFormClient.TryParseEvent(payload, out var value));
        return value;
    }

    private static string Own(string json) => json.Replace("{session}", Session, StringComparison.Ordinal);

    [Fact]
    public void TextIsEmittedOnceWhenTheBlockEnds()
    {
        var mapper = new ServeEventMapper(Session);

        Assert.Empty(mapper.Map(Event("session.text.started",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0}"""))));
        Assert.Empty(mapper.Map(Event("session.text.delta",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"delta":"Hel"}"""))));

        var ended = mapper.Map(Event("session.text.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"Hello there"}""")));

        var line = Assert.Single(ended);
        var entry = LogFormatter.Parse(line);
        Assert.Equal(LogKind.Text, entry.Kind);
        Assert.Equal("Hello there", entry.Text);

        // A durable replay of the same block must not print it twice.
        Assert.Empty(mapper.Map(Event("session.text.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"Hello there"}"""))));
    }

    [Fact]
    public void ASecondBlockInTheSameMessageStillGetsItsOwnLine()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.text.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"first"}""")));
        var line = Assert.Single(mapper.Map(Event("session.text.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":1,"text":"second"}"""))));

        Assert.Equal("second", LogFormatter.Parse(line).Text);
    }

    [Fact]
    public void ReasoningIsEmittedOnceAsReasoning()
    {
        var mapper = new ServeEventMapper(Session);

        var lines = mapper.Map(Event("session.reasoning.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"thinking it over"}""")));

        var entry = LogFormatter.Parse(Assert.Single(lines));
        Assert.Equal(LogKind.Reasoning, entry.Kind);
        Assert.Equal("thinking it over", entry.Text);
    }

    [Fact]
    public void AToolKeepsItsNameInputAndOutputAcrossThreeEvents()
    {
        var mapper = new ServeEventMapper(Session);

        Assert.Empty(mapper.Map(Event("session.tool.input.started",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","name":"shell"}"""))));

        var running = Assert.Single(mapper.Map(Event("session.tool.called",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","executed":true,"input":{"command":"dotnet test","description":"Run tests"}}"""))));

        var runningEntry = LogFormatter.Parse(running);
        Assert.Equal(LogKind.Tool, runningEntry.Kind);
        Assert.Equal("shell", runningEntry.Text);
        Assert.Equal(StepStatus.Running, runningEntry.Status);
        Assert.Equal("dotnet test", runningEntry.Command);

        var done = Assert.Single(mapper.Map(Event("session.tool.success",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","executed":true,"content":[{"type":"text","text":"Passed!"},{"type":"file","uri":"file:///a.png","mime":"image/png"}],"metadata":{"exit":0}}"""))));

        var doneEntry = LogFormatter.Parse(done);
        Assert.Equal("shell", doneEntry.Text);
        Assert.Equal(StepStatus.None, doneEntry.Status);
        Assert.Equal("Passed!", doneEntry.ToolOutput);
        // The input is remembered from `called`, so the finished row still names the command.
        Assert.Equal("dotnet test", doneEntry.Command);
    }

    [Fact]
    public void AFailedToolKeepsTheServersMessage()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.tool.called",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","executed":true,"input":{"path":"a.ts"}}""")));
        mapper.Map(Event("session.tool.input.started",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","name":"read"}""")));

        var lines = mapper.Map(Event("session.tool.failed",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","executed":true,"error":{"type":"NotFound","message":"no such file"},"content":[]}""")));

        var entry = LogFormatter.Parse(Assert.Single(lines));
        Assert.Equal(StepStatus.Failed, entry.Status);
        Assert.Equal("no such file", entry.ToolOutput);
    }

    [Fact]
    public void AStepEndBecomesTheStepFinishLineTheMetricsAlreadyRead()
    {
        var mapper = new ServeEventMapper(Session);

        var lines = mapper.Map(Event("session.step.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","finish":"tool-calls","cost":0.0125,"tokens":{"input":120,"output":30,"reasoning":4,"cache":{"read":400,"write":10}}}""")));

        var entry = LogFormatter.Parse(Assert.Single(lines));
        Assert.Equal(LogKind.StepFinish, entry.Kind);
        Assert.Contains("tool calls", entry.Text);
        Assert.Contains("$0.0125", entry.Text);

        var metrics = RunMetrics.Zero.Add(lines[0]);
        Assert.Equal(0.0125m, metrics.SpendUsd);
        Assert.Equal(120, metrics.InputTokens);
        Assert.Equal(30, metrics.OutputTokens);
        Assert.Equal(400, metrics.CacheReadTokens);
        Assert.Equal(10, metrics.CacheWriteTokens);
    }

    [Fact]
    public void AForeignSessionProducesNothing()
    {
        var mapper = new ServeEventMapper(Session);

        Assert.Empty(mapper.Map(Event("session.text.ended",
            """{"sessionID":"ses_other","assistantMessageID":"msg_9","ordinal":0,"text":"not ours"}""")));
    }

    [Fact]
    public void UnmappedFamiliesProduceNothing()
    {
        var mapper = new ServeEventMapper(Session);

        Assert.Empty(mapper.Map(Event("form.created", Own("""{"sessionID":"{session}","formID":"frm_1"}"""))));
        Assert.Empty(mapper.Map(Event("permission.asked", Own("""{"sessionID":"{session}"}"""))));
        Assert.Empty(mapper.Map(Event("session.tool.progress",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","id":"call_1","metadata":{}}"""))));
        Assert.Equal(ServeTurnEnd.Running, mapper.End);
    }

    [Fact]
    public void ATerminalEventBeforeTheTurnBeganIsIgnored()
    {
        var mapper = new ServeEventMapper(Session);

        Assert.Empty(mapper.Map(Event("session.execution.succeeded", Own("""{"sessionID":"{session}"}"""))));
        Assert.Equal(ServeTurnEnd.Running, mapper.End);
        Assert.False(mapper.Armed);
    }

    [Fact]
    public void AnIdleSessionDoesNotEndATurnThatNeverStarted()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.idle", Own("""{"sessionID":"{session}"}""")));

        Assert.Equal(ServeTurnEnd.Running, mapper.End);
    }

    [Fact]
    public void StartedThenSucceededEndsTheTurn()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        mapper.Map(Event("session.execution.succeeded", Own("""{"sessionID":"{session}"}""")));

        Assert.True(mapper.Armed);
        Assert.Equal(ServeTurnEnd.Succeeded, mapper.End);
    }

    [Fact]
    public void StartedThenIdleAlsoEndsTheTurn()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        mapper.Map(Event("session.idle", Own("""{"sessionID":"{session}"}""")));

        Assert.Equal(ServeTurnEnd.Succeeded, mapper.End);
    }

    [Fact]
    public void AFailedExecutionEndsTheTurnWithTheServersMessage()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        var lines = mapper.Map(Event("session.execution.failed",
            Own("""{"sessionID":"{session}","error":{"type":"ProviderError","message":"quota exhausted"}}""")));

        Assert.Equal(ServeTurnEnd.Failed, mapper.End);
        Assert.Equal("quota exhausted", mapper.Failure);

        var entry = LogFormatter.Parse(Assert.Single(lines));
        Assert.Equal(LogKind.Error, entry.Kind);
        Assert.Equal("quota exhausted", entry.Text);
    }

    [Fact]
    public void AFailedStepAndAFailedExecutionPrintOneRowNotTwo()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        var step = mapper.Map(Event("session.step.failed",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","error":{"type":"provider.auth","message":"User not found."}}""")));
        var execution = mapper.Map(Event("session.execution.failed",
            Own("""{"sessionID":"{session}","error":{"type":"provider.auth","message":"User not found."}}""")));

        var entry = LogFormatter.Parse(Assert.Single(step));
        Assert.Equal("User not found.", entry.Text);
        Assert.Empty(execution);
        Assert.Equal(ServeTurnEnd.Failed, mapper.End);
    }

    [Fact]
    public void AnInterruptedExecutionEndsTheTurn()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        mapper.Map(Event("session.execution.interrupted",
            Own("""{"sessionID":"{session}","reason":"user"}""")));

        Assert.Equal(ServeTurnEnd.Interrupted, mapper.End);
    }

    [Fact]
    public void NothingIsEmittedAfterTheTurnEnded()
    {
        var mapper = new ServeEventMapper(Session);

        mapper.Map(Event("session.execution.started", Own("""{"sessionID":"{session}"}""")));
        mapper.Map(Event("session.execution.succeeded", Own("""{"sessionID":"{session}"}""")));

        Assert.Empty(mapper.Map(Event("session.text.ended",
            Own("""{"sessionID":"{session}","assistantMessageID":"msg_1","ordinal":0,"text":"late"}"""))));
    }
}