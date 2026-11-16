using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class LogGrouperTests
{
    private static LogEntry Entry(LogKind kind, string text, int index, StepStatus status = StepStatus.None) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", kind, text, Status: status);

    private static LogEntry Text(string text, int index) => Entry(LogKind.Text, text, index);
    private static LogEntry Tool(string name, int index, StepStatus status = StepStatus.None) =>
        Entry(LogKind.Tool, name, index, status);
    private static LogEntry Subagent(string agent, int index) => Entry(LogKind.Subagent, agent, index);
    private static LogEntry Reasoning(string text, int index) => Entry(LogKind.Reasoning, text, index);
    private static LogEntry Start(int index) => Entry(LogKind.StepStart, "step start", index);
    private static LogEntry Finish(int index) => Entry(LogKind.StepFinish, "stop · $0.0002 · 221 in / 116 out", index);

    [Fact]
    public void EmptyInputProducesNoItems()
    {
        Assert.Empty(LogGrouper.Group(Array.Empty<LogEntry>()));
    }

    [Fact]
    public void ConsecutiveTextMergesIntoOneMessageInOrder()
    {
        var items = LogGrouper.Group([Text("one", 1), Text("two", 2)]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Message, item.Kind);
        Assert.Equal(new[] { "one", "two" }, item.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void ConsecutiveToolsJoinOneTurnInOrder()
    {
        var items = LogGrouper.Group([Tool("shell", 1), Tool("read", 2), Subagent("plan-architect", 3)]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Equal(
            new[] { "shell", "read", "plan-architect" },
            item.Entries.Select(entry => entry.Text));
        Assert.Null(item.Footer);
    }

    [Fact]
    public void StepStartOpensAndStepFinishClosesATurn()
    {
        var finish = Finish(4);
        var items = LogGrouper.Group([
            Start(1),
            Reasoning("first", 2),
            Tool("shell", 3),
            finish,
        ]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Same(finish, item.Footer);
        Assert.Equal(2, item.Entries.Count);
    }

    [Fact]
    public void ProseDuringATurnKeepsItsChronologicalPlace()
    {
        var items = LogGrouper.Group([
            Tool("shell", 1),
            Text("done", 2),
            Tool("read", 3),
        ]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Message, OutputItemKind.Turn },
            items.Select(item => item.Kind));
        Assert.Equal(new[] { "shell" }, items[0].Entries.Select(entry => entry.Text));
        Assert.Equal("done", items[1].Entries.Single().Text);
        Assert.Equal(new[] { "read" }, items[2].Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void AUserPromptIsItsOwnRowAndClosesTheWorkBeforeIt()
    {
        var prompt = Entry(LogKind.UserPrompt, "explain the code", 5);
        var items = LogGrouper.Group([
            Start(1),
            Tool("shell", 2),
            Finish(3),
            Text("it is green", 4),
            prompt,
            Tool("read", 6),
        ]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Message, OutputItemKind.Prompt, OutputItemKind.Turn },
            items.Select(item => item.Kind));
        Assert.Same(prompt, items[2].Entries.Single());
    }

    [Fact]
    public void ProseBeforeAStepFinishKeepsTheFooterOnItsTurn()
    {
        var finish = Finish(3);
        var items = LogGrouper.Group([Tool("shell", 1), Text("done", 2), finish]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Message },
            items.Select(item => item.Kind));
        Assert.Same(finish, items[0].Footer);
        Assert.Equal("done", items[1].Entries.Single().Text);
    }

    [Fact]
    public void AFooterWithNothingButProseBeforeItIsStillAMarker()
    {
        var finish = Finish(2);
        var items = LogGrouper.Group([Text("answer", 1), finish]);

        Assert.Equal(
            new[] { OutputItemKind.Message, OutputItemKind.Marker },
            items.Select(item => item.Kind));
        Assert.Same(finish, items[1].Entries[0]);
    }

    [Fact]
    public void ProseBeforeAnyActivityStaysAheadOfTheTurn()
    {
        var items = LogGrouper.Group([Text("thinking out loud", 1), Tool("shell", 2)]);

        Assert.Equal(
            new[] { OutputItemKind.Message, OutputItemKind.Turn },
            items.Select(item => item.Kind));
    }

    [Fact]
    public void StepStartBreaksConsecutiveActivityIntoTwoTurns()
    {
        var items = LogGrouper.Group([
            Tool("shell", 1),
            Start(2),
            Tool("read", 3),
        ]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Turn },
            items.Select(item => item.Kind));
    }

    [Fact]
    public void StepFinishWithNoActivityBecomesAQuietMarker()
    {
        var finish = Finish(2);
        var items = LogGrouper.Group([Text("answer", 1), finish]);

        Assert.Equal(
            new[] { OutputItemKind.Message, OutputItemKind.Marker },
            items.Select(item => item.Kind));
        Assert.Same(finish, items[1].Entries[0]);
    }

    [Fact]
    public void ErrorBecomesAnErrorRowAndEndsTheTurn()
    {
        var error = Entry(LogKind.Error, "boom", 2);
        var items = LogGrouper.Group([Tool("shell", 1), error]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Error },
            items.Select(item => item.Kind));
        Assert.Same(error, items[1].Entries[0]);
        Assert.False(items[0].Open);
    }

    [Fact]
    public void PlainBecomesAPlainRowAndEndsTheTurn()
    {
        var plain = Entry(LogKind.Plain, "$ dotnet test", 2);
        var items = LogGrouper.Group([Tool("shell", 1), plain, Tool("read", 3)]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Plain, OutputItemKind.Turn },
            items.Select(item => item.Kind));
        Assert.Same(plain, items[1].Entries[0]);
    }

    [Fact]
    public void ReasoningBetweenToolsStaysInTheSameTurn()
    {
        var items = LogGrouper.Group([
            Tool("shell", 1),
            Reasoning("first", 2),
            Reasoning("second", 3),
            Tool("read", 4),
        ]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Equal(
            new[] { "shell", "first", "second", "read" },
            item.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void EmptyReasoningIsSkippedWithoutSplittingActivity()
    {
        var items = LogGrouper.Group([Tool("shell", 1), Reasoning("  ", 2), Tool("read", 3)]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Equal(new[] { "shell", "read" }, item.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void UnrecognizedEventBetweenTurnsBecomesItsOwnQuietRow()
    {
        var unknown = Entry(LogKind.Unrecognized, "session.updated", 2);
        var items = LogGrouper.Group([Tool("shell", 1), Finish(2), unknown, Tool("read", 4)]);

        Assert.Equal(
            new[] { OutputItemKind.Turn, OutputItemKind.Unrecognized, OutputItemKind.Turn },
            items.Select(item => item.Kind));
        Assert.Same(unknown, items[1].Entries[0]);
    }

    [Fact]
    public void UnrecognizedEventInsideATurnStaysInItsPlace()
    {
        var unknown = Entry(LogKind.Unrecognized, "session.updated", 2);
        var items = LogGrouper.Group([Tool("shell", 1), unknown, Tool("read", 3)]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Equal(
            new[] { "shell", "session.updated", "read" },
            item.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void MixedTranscriptKeepsKindsAndOrder()
    {
        var items = LogGrouper.Group([
            Entry(LogKind.Plain, "===== run started =====", 0),
            Text("first", 1),
            Text("second", 2),
            Tool("shell", 3),
            Tool("read", 4),
            Finish(5),
            Text("third", 6),
            Entry(LogKind.Error, "boom", 7),
        ]);

        Assert.Equal(
            new[]
            {
                OutputItemKind.Plain,
                OutputItemKind.Message,
                OutputItemKind.Turn,
                OutputItemKind.Message,
                OutputItemKind.Error,
            },
            items.Select(item => item.Kind));
        Assert.Equal(2, items[1].Entries.Count);
        Assert.Equal(2, items[2].Entries.Count);
        Assert.NotNull(items[2].Footer);
    }

    [Fact]
    public void MergedRowsKeepEveryEntry()
    {
        var items = LogGrouper.Group([Text("one", 1), Text("two", 2), Tool("shell", 3), Tool("read", 4)]);

        Assert.Equal(
            new[] { OutputItemKind.Message, OutputItemKind.Turn },
            items.Select(item => item.Kind));
        Assert.Equal(2, items[0].Entries.Count);
        Assert.Equal(2, items[1].Entries.Count);
    }

    [Fact]
    public void AStillRunningTurnIsEmittedWithoutAFooter()
    {
        var items = LogGrouper.Group([
            Start(1),
            Reasoning("checking", 2),
            Tool("shell", 3, StepStatus.Running),
        ]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.Turn, item.Kind);
        Assert.Null(item.Footer);
        Assert.True(item.Open);
        Assert.Equal(StepStatus.Running, item.Entries[1].Status);
    }
}