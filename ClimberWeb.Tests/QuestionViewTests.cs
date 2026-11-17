using ClimberWeb.Components.Ui.Ai;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class QuestionViewTests
{
    [Fact]
    public void APendingChoiceAsksWithItsTitleAndOptions()
    {
        var view = QuestionView.For(State());

        Assert.True(view.ShowPrefix);
        Assert.True(view.Interactive);
        Assert.False(view.Busy);
        Assert.Equal("asks:", QuestionView.Copy.Prefix);
        Assert.Equal("Which colour should the status band use?", view.Title);
        Assert.Equal("↑↓ move · enter commits", view.Hint);
        Assert.Equal("none fit? answer via the composer.", view.Escape);
        Assert.Null(view.StatusLine);
        Assert.Equal(new[] { "Amber", "Violet", "Slate" }, view.Options.Select(option => option.Label));
        Assert.All(view.Options, option => Assert.False(option.IsChosen));
        Assert.Equal(QuestionAction.None, view.Action);
        Assert.True(view.ShowsOptions);
    }

    [Fact]
    public void TheBooleanPairIsTheServersOwnYesAndNo()
    {
        var view = QuestionView.For(State(
            kind: QuestionKind.Boolean,
            options: new[]
            {
                new QuestionOption("true", "Yes"),
                new QuestionOption("false", "No"),
            }));

        Assert.True(view.Interactive);
        Assert.Equal(new[] { "Yes", "No" }, view.Options.Select(option => option.Label));
        Assert.Equal(new[] { "true", "false" }, view.Options.Select(option => option.Value));
    }

    [Fact]
    public void SubmittingKeepsTheRowButDisablesIt()
    {
        var view = QuestionView.For(State(QuestionStatus.Submitting, chosen: "violet"));

        Assert.True(view.Busy);
        Assert.False(view.Interactive);
        Assert.Null(view.Hint);
        Assert.Null(view.Escape);
        Assert.Equal("sending…", view.StatusLine);
        Assert.True(view.ShowPrefix);
    }

    [Fact]
    public void AnsweredFoldsToTheChosenLabelVerbatim()
    {
        var view = QuestionView.For(State(QuestionStatus.Answered, chosen: "violet"));

        Assert.True(view.IsFold);
        Assert.False(view.FoldOpenByDefault);
        Assert.Equal("answered: Violet", view.FoldedLabel);
        Assert.Equal("violet", view.Options.Single(option => option.IsChosen).Value);
        Assert.All(
            view.Options.Where(option => !option.IsChosen),
            option => Assert.False(option.IsChosen));
    }

    [Fact]
    public void AnsweredWithNoKnownLabelStillReadsAsAnswered()
    {
        var view = QuestionView.For(State(QuestionStatus.Answered, chosen: "violet") with
        {
            Options = Array.Empty<QuestionOption>(),
        });

        Assert.Equal("answered: violet", view.FoldedLabel);
    }

    [Fact]
    public void CancelledIsOneQuietLine()
    {
        var view = QuestionView.For(State(QuestionStatus.Cancelled));

        Assert.False(view.Interactive);
        Assert.Equal("cancelled · no answer sent", view.StatusLine);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    [Fact]
    public void RestoredPendingIsClosedAndSaysNoAnswerWasSent()
    {
        var view = QuestionView.For(State(QuestionStatus.Stale));

        Assert.False(view.Interactive);
        Assert.False(view.ShowPrefix);
        Assert.Equal("no answer sent", view.StatusLine);
        Assert.Null(view.Hint);
        Assert.Equal("Which colour should the status band use?", view.Title);
    }

    [Fact]
    public void QuestionRestoreFoldsAPendingRowIntoTheStaleCopy()
    {
        var restored = QuestionRestore.Apply(State());

        Assert.True(restored.Restored);
        Assert.Equal(QuestionStatus.Stale, restored.Status);

        var view = QuestionView.For(restored);
        Assert.False(view.Interactive);
        Assert.False(view.AwaitsAnswer);
        Assert.False(view.ShowPrefix);
        Assert.Equal("no answer sent", view.StatusLine);
        Assert.Equal(string.Empty, view.Announcement);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    [Fact]
    public void ARestoredTerminalRowKeepsItsCopyButOffersNoAction()
    {
        var unsupported = QuestionView.For(State(
            QuestionStatus.Unsupported,
            kind: QuestionKind.Unsupported,
            options: Array.Empty<QuestionOption>(),
            kindLabel: "several answers required") with { Restored = true });
        var refused = QuestionView.For(State(QuestionStatus.Refused) with { Restored = true });
        var unconfirmed = QuestionView.For(State(QuestionStatus.Unconfirmed) with { Restored = true });

        Assert.Equal("several answers required", unsupported.Notice);
        Assert.Equal("that answer was refused", refused.StatusLine);
        Assert.Equal("submit unconfirmed", unconfirmed.StatusLine);
        Assert.All(
            new[] { unsupported, refused, unconfirmed },
            view =>
            {
                Assert.False(view.Interactive);
                Assert.Equal(QuestionAction.None, view.Action);
            });
    }

    [Fact]
    public void ARestoredAnsweredRowStaysFoldedAndClosed()
    {
        var view = QuestionView.For(State(QuestionStatus.Answered, chosen: "violet") with { Restored = true });

        Assert.True(view.IsFold);
        Assert.False(view.FoldOpenByDefault);
        Assert.Equal("answered: Violet", view.FoldedLabel);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    [Fact]
    public void AnUnsupportedShapeOffersOnlyDismissAndNamesWhatIsNeeded()
    {
        var view = QuestionView.For(State(
            QuestionStatus.Unsupported,
            kind: QuestionKind.Unsupported,
            options: Array.Empty<QuestionOption>(),
            kindLabel: "number answer required"));

        Assert.True(view.ShowPrefix);
        Assert.False(view.Interactive);
        Assert.Equal("number answer required", view.Notice);
        Assert.Equal(QuestionAction.Dismiss, view.Action);
        Assert.Equal("dismiss", view.ActionLabel);
        Assert.False(view.ShowsOptions);
    }

    [Fact]
    public void AnUnsupportedShapeWithNoLabelStillNamesTheProblem()
    {
        var view = QuestionView.For(State(
            QuestionStatus.Unsupported,
            kind: QuestionKind.Unsupported,
            options: Array.Empty<QuestionOption>()));

        Assert.Equal("an unsupported answer is required", view.Notice);
    }

    [Fact]
    public void RefusedAndUnconfirmedOfferCheck()
    {
        var refused = QuestionView.For(State(QuestionStatus.Refused));
        var unconfirmed = QuestionView.For(State(QuestionStatus.Unconfirmed));

        Assert.Equal("that answer was refused", refused.StatusLine);
        Assert.Equal("submit unconfirmed", unconfirmed.StatusLine);
        Assert.Equal(QuestionAction.Check, refused.Action);
        Assert.Equal("check", refused.ActionLabel);
        Assert.Equal(QuestionAction.Check, unconfirmed.Action);
    }

    [Fact]
    public void UnavailableIsTerminalAndQuiet()
    {
        var view = QuestionView.For(State(QuestionStatus.Unavailable));

        Assert.Equal("questions unavailable", view.StatusLine);
        Assert.False(view.Interactive);
        Assert.Equal(QuestionAction.None, view.Action);
    }

    [Fact]
    public void ALongLabelIsKeptVerbatim()
    {
        var label = "Use the shipped amber accent but soften it by one step so the band does not shout";
        var view = QuestionView.For(State(options: new[] { new QuestionOption("amber", label) }));

        Assert.Equal(label, Assert.Single(view.Options).Label);
    }

    [Fact]
    public void TheAnnouncementNamesTheFirstOption()
    {
        var view = QuestionView.For(State());

        Assert.Equal(
            "question from the run · focus moved to Amber",
            view.Announcement);
    }

    [Fact]
    public void AppCopyNeverMentionsPermissions()
    {
        foreach (var status in Enum.GetValues<QuestionStatus>())
        {
            var view = QuestionView.For(State(status, kind: QuestionKind.Unsupported));
            var text = string.Join(
                ' ',
                view.Title,
                view.Hint,
                view.Escape,
                view.StatusLine,
                view.Notice,
                view.FoldedLabel,
                view.ActionLabel,
                string.Join(' ', view.Options.Select(option => option.Label)));

            Assert.DoesNotContain("permission", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("allow", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deny", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void APendingRowAsksTheBandForTheAwaitingWord()
    {
        Assert.True(QuestionView.For(State()).AwaitsAnswer);
        Assert.False(QuestionView.For(State(QuestionStatus.Answered, chosen: "violet")).AwaitsAnswer);
        Assert.Equal("awaiting answer", QuestionView.Copy.AwaitingAnswer);
    }

    // --------------------------------------------------------------------- helpers

    private static QuestionState State(
        QuestionStatus status = QuestionStatus.Pending,
        QuestionKind kind = QuestionKind.Choice,
        string? chosen = null,
        IReadOnlyList<QuestionOption>? options = null,
        string? kindLabel = null) =>
        new(
            "frm_1",
            "ses_1",
            "Which colour should the status band use?",
            "colour",
            kind,
            options ?? new[]
            {
                new QuestionOption("amber", "Amber", "the current accent"),
                new QuestionOption("violet", "Violet"),
                new QuestionOption("slate", "Slate"),
            },
            chosen,
            status,
            null,
            kindLabel);
}