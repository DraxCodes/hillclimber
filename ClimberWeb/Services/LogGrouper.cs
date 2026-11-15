namespace ClimberWeb.Services;

/// <summary>The shape of a rendered transcript item.</summary>
public enum OutputItemKind
{
    /// <summary>One or more consecutive <see cref="LogKind.Text"/> entries.</summary>
    Message,

    /// <summary>One or more consecutive <see cref="LogKind.Tool"/> / <see cref="LogKind.Subagent"/> entries.</summary>
    TraceGroup,

    /// <summary>One or more consecutive <see cref="LogKind.Reasoning"/> entries.</summary>
    Reasoning,

    /// <summary>A JSON event this renderer does not recognise; raw payload behind a disclosure.</summary>
    Unrecognized,

    /// <summary>A step boundary or summary line.</summary>
    Marker,

    /// <summary>An opencode <c>error</c> event.</summary>
    Error,

    /// <summary>Anything that is not a recognised event; rendered verbatim.</summary>
    Plain,
}

/// <summary>One transcript row: a message, a group of traces, or a single marker/plain line.</summary>
/// <param name="Kind">How the row should be rendered.</param>
/// <param name="Entries">The entries in chronological order; one for a single-row kind.</param>
public sealed record OutputItem(OutputItemKind Kind, IReadOnlyList<LogEntry> Entries);

/// <summary>
/// Folds a run's display-ready log entries into the rows the transcript renders: contiguous
/// prose becomes one message, contiguous tool calls become one trace group, and everything
/// else keeps its own row. Pure and order-preserving, so it is cheap to test and to call on
/// every render.
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
        var pending = new List<LogEntry>();
        OutputItemKind? pendingKind = null;

        void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }

            items.Add(new OutputItem(pendingKind!.Value, pending.ToArray()));
            pending.Clear();
            pendingKind = null;
        }

        void Append(OutputItemKind kind, LogEntry entry)
        {
            if (pendingKind != kind)
            {
                Flush();
                pendingKind = kind;
            }

            pending.Add(entry);
        }

        foreach (var entry in entries)
        {
            switch (entry.Kind)
            {
                case LogKind.Text:
                    Append(OutputItemKind.Message, entry);
                    break;

                case LogKind.Tool:
                case LogKind.Subagent:
                    Append(OutputItemKind.TraceGroup, entry);
                    break;

                // A thought belongs with the activity it explains; consecutive thoughts
                // merge like prose. An empty thought carries nothing to show.
                case LogKind.Reasoning:
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                    {
                        Append(OutputItemKind.Reasoning, entry);
                    }

                    break;

                // A recognised event the renderer does not understand: one quiet row,
                // with its payload available on demand.
                case LogKind.Unrecognized:
                    Flush();
                    items.Add(new OutputItem(OutputItemKind.Unrecognized, new[] { entry }));
                    break;

                // A step start marks a new turn but carries nothing worth showing.
                case LogKind.StepStart:
                    Flush();
                    break;

                case LogKind.StepFinish:
                    Flush();
                    items.Add(new OutputItem(OutputItemKind.Marker, new[] { entry }));
                    break;

                case LogKind.Error:
                    Flush();
                    items.Add(new OutputItem(OutputItemKind.Error, new[] { entry }));
                    break;

                default:
                    Flush();
                    items.Add(new OutputItem(OutputItemKind.Plain, new[] { entry }));
                    break;
            }
        }

        Flush();
        return items;
    }
}