namespace ClimberWeb.Services;

/// <summary>A single navigable directory (or drive/root).</summary>
/// <param name="Name">Display label (folder name, or decorated drive name).</param>
/// <param name="FullPath">Absolute path to navigate to.</param>
public sealed record DirectoryEntry(string Name, string FullPath);

/// <summary>
/// A snapshot of one folder level, ready to render.
/// </summary>
/// <param name="CurrentPath">The folder being shown; <c>null</c> for the "roots" (drive list) view.</param>
/// <param name="ParentPath">Parent to navigate up to; <c>null</c> when already at the top.</param>
/// <param name="Trail">Breadcrumb segments for <paramref name="CurrentPath"/>, outermost first.</param>
/// <param name="Directories">Sub-directories of <paramref name="CurrentPath"/>, sorted by name.</param>
/// <param name="Roots">Available drives / filesystem roots.</param>
/// <param name="Error">Populated when the requested folder could not be listed.</param>
public sealed record DirectoryListing(
    string? CurrentPath,
    string? ParentPath,
    IReadOnlyList<DirectoryEntry> Trail,
    IReadOnlyList<DirectoryEntry> Directories,
    IReadOnlyList<DirectoryEntry> Roots,
    string? Error);

/// <summary>Lists directories on the machine hosting the dashboard.</summary>
public interface IFileBrowser
{
    /// <summary>
    /// Lists <paramref name="path"/>. A null/empty path yields the roots (drive) view.
    /// Never throws for bad input; problems are surfaced through <see cref="DirectoryListing.Error"/>.
    /// </summary>
    DirectoryListing List(string? path);
}

/// <summary>Default <see cref="IFileBrowser"/> backed by <see cref="Directory"/> and <see cref="DriveInfo"/>.</summary>
public sealed class FileBrowser : IFileBrowser
{
    public DirectoryListing List(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootsView(null);
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return RootsView($"Invalid path: {ex.Message}");
        }

        // Drop any trailing separator (except on a filesystem root such as "C:\" or "/").
        var root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, StringComparison.Ordinal))
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (!Directory.Exists(full))
        {
            return RootsView($"{full} is not a folder.");
        }

        var directories = new List<DirectoryEntry>();
        string? error = null;

        try
        {
            foreach (var child in Directory.EnumerateDirectories(full))
            {
                try
                {
                    var attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.Hidden) != 0 || (attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                directories.Add(new DirectoryEntry(name, child));
            }
        }
        catch (UnauthorizedAccessException)
        {
            error = "Access denied.";
        }
        catch (DirectoryNotFoundException)
        {
            return RootsView($"{full} no longer exists.");
        }

        directories.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return new DirectoryListing(
            CurrentPath: full,
            ParentPath: Directory.GetParent(full)?.FullName,
            Trail: BuildTrail(full),
            Directories: directories,
            Roots: Roots(),
            Error: error);
    }

    private static DirectoryListing RootsView(string? error) =>
        new(
            CurrentPath: null,
            ParentPath: null,
            Trail: Array.Empty<DirectoryEntry>(),
            Directories: Array.Empty<DirectoryEntry>(),
            Roots: Roots(),
            Error: error);

    private static IReadOnlyList<DirectoryEntry> Roots()
    {
        var roots = new List<DirectoryEntry>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var label = drive.Name;

                try
                {
                    if (drive.IsReady && drive.VolumeLabel is { Length: > 0 } volume)
                    {
                        label = $"{drive.Name.TrimEnd('\\')} · {volume}";
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fall back to the bare drive name.
                }

                roots.Add(new DirectoryEntry(label, drive.RootDirectory.FullName));
            }
        }
        else
        {
            roots.Add(new DirectoryEntry("/", "/"));
        }

        return roots;
    }

    private static IReadOnlyList<DirectoryEntry> BuildTrail(string full)
    {
        var stack = new Stack<DirectoryInfo>();

        for (var dir = new DirectoryInfo(full); dir is not null; dir = dir.Parent)
        {
            stack.Push(dir);
        }

        var trail = new List<DirectoryEntry>();
        foreach (var dir in stack)
        {
            // Roots show their full path ("C:\", "/"); ordinary folders show just the name.
            var label = dir.Parent is null ? dir.FullName : dir.Name;
            trail.Add(new DirectoryEntry(label, dir.FullName));
        }

        return trail;
    }
}
