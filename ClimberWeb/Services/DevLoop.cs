using System.Diagnostics;

namespace ClimberWeb.Services;

/// <summary>
/// The live loop's build identity: what is running, and how new the sources are. Development
/// only — this is a working aid, never a product surface, and nothing here is mapped outside
/// Development.
/// </summary>
/// <remarks>
/// <para>
/// Hot reload is an optimisation, not a guarantee: it can report success while the served page is
/// stale, and hot-patching a layout that owns a <c>SectionOutlet</c> can throw and leave rendering
/// broken. The loop therefore asks the host what it is, instead of assuming.
/// </para>
/// <para>
/// <see cref="DevStamp.Build"/> changes when the running assembly is replaced (a restart);
/// <see cref="DevStamp.Source"/> changes on any save, including one that was hot-patched without
/// rewriting the assembly.
/// </para>
/// </remarks>
public static class DevLoop
{
    /// <summary>Where the loop asks whether the running host is the current build.</summary>
    public const string StampPath = "/__dev/stamp";

    private static readonly string[] WatchedRoots = ["Components", "Services", "wwwroot"];

    private static readonly string[] WatchedExtensions = [".razor", ".cs", ".css", ".js"];

    private static readonly string[] SkippedDirectories = ["bin", "obj", "lib", "node_modules"];

    /// <summary>The single gate: the loop exists in Development and nowhere else.</summary>
    public static bool IsEnabled(bool isDevelopment) => isDevelopment;

    /// <summary>A snapshot of the running host and the sources under <paramref name="contentRoot"/>.</summary>
    public static DevStamp Stamp(string contentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);

        return new DevStamp(
            Build: AssemblyWrite(),
            Source: NewestSourceWrite(contentRoot),
            StartedAt: ProcessStart(),
            Pid: Environment.ProcessId);
    }

    /// <summary>
    /// The newest watched file write under <paramref name="contentRoot"/>, or null when the
    /// watched roots are absent. Unreadable directories and files are skipped: a save in progress
    /// must never fail the check.
    /// </summary>
    public static DateTimeOffset? NewestSourceWrite(string contentRoot)
    {
        if (!Directory.Exists(contentRoot))
        {
            return null;
        }

        DateTimeOffset? newest = null;
        foreach (var root in WatchedRoots)
        {
            var directory = Path.Combine(contentRoot, root);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in EnumerateWatched(directory))
            {
                try
                {
                    var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                    if (newest is null || written > newest)
                    {
                        newest = written;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // A file that vanished mid-scan is not evidence of a change.
                }
            }
        }

        return newest;
    }

    private static DateTimeOffset? AssemblyWrite()
    {
        var location = typeof(DevLoop).Assembly.Location;
        if (string.IsNullOrEmpty(location) || !File.Exists(location))
        {
            // A single-file publish has no on-disk assembly; the loop is a development aid, so a
            // missing stamp is honest rather than fatal.
            return null;
        }

        return new DateTimeOffset(File.GetLastWriteTimeUtc(location), TimeSpan.Zero);
    }

    private static DateTimeOffset ProcessStart()
    {
        try
        {
            return new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            return DateTimeOffset.UtcNow;
        }
    }

    private static IEnumerable<string> EnumerateWatched(string directory)
    {
        string[] files;
        string[] children;

        try
        {
            files = Directory.GetFiles(directory);
            children = Directory.GetDirectories(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            if (WatchedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var child in children)
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var nested in EnumerateWatched(child))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>
/// One reading of the running host: its assembly write time, the newest source write, when the
/// process started and its id. No path, build number or secret is exposed.
/// </summary>
public sealed record DevStamp(
    DateTimeOffset? Build,
    DateTimeOffset? Source,
    DateTimeOffset StartedAt,
    int Pid);