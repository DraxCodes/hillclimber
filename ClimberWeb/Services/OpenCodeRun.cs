namespace ClimberWeb.Services;

/// <summary>
/// Builds the one opencode invocation both methods make: a single non-interactive
/// <c>opencode run</c> that streams structured JSON events. Kept in one place so the
/// general run and the hillclimb loop never drift on the arguments.
/// </summary>
public static class OpenCodeRun
{
    /// <summary>The command line as shown in the output panel.</summary>
    public static string CommandLine(string prompt, string? agent = null, string? model = null)
    {
        var line = "opencode run \"" + prompt + "\"";
        if (!string.IsNullOrEmpty(agent))
        {
            line += $" --agent {agent}";
        }

        if (!string.IsNullOrEmpty(model))
        {
            line += $" --model {model}";
        }

        return line + " --format json";
    }

    /// <summary>
    /// The request <see cref="CommandLine"/> describes, run in <paramref name="folder"/>.
    /// <paramref name="agent"/> and <paramref name="model"/> are omitted when null, so
    /// opencode falls back to its own defaults.
    /// </summary>
    public static CommandRequest Request(string prompt, string folder, string? agent = null, string? model = null)
    {
        var arguments = new List<string> { "run", prompt };
        if (!string.IsNullOrEmpty(agent))
        {
            arguments.Add("--agent");
            arguments.Add(agent);
        }

        if (!string.IsNullOrEmpty(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        arguments.Add("--format");
        arguments.Add("json");
        return new CommandRequest("opencode", arguments, folder);
    }
}
