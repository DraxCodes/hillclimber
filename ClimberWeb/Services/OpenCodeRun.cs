namespace ClimberWeb.Services;

/// <summary>
/// Builds the one opencode invocation both methods make: a single non-interactive
/// <c>opencode run</c> that streams structured JSON events. Kept in one place so the
/// general run and the hillclimb loop never drift on the arguments.
/// </summary>
public static class OpenCodeRun
{
    /// <summary>The command line as shown in the output panel.</summary>
    public static string CommandLine(string prompt) => $"opencode run \"{prompt}\" --format json";

    /// <summary>The request <see cref="CommandLine"/> describes, run in <paramref name="folder"/>.</summary>
    public static CommandRequest Request(string prompt, string folder) =>
        new("opencode", new[] { "run", prompt, "--format", "json" }, folder);
}
