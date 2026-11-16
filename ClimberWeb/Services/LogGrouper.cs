namespace ClimberWeb.Services;

/// <summary>The shape of a rendered transcript item.</summary>
public enum OutputItemKind
{
    /// <summary>One or more consecutive <see cref="LogKind.Text"/> entries.</summary>
    Message,

    /// <summary>The prompt that opened a turn, rendered where a chat shows the reader's own question.</summary>
    Prompt,

    /// <summary>One step's reasoning and tool calls, folded behind a summary.</summary>
    Turn,

    /// <summary>A JSON event this renderer does not recognise; raw payload behind a disclosure.</summary>
    Unrecognized,

    /// <summary>A footer with no activity before it, rendered as one quiet line.</summary>
    Marker,

    /// <summary>An opencode <c>error</c> event.</summary>
    Error,

    /// <summary>Anything that is not a recognised event; rendered verbatim.</summary>
    Plain,
}

/// <summary>One transcript row: a message, a turn of activity, or a single marker/plain line.</summary>
/// <param name="Kind">How the row should be rendered.</param>
/// <param name="Entries">The entries in chronological order; one for a single-row kind.</param>
/// <param name="Footer">The <c>step_finish</c> that closed a turn, or null when it never arrived.</param>
/// <param name="Open">True only for the feed's last turn, which was still streaming.</param>
public sealed record OutputItem(
    OutputItemKind Kind,
    IReadOnlyList<LogEntry> Entries,
    LogEntry? Footer = null,
    bool Open = false);

/// <summary>
/// Folds a run's display-ready log entries into transcript rows. A <c>step_start</c> opens a
/// turn, its reasoning and tool calls join it, and <c>step_finish</c> closes it with a footer.
/// Prose belongs to the reader, not the fold: text is never hidden inside a turn, so text that
/// arrives during one closes that turn and renders in place, and the activity after it opens
/// the next. Unknown events join the turn they arrive in, or stand alone between turns. Errors
/// and plain lines end the current turn and render as their own rows, and so does a
/// restored session's prompt. Pure and order-preserving, so it is cheap to test and to
/// call on every render.
/// </summary>
public static class LogGrouper
{
    /// <summary>Groups <paramref name="entries"/>, which must be in chronological order.</summary>
    public static IReadOnlyList<OutputItem> Group(IReadOnlyList<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return Array.Empty<OutputItem>();
        }

        var items = new List<OutputItem>();
        var turn = new List<LogEntry>();

        // Text is the answer, so it is never folded. Consecutive text becomes one message.
        var message = new List<LogEntry>();

        void FlushMessage()
        {
            if (message.Count == 0)
            {
                return;
            }

            items.Add(new OutputItem(OutputItemKind.Message, message.ToArray()));
            message.Clear();
        }

        void FlushTurn(LogEntry? footer = null, bool open = false)
        {
            if (turn.Count > 0)
            {
                items.Add(new OutputItem(OutputItemKind.Turn, turn.ToArray(), footer, open));
            }
            else if (footer is not null && !AttachToLastTurn(footer))
            {
                // A step that produced only prose still has a cost worth one quiet line.
                items.Add(new OutputItem(OutputItemKind.Marker, new[] { footer }));
            }

            turn.Clear();
        }

        // Prose closes a turn before its step finishes, so the footer can arrive with no open
        // turn. It still belongs to that work: look back past the prose it answered and give
        // the footer to that turn, rather than to a line of its own.
        bool AttachToLastTurn(LogEntry footer)
        {
            for (var index = items.Count - 1; index >= 0; index--)
            {
                if (items[index].Kind == OutputItemKind.Message)
                {
                    continue;
                }

                if (items[index] is { Kind: OutputItemKind.Turn, Footer: null } last)
                {
                    items[index] = last with { Footer = footer };
                    return true;
                }

                return false;
            }

            return false;
        }

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                // Prose is the reader's, so it renders where it arrived: closing the turn
                // keeps it out of the fold instead of pushing it past the work that follows.
                case LogKind.Text:
                    FlushTurn();
                    message.Add(entry);
                    break;

                // A thought belongs with the activity it explains. An empty thought
                // carries nothing to show and must not open a turn of its own.
                case LogKind.Reasoning:
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                    {
                        FlushMessage();
                        turn.Add(entry);
                    }

                    break;

                case LogKind.Tool:
                case LogKind.Subagent:
                    FlushMessage();
                    turn.Add(entry);
                    break;

                // A step start opens a turn but carries nothing worth showing.
                case LogKind.StepStart:
                    FlushMessage();
                    FlushTurn();
                    break;

                case LogKind.StepFinish:
                    FlushMessage();
                    FlushTurn(entry);
                    break;

                // An unknown event is not a boundary: inside a turn it folds in as one quiet
                // row, and between turns it stands alone rather than opening a turn of its own.
                case LogKind.Unrecognized:
                    if (turn.Count > 0)
                    {
                        turn.Add(entry);
                        break;
                    }

                    FlushMessage();
                    items.Add(new OutputItem(OutputItemKind.Unrecognized, new[] { entry }));
                    break;

                case LogKind.Error:
                    FlushMessage();
                    FlushTurn();
                    items.Add(new OutputItem(OutputItemKind.Error, new[] { entry }));
                    break;

                // A restored session's question: its own row, never folded into the work
                // that answered it. It closes whatever came before for the same reason a
                // message does — the reader's words are not activity.
                case LogKind.UserPrompt:
                    FlushMessage();
                    FlushTurn();
                    items.Add(new OutputItem(OutputItemKind.Prompt, new[] { entry }));
                    break;

                default:
                    FlushMessage();
                    FlushTurn();
                    items.Add(new OutputItem(OutputItemKind.Plain, new[] { entry }));
                    break;
            }
        }

        FlushMessage();

        // Whatever is still open when the feed ends is the run's live turn.
        FlushTurn(open: turn.Count > 0);
        return items;
    }
}