using ClimberWeb.Services;

namespace ClimberWeb.Tests;

/// <summary>
/// The prompt box after a launch: it clears, remembers the last prompt, offers it back while
/// idle, and keeps the text when the launch is rejected.
/// </summary>
public class PromptBoxTests
{
    // ---- a successful launch -------------------------------------------------

    [Fact]
    public void Launch_ClearsTheBoxAndRemembersWhatItSent()
    {
        var box = new PromptBox { Text = "  add a test  " };

        var failure = box.Launch("add a test", () => { });

        Assert.Null(failure);
        Assert.Equal(string.Empty, box.Text);
        Assert.Equal("add a test", box.LastPrompt);
    }

    [Fact]
    public void Launch_StartsTheRunAndOwesOneFocus()
    {
        var box = new PromptBox { Text = "do the thing" };
        var started = 0;

        box.Launch("do the thing", () => started++);

        Assert.Equal(1, started);
        Assert.True(box.OwesFocus);
    }

    // ---- a rejected launch (the fallback-approval trap's sibling) ------------

    [Fact]
    public void Launch_KeepsTheTextAndTheLastPromptWhenTheStartThrows()
    {
        var box = new PromptBox { Text = "  " };
        box.Launch("first run", () => { });
        box.Text = "second run";

        var failure = box.Launch("second run", () => throw new InvalidOperationException("A prompt is required."));

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("second run", box.Text);
        Assert.Equal("first run", box.LastPrompt);
    }

    [Fact]
    public void Launch_DoesNotOweFocusWhenTheStartThrows()
    {
        var box = new PromptBox { Text = "nope" };

        box.Launch("nope", () => throw new ArgumentException("A target folder is required."));

        Assert.False(box.OwesFocus);
        Assert.Null(box.LastPrompt);
    }

    // ---- offering the last prompt back --------------------------------------

    [Fact]
    public void CanRecall_IsFalseUntilSomethingHasStarted()
    {
        var box = new PromptBox();

        Assert.False(box.CanRecall(running: false));
    }

    [Fact]
    public void CanRecall_IsTrueOnceIdleWithAnEmptyBox()
    {
        var box = new PromptBox();
        box.Launch("tidy the tests", () => { });

        Assert.True(box.CanRecall(running: false));
    }

    [Fact]
    public void CanRecall_IsFalseWhileARunHoldsTheBox()
    {
        var box = new PromptBox();
        box.Launch("tidy the tests", () => { });

        Assert.False(box.CanRecall(running: true));
    }

    [Theory]
    [InlineData("new text", false)]
    [InlineData("   ", true)]
    public void CanRecall_TreatsWhitespaceAsAnEmptyBox(string text, bool expected)
    {
        var box = new PromptBox();
        box.Launch("tidy the tests", () => { });
        box.Text = text;

        Assert.Equal(expected, box.CanRecall(running: false));
    }

    [Fact]
    public void Recall_RestoresExactlyWhatWasSent()
    {
        var box = new PromptBox();
        box.Launch("Reply with the single word: ok", () => { });

        Assert.True(box.Recall());
        Assert.Equal("Reply with the single word: ok", box.Text);
    }

    [Fact]
    public void Recall_HidesTheAffordanceOnceTheBoxHasTextAgain()
    {
        var box = new PromptBox();
        box.Launch("tidy the tests", () => { });

        box.Recall();

        Assert.False(box.CanRecall(running: false));
    }

    [Fact]
    public void Recall_DoesNothingBeforeAnyLaunch()
    {
        var box = new PromptBox { Text = "typed but never started" };

        Assert.False(box.Recall());
        Assert.Equal("typed but never started", box.Text);
    }

    [Fact]
    public void Recall_KeepsTheLastPromptForAnotherRecall()
    {
        var box = new PromptBox();
        box.Launch("tidy the tests", () => { });

        box.Recall();
        box.Text = string.Empty;

        Assert.True(box.Recall());
        Assert.Equal("tidy the tests", box.Text);
    }

    // ---- the focus the emptied box owes -------------------------------------

    [Fact]
    public void TakeFocus_WaitsForAnUnlockedIdleBox()
    {
        var box = new PromptBox();
        box.Launch("go", () => { });

        Assert.False(box.TakeFocus(unlocked: true, running: true));
        Assert.False(box.TakeFocus(unlocked: false, running: false));
        Assert.True(box.TakeFocus(unlocked: true, running: false));
    }

    [Fact]
    public void TakeFocus_IsClaimedOnlyOncePerLaunch()
    {
        var box = new PromptBox();
        box.Launch("go", () => { });

        Assert.True(box.TakeFocus(unlocked: true, running: false));
        Assert.False(box.TakeFocus(unlocked: true, running: false));
        Assert.False(box.OwesFocus);
    }

    [Fact]
    public void TakeFocus_IsNotOwedAfterARejectedLaunch()
    {
        var box = new PromptBox();
        box.Launch("go", () => throw new InvalidOperationException("A run is already in progress."));

        Assert.False(box.TakeFocus(unlocked: true, running: false));
    }
}