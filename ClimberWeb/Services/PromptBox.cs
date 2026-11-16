namespace ClimberWeb.Services;

/// <summary>
/// The prompt box's behaviour around a launch: the text being typed, the last prompt a run
/// took, and the focus the emptied box still owes. Pure, so the rules are testable without a
/// browser.
/// </summary>
public sealed class PromptBox
{
    /// <summary>The textarea's text; assigned through the page's binding.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The last prompt a launch accepted, offered back while the box is idle and empty.</summary>
    public string? LastPrompt { get; private set; }

    /// <summary>True while the emptied box still owes a focus once it is usable again.</summary>
    public bool OwesFocus { get; private set; }

    /// <summary>
    /// Runs <paramref name="start"/> and, only when it returns without throwing, takes the
    /// prompt it was given. A rejected launch — a concurrent run, invalid input — leaves the
    /// box untouched so the text is still there to fix and retry.
    /// </summary>
    /// <returns>The failure the start raised, or null when it started.</returns>
    public Exception? Launch(string prompt, Action start)
    {
        ArgumentNullException.ThrowIfNull(start);

        try
        {
            start();
        }
        catch (Exception ex)
        {
            return ex;
        }

        LastPrompt = prompt;
        Text = string.Empty;
        OwesFocus = true;
        return null;
    }

    /// <summary>Whether the last prompt can be offered back: idle, empty box, something remembered.</summary>
    public bool CanRecall(bool running) =>
        !running && !string.IsNullOrWhiteSpace(LastPrompt) && string.IsNullOrWhiteSpace(Text);

    /// <summary>Puts the last prompt back. Returns false when nothing has been started yet.</summary>
    public bool Recall()
    {
        if (LastPrompt is not { } last)
        {
            return false;
        }

        Text = last;
        return true;
    }

    /// <summary>
    /// Drops the remembered prompt, for a new conversation. Recalling the previous session's
    /// prompt into a fresh one would put the wrong words in the composer.
    /// </summary>
    public void Forget()
    {
        LastPrompt = null;
        OwesFocus = false;
    }

    /// <summary>
    /// Claims the pending focus once the box can take it: the page unlocked and no run holding
    /// the (disabled) textarea. The flag clears first, so a launch focuses at most once.
    /// </summary>
    public bool TakeFocus(bool unlocked, bool running)
    {
        if (!OwesFocus || !unlocked || running)
        {
            return false;
        }

        OwesFocus = false;
        return true;
    }
}