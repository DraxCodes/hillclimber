using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class FileBrowserTests : IDisposable
{
    private readonly FileBrowser _browser = new();
    private readonly string _root;

    public FileBrowserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "climber-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void List_NullPath_ReturnsRootsView()
    {
        var listing = _browser.List(null);

        Assert.Null(listing.CurrentPath);
        Assert.Null(listing.ParentPath);
        Assert.NotEmpty(listing.Roots);
        Assert.Empty(listing.Directories);
        Assert.Null(listing.Error);
    }

    [Fact]
    public void List_ListsOnlySubdirectories_SortedByName()
    {
        Directory.CreateDirectory(Path.Combine(_root, "beta"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_root, "gamma"));
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "x");

        var listing = _browser.List(_root);

        Assert.Equal(
            new[] { "Alpha", "beta", "gamma" },
            listing.Directories.Select(d => d.Name).ToArray());
    }

    [Fact]
    public void List_ExcludesHiddenDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "visible"));
        var hidden = Path.Combine(_root, ".hidden");
        Directory.CreateDirectory(hidden);

        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, FileAttributes.Hidden);
        }

        var listing = _browser.List(_root);

        Assert.Contains(listing.Directories, d => d.Name == "visible");
        Assert.DoesNotContain(listing.Directories, d => d.Name == ".hidden");
    }

    [Fact]
    public void List_ComputesParentAndBreadcrumbTrail()
    {
        var nested = Path.Combine(_root, "one", "two");
        Directory.CreateDirectory(nested);

        var listing = _browser.List(nested);

        Assert.Equal(nested, listing.CurrentPath);
        Assert.Equal(Path.Combine(_root, "one"), listing.ParentPath);
        Assert.Equal(nested, listing.Trail[^1].FullPath);
        Assert.Equal("two", listing.Trail[^1].Name);
        Assert.Equal(
            new[] { "one", "two" },
            listing.Trail.TakeLast(2).Select(t => t.Name).ToArray());
    }

    [Fact]
    public void List_NonExistentFolder_FallsBackToRootsWithError()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        var listing = _browser.List(missing);

        Assert.Null(listing.CurrentPath);
        Assert.NotNull(listing.Error);
        Assert.NotEmpty(listing.Roots);
    }

    [Fact]
    public void List_InvalidPath_ReturnsError()
    {
        var listing = _browser.List("\0invalid");

        Assert.Null(listing.CurrentPath);
        Assert.NotNull(listing.Error);
    }

    [Fact]
    public void List_TrailingSeparator_IsNormalized()
    {
        var nested = Path.Combine(_root, "one");
        Directory.CreateDirectory(nested);

        var listing = _browser.List(nested + Path.DirectorySeparatorChar);

        Assert.Equal(nested, listing.CurrentPath);
    }
}
