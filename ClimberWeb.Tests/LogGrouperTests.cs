using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class LogGrouperTests
{
    private static LogEntry Entry(LogKind kind, string text, int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", kind, text);

    private static LogEntry Text(string text, int index) => Entry(LogKind.Text, text, index);
    private static LogEntry Tool(string name, int index) => Entry(LogKind.Tool, name, index);
    private static LogEntry Subagent(string agent, int index) => Entry(LogKind.Subagent, agent, index);

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
    public void ConsecutiveToolsMergeIntoOneTraceGroupInOrder()
    {
        var items = LogGrouper.Group([Tool("shell", 1), Tool("read", 2), Subagent("plan-architect", 3)]);

        var item = Assert.Single(items);
        Assert.Equal(OutputItemKind.TraceGroup, item.Kind);
        Assert.Equal(
            new[] { "shell", "read", "plan-architect" },
            item.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void TextBetweenToolsSplitsGroups()
    {
        var items = LogGrouper.Group([Tool("shell", 1), Text("done", 2), Tool("read", 3)]);

        Assert.Equal(
            new[] { OutputItemKind.TraceGroup, OutputItemKind.Message, OutputItemKind.TraceGroup },
            items.Select(item => item.Kind));
    }

    [Fact]
    public void StepStartBreaksTheGroupAndIsNotRendered()
    {
        var items = LogGrouper.Group([
            Tool("shell", 1),
            Entry(LogKind.StepStart, "step start", 2),
            Tool("read", 3),
        ]);

        Assert.Equal(
            new[] { OutputItemKind.TraceGroup, OutputItemKind.TraceGroup },
            items.Select(item => item.Kind));
        Assert.DoesNotContain(items, item => item.Kind == OutputItemKind.Marker);
    }

    [Fact]
    public void StepFinishBecomesAMarkerAndBreaksTheGroup()
    {
        var finish = Entry(LogKind.StepFinish, "step finish · stop", 2);
        var items = LogGrouper.Group([Tool("shell", 1), finish, Tool("read", 3)]);

        Assert.Equal(
            new[] { OutputItemKind.TraceGroup, OutputItemKind.Marker, OutputItemKind.TraceGroup },
            items.Select(item => item.Kind));
        Assert.Same(finish, items[1].Entries[0]);
    }

    [Fact]
    public void ErrorBecomesAnErrorRowAndBreaksTheGroup()
    {
        var error = Entry(LogKind.Error, "boom", 2);
        var items = LogGrouper.Group([Tool("shell", 1), error]);

        Assert.Equal(
            new[] { OutputItemKind.TraceGroup, OutputItemKind.Error },
            items.Select(item => item.Kind));
        Assert.Same(error, items[1].Entries[0]);
    }

    [Fact]
    public void PlainBecomesAPlainRowAndBreaksTheGroup()
    {
        var plain = Entry(LogKind.Plain, "$ dotnet test", 2);
        var items = LogGrouper.Group([Tool("shell", 1), plain, Tool("read", 3)]);

        Assert.Equal(
            new[] { OutputItemKind.TraceGroup, OutputItemKind.Plain, OutputItemKind.TraceGroup },
            items.Select(item => item.Kind));
        Assert.Same(plain, items[1].Entries[0]);
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
            Entry(LogKind.StepFinish, "step finish · stop", 5),
            Text("third", 6),
            Entry(LogKind.Error, "boom", 7),
        ]);

        Assert.Equal(
            new[]
            {
                OutputItemKind.Plain,
                OutputItemKind.Message,
                OutputItemKind.TraceGroup,
                OutputItemKind.Marker,
                OutputItemKind.Message,
                OutputItemKind.Error,
            },
            items.Select(item => item.Kind));
        Assert.Equal(2, items[1].Entries.Count);
        Assert.Equal(2, items[2].Entries.Count);
    }

    [Fact]
    public void MergedGroupsKeepEveryEntry()
    {
        var items = LogGrouper.Group([Text("one", 1), Text("two", 2), Tool("shell", 3), Tool("read", 4)]);

        Assert.Equal(
            new[] { OutputItemKind.Message, OutputItemKind.TraceGroup },
            items.Select(item => item.Kind));
        Assert.Equal(2, items[0].Entries.Count);
        Assert.Equal(2, items[1].Entries.Count);
    }
}