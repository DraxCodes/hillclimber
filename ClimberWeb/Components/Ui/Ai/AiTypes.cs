namespace ClimberWeb.Components.Ui.Ai;

/// <summary>One step inside a Plan or a Task.</summary>
/// <param name="Title">The step's one-line label.</param>
/// <param name="Status">done, running or pending (Plan) / error (Task).</param>
public sealed record PlanStep(string Title, string Status)
{
    /// <summary>The state a Trace line understands, or null when the step is settled.</summary>
    public string? TraceStatus => Status switch
    {
        "running" => "running",
        "pending" => "pending",
        "error" => "error",
        _ => null,
    };
}

/// <summary>A web or MCP source the run reached.</summary>
/// <param name="Url">The absolute target.</param>
/// <param name="Title">The label a reader sees; falls back to the host.</param>
public sealed record SourceLink(string Url, string? Title = null)
{
    /// <summary>The host without a leading www, or the raw URL when it will not parse.</summary>
    public string Host
    {
        get
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                return Url;
            }

            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }
    }

    /// <summary>The title, or the host when the run recorded none.</summary>
    public string Label => string.IsNullOrWhiteSpace(Title) ? Host : Title;

    /// <summary>The path a reader can use to tell two links to one host apart.</summary>
    public string? Path
    {
        get
        {
            if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var path = uri.PathAndQuery.TrimEnd('/');
            return path.Length > 1 ? path : null;
        }
    }
}