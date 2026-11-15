namespace ClimberWeb.Services;

/// <summary>The shape of a rendered transcript item.</summary>
public enum OutputItemKind
{
    /// <summary>One or more consecutive <see cref="LogKind.Text"/> entries.</summary>
    Message,

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
/// Prose belongs to the reader, not the fold: text that arrives during a turn renders just
/// after it, and text outside a turn renders on its own. Errors and plain lines end the
/// current turn and render as their own rows. Pure and order-preserving, so it is cheap to
/// test and to call on every render.
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

        // Text is the answer, so it is never folded. Text that arrives while a turn is open
        // is buffered and rendered directly after that turn; text outside a turn is its own row.
        var pending = new List<LogEntry>();
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
            else if (footer is not null)
            {
                // A step that produced only prose still has a cost worth one quiet line.
                items.Add(new OutputItem(OutputItemKind.Marker, new[] { footer }));
            }

            turn.Clear();

            if (pending.Count > 0)
            {
                items.Add(new OutputItem(OutputItemKind.Message, pending.ToArray()));
                pending.Clear();
            }
        }

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case LogKind.Text:
                    if (turn.Count > 0)
                    {
                        pending.Add(entry);
                    }
                    else
                    {
                        message.Add(entry);
                    }

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

                case LogKind.Unrecognized:
                    FlushMessage();
                    FlushTurn();
                    items.Add(new OutputItem(OutputItemKind.Unrecognized, new[] { entry }));
                    break;

                case LogKind.Error:
                    FlushMessage();
                    FlushTurn();
                    items.Add(new OutputItem(OutputItemKind.Error, new[] { entry }));
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