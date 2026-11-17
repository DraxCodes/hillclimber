using ClimberWeb.Services;

namespace ClimberWeb.Components.Ui.Ai;

/// <summary>The one action a settled or unsupported row offers.</summary>
public enum QuestionAction
{
    None,
    Dismiss,
    Check,
}

/// <summary>One option as the row draws it, with its taken mark resolved.</summary>
public sealed record QuestionOptionView(
    string Value,
    string Label,
    string? Description,
    bool IsChosen);

/// <summary>
/// The row's copy and interaction state, derived from one <see cref="QuestionState"/>. It is
/// pure so every string the reader sees is pinned by a test, and the markup only arranges it.
/// </summary>
public sealed record QuestionView(
    QuestionState State,
    string Title,
    bool ShowPrefix,
    bool Interactive,
    bool Busy,
    bool IsFold,
    bool FoldOpenByDefault,
    string? Hint,
    string? Escape,
    string? StatusLine,
    string? Notice,
    QuestionAction Action,
    IReadOnlyList<QuestionOptionView> Options,
    string FoldedLabel,
    string Announcement)
{
    /// <summary>The exact copy. App copy is terminal-quiet and never mentions permissions.</summary>
    public static class Copy
    {
        public const string Prefix = "asks:";
        public const string Hint = "↑↓ move · enter commits";
        public const string Escape = "none fit? answer via the composer.";
        public const string AnsweredPrefix = "answered: ";
        public const string ChosenMark = "→ ";
        public const string ChosenForScreenReader = "chosen";
        public const string Cancelled = "cancelled · no answer sent";
        public const string Stale = "no answer sent";
        public const string Dismiss = "dismiss";
        public const string Submitting = "sending…";
        public const string Refused = "that answer was refused";
        public const string Unconfirmed = "submit unconfirmed";
        public const string Unavailable = "questions unavailable";
        public const string Check = "check";
        public const string UnknownKind = "an unsupported answer is required";
        public const string AwaitingAnswer = "awaiting answer";
    }

    /// <summary>Builds the view for <paramref name="state"/>.</summary>
    public static QuestionView For(QuestionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var chosen = state.Options.FirstOrDefault(
            option => string.Equals(option.Value, state.ChosenValue, StringComparison.Ordinal));
        var chosenLabel = chosen?.Label ?? state.ChosenValue;

        var options = state.Options
            .Select(option => new QuestionOptionView(
                option.Value,
                option.Label,
                option.Description,
                state.Status == QuestionStatus.Answered
                    && string.Equals(option.Value, state.ChosenValue, StringComparison.Ordinal)))
            .ToArray();

        var (showPrefix, interactive, busy, isFold, statusLine, action) = state.Status switch
        {
            QuestionStatus.Pending => (true, true, false, false, (string?)null, QuestionAction.None),
            QuestionStatus.Submitting => (true, false, true, false, Copy.Submitting, QuestionAction.None),
            QuestionStatus.Answered => (false, false, false, true, null, QuestionAction.None),
            QuestionStatus.Cancelled => (false, false, false, false, Copy.Cancelled, QuestionAction.None),
            QuestionStatus.Unsupported => (true, false, false, false, null, QuestionAction.Dismiss),
            QuestionStatus.Refused => (true, false, false, false, Copy.Refused, QuestionAction.Check),
            QuestionStatus.Unconfirmed => (true, false, false, false, Copy.Unconfirmed, QuestionAction.Check),
            QuestionStatus.Stale => (false, false, false, false, Copy.Stale, QuestionAction.None),
            _ => (false, false, false, false, Copy.Unavailable, QuestionAction.None),
        };

        var notice = state.Status == QuestionStatus.Unsupported
            ? state.KindLabel ?? Copy.UnknownKind
            : null;

        var folded = state.Status == QuestionStatus.Answered
            ? string.IsNullOrWhiteSpace(chosenLabel)
                ? Copy.AnsweredPrefix.TrimEnd()
                : Copy.AnsweredPrefix + chosenLabel
            : string.Empty;

        var firstLabel = state.Options.Count > 0 ? state.Options[0].Label : state.Title;
        var announcement = state.Status == QuestionStatus.Pending
            ? $"question from the run · focus moved to {firstLabel}"
            : string.Empty;

        return new QuestionView(
            state,
            state.Title,
            showPrefix,
            interactive,
            busy,
            isFold,
            FoldOpenByDefault: false,
            Hint: interactive ? Copy.Hint : null,
            Escape: interactive ? Copy.Escape : null,
            statusLine,
            notice,
            action,
            options,
            folded,
            announcement);
    }

    /// <summary>The status-band word while a question waits for the reader.</summary>
    public bool AwaitsAnswer => State.Status == QuestionStatus.Pending;

    /// <summary>The action's visible label, or null when the row offers none.</summary>
    public string? ActionLabel => Action switch
    {
        QuestionAction.Dismiss => Copy.Dismiss,
        QuestionAction.Check => Copy.Check,
        _ => null,
    };

    /// <summary>True when the options are the row's body and take answer weight.</summary>
    public bool ShowsOptions => State.Kind != QuestionKind.Unsupported && Options.Count > 0;
}