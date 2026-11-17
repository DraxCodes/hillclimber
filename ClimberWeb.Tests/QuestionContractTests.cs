using System.Text.Json;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class QuestionContractTests
{
    private static JsonElement Fields(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement Form(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static LogEntry Question(QuestionState state, int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", LogKind.Question, state.Title, Question: state);

    private static LogEntry Text(string text, int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", LogKind.Text, text);

    private static LogEntry Tool(string name, int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", LogKind.Tool, name);

    private static LogEntry Start(int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", LogKind.StepStart, "step start");

    private static LogEntry Finish(int index) =>
        new($"[12:00:{index:00}] raw-{index}", "12:00:00", LogKind.StepFinish, "stop · $0.0002");

    private static QuestionState State(
        QuestionStatus status = QuestionStatus.Pending,
        string chosen = "violet",
        string formId = "frm_1") =>
        new(
            formId,
            "ses_1",
            "Which colour should the status band use?",
            "colour",
            QuestionKind.Choice,
            new[]
            {
                new QuestionOption("amber", "Amber", "the current accent"),
                new QuestionOption("violet", "Violet"),
                new QuestionOption("slate", "Slate"),
            },
            chosen,
            status);

    [Fact]
    public void StringWithOptionsIsAChoice()
    {
        var spec = QuestionSupport.Classify("Which?", Fields("""
            [{"key":"colour","type":"string","options":[
              {"value":"amber","label":"Amber","description":"warm"},
              {"value":"violet","label":"Violet"}]}]
            """));

        Assert.Equal(QuestionKind.Choice, spec.Kind);
        Assert.Equal("colour", spec.FieldKey);
        Assert.Null(spec.KindLabel);
        Assert.Equal(new[] { "amber", "violet" }, spec.Options.Select(option => option.Value));
        Assert.Equal("warm", spec.Options[0].Description);
    }

    [Fact]
    public void StringWithoutOptionsIsUnsupportedText()
    {
        var spec = QuestionSupport.Classify("Which?", Fields("""
            [{"key":"colour","type":"string"}]
            """));

        Assert.Equal(QuestionKind.Unsupported, spec.Kind);
        Assert.Equal("text answer required", spec.KindLabel);
    }

    [Fact]
    public void BooleanIsYesNoWithBooleanValues()
    {
        var spec = QuestionSupport.Classify("Confirm?", Fields("""
            [{"key":"ok","type":"boolean"}]
            """));

        Assert.Equal(QuestionKind.Boolean, spec.Kind);
        Assert.Equal(new[] { "true", "false" }, spec.Options.Select(option => option.Value));
        Assert.Equal(new[] { "Yes", "No" }, spec.Options.Select(option => option.Label));
    }

    [Theory]
    [InlineData("""[{"key":"n","type":"number"}]""", "number answer required")]
    [InlineData("""[{"key":"n","type":"integer"}]""", "whole-number answer required")]
    [InlineData("""[{"key":"m","type":"multiselect","options":[{"value":"a","label":"A"}]}]""", "several answers required")]
    [InlineData("""[{"key":"u","type":"external","url":"https://example.test"}]""", "a link is required")]
    [InlineData("""[{"key":"x","type":"future"}]""", "an unsupported answer is required")]
    public void EverythingElseIsUnsupportedWithAKindLabel(string fields, string label)
    {
        var spec = QuestionSupport.Classify("Which?", Fields(fields));

        Assert.Equal(QuestionKind.Unsupported, spec.Kind);
        Assert.Equal(label, spec.KindLabel);
    }

    [Fact]
    public void MoreThanOneFieldIsUnsupported()
    {
        var spec = QuestionSupport.Classify("Which?", Fields("""
            [{"key":"a","type":"string","options":[{"value":"x","label":"X"}]},
             {"key":"b","type":"boolean"}]
            """));

        Assert.Equal(QuestionKind.Unsupported, spec.Kind);
        Assert.Equal("more than one answer required", spec.KindLabel);
    }

    [Fact]
    public void HiddenOrConditionalFieldIsUnsupported()
    {
        Assert.Equal(
            "an unsupported answer is required",
            QuestionSupport.Classify("Which?", Fields("""[{"key":"a","type":"string","hidden":true}]""")).KindLabel);

        Assert.Equal(
            "an unsupported answer is required",
            QuestionSupport.Classify("Which?", Fields("""
                [{"key":"a","type":"string","when":[{"key":"b","op":"eq","value":"yes"}]}]
                """)).KindLabel);
    }

    [Fact]
    public void MissingTitleFallsBackToTheKey()
    {
        var spec = QuestionSupport.Classify(null, Fields("""[{"key":"colour","type":"boolean"}]"""));

        Assert.Equal("colour", spec.Title);
    }

    [Fact]
    public void CustomStringWithOptionsIsStillAChoice()
    {
        var spec = QuestionSupport.Classify("Which?", Fields("""
            [{"key":"c","type":"string","custom":true,"options":[{"value":"a","label":"A"}]}]
            """));

        Assert.Equal(QuestionKind.Choice, spec.Kind);
        Assert.Single(spec.Options);
    }

    [Fact]
    public void OptionWithoutLabelUsesItsValue()
    {
        var spec = QuestionSupport.Classify("Which?", Fields("""
            [{"key":"c","type":"string","options":[{"value":"a"}]}]
            """));

        Assert.Equal("a", spec.Options[0].Label);
    }

    [Fact]
    public void FromFormKeepsTheFormTitleAndStartsPending()
    {
        var state = QuestionSupport.FromForm(Form("""
            {"id":"frm_1","sessionID":"ses_1","title":"Which colour?","fields":[
              {"key":"colour","type":"string","options":[{"value":"amber","label":"Amber"}]}]}
            """));

        Assert.Equal("frm_1", state.FormId);
        Assert.Equal("ses_1", state.SessionId);
        Assert.Equal("Which colour?", state.Title);
        Assert.Equal(QuestionKind.Choice, state.Kind);
        Assert.Equal(QuestionStatus.Pending, state.Status);
    }

    [Fact]
    public void QuestionToolFormShowsTheCompleteQuestion()
    {
        // The installed server's question tool titles every form "Questions" and sends the real
        // ask in the field description, with a 30-char header as the field title. The row must
        // show the complete question, not the container's title.
        var state = QuestionSupport.FromForm(Form("""
            {"id":"frm_3","sessionID":"ses_1","title":"Questions","fields":[
              {"key":"q0","type":"string","title":"Which file?",
               "description":"Which of these two files should I edit?","custom":true,
               "options":[{"value":"Alpha.cs","label":"Alpha.cs"},{"value":"Beta.cs","label":"Beta.cs"}]}]}
            """));

        Assert.Equal("Which of these two files should I edit?", state.Title);
        Assert.Equal("q0", state.FieldKey);
        Assert.Equal(QuestionKind.Choice, state.Kind);
        Assert.Equal(QuestionStatus.Pending, state.Status);
        Assert.Equal(2, state.Options.Count);
    }

    [Fact]
    public void FormTitleStillBeatsTheFieldHeaderWhenThereIsNoDescription()
    {
        var spec = QuestionSupport.Classify("Keep the live dot?", Fields("""
            [{"key":"q0","type":"boolean","title":"band"}]
            """));

        Assert.Equal("Keep the live dot?", spec.Title);
    }

    [Fact]
    public void FromFormMarksUnsupportedShapesTerminal()
    {
        var state = QuestionSupport.FromForm(Form("""
            {"id":"frm_2","sessionID":"ses_1","title":"How many?","fields":[
              {"key":"count","type":"integer"}]}
            """));

        Assert.Equal(QuestionKind.Unsupported, state.Kind);
        Assert.Equal(QuestionStatus.Unsupported, state.Status);
        Assert.Equal("whole-number answer required", state.KindLabel);
    }

    [Fact]
    public void LineRoundTripsEveryField()
    {
        var state = State(QuestionStatus.Answered) with
        {
            Notice = "a notice",
            KindLabel = "a label",
        };

        var parsed = QuestionLine.Parse(QuestionLine.Write(state));

        Assert.NotNull(parsed);
        Assert.Equal(state.FormId, parsed!.FormId);
        Assert.Equal(state.SessionId, parsed.SessionId);
        Assert.Equal(state.Title, parsed.Title);
        Assert.Equal(state.FieldKey, parsed.FieldKey);
        Assert.Equal(state.Kind, parsed.Kind);
        Assert.Equal(state.Status, parsed.Status);
        Assert.Equal(state.ChosenValue, parsed.ChosenValue);
        Assert.Equal(state.Notice, parsed.Notice);
        Assert.Equal(state.KindLabel, parsed.KindLabel);
        Assert.Equal(state.Options, parsed.Options);
    }

    [Fact]
    public void LineParsesThroughTheStampedFeedFormatter()
    {
        var line = "[12:34:56] " + QuestionLine.Write(State());

        var entry = LogFormatter.Parse(line);

        Assert.Equal(LogKind.Question, entry.Kind);
        Assert.Equal("Which colour should the status band use?", entry.Text);
        Assert.NotNull(entry.Question);
        Assert.Equal("frm_1", entry.Question!.FormId);
    }

    [Fact]
    public void MalformedOrForeignLinesAreNotQuestions()
    {
        Assert.Null(QuestionLine.Parse(null));
        Assert.Null(QuestionLine.Parse("not json"));
        Assert.Null(QuestionLine.Parse("""{"type":"climber.question"}"""));
        Assert.Null(QuestionLine.Parse("""{"type":"climber.question","question":{"formId":"frm_1","kind":"nope","status":"Pending"}}"""));
        Assert.Equal(LogKind.Plain, LogFormatter.Parse("""{"type":"climber.question","question":{"formId":"frm_1","kind":"nope"}}""").Kind);
    }

    [Fact]
    public void WrittenLineCarriesNoCredentialOrEndpoint()
    {
        var line = QuestionLine.Write(State());

        Assert.DoesNotContain("password", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollapseKeepsTheFirstPositionAndTheLastState()
    {
        var answered = State(QuestionStatus.Answered) with { ChosenValue = "violet" };
        var entries = new[]
        {
            Question(State(QuestionStatus.Pending), 1),
            Text("between", 2),
            Question(answered, 3),
        };

        var collapsed = QuestionLines.Collapse(entries);

        Assert.Equal(2, collapsed.Count);
        Assert.Equal(LogKind.Question, collapsed[0].Kind);
        Assert.Equal(QuestionStatus.Answered, collapsed[0].Question!.Status);
        Assert.Equal(LogKind.Text, collapsed[1].Kind);
    }

    [Fact]
    public void GrouperMakesAQuestionItsOwnRowAndClosesTheTurn()
    {
        var items = LogGrouper.Group([
            Start(1),
            Tool("shell", 2),
            Question(State(), 3),
            Tool("read", 4),
            Finish(5),
        ]);

        Assert.Equal(new[] { OutputItemKind.Turn, OutputItemKind.Question, OutputItemKind.Turn }, items.Select(item => item.Kind));
        Assert.Equal("frm_1", items[1].Entries[0].Question!.FormId);
    }

    [Fact]
    public void GrouperCollapsesPendingAndAnsweredIntoOneRow()
    {
        var entries = new[]
        {
            Question(State(QuestionStatus.Pending), 1),
            Tool("shell", 2),
            Question(State(QuestionStatus.Answered), 3),
        };

        var items = LogGrouper.Group(entries);
        var questions = items.Where(item => item.Kind == OutputItemKind.Question).ToArray();

        var question = Assert.Single(questions);
        Assert.Equal(QuestionStatus.Answered, question.Entries[0].Question!.Status);
    }
}