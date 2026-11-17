using System.Text.Json;
using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class DevLoopTests
{
    [Fact]
    public void TheLoopOnlyExistsInDevelopment()
    {
        Assert.True(DevLoop.IsEnabled(isDevelopment: true));
        Assert.False(DevLoop.IsEnabled(isDevelopment: false));
    }

    [Fact]
    public void TheStampNamesTheRunningProcess()
    {
        var stamp = DevLoop.Stamp(Root());

        Assert.Equal(Environment.ProcessId, stamp.Pid);
        Assert.NotEqual(default, stamp.StartedAt);
        Assert.NotNull(stamp.Build);
    }

    [Fact]
    public void TheNewestWatchedWriteWins()
    {
        var root = Root();
        try
        {
            var older = Write(root, "Components/Older.razor", Utc(2026, 1, 1, 9, 0, 0));
            var newer = Write(root, "wwwroot/js/newer.js", Utc(2026, 1, 2, 9, 0, 0));

            Assert.True(newer > older);
            Assert.Equal(Utc(2026, 1, 2, 9, 0, 0), DevLoop.NewestSourceWrite(root));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void FilesOutsideTheWatchedSetDoNotCount()
    {
        var root = Root();
        try
        {
            Write(root, "Components/Page.razor", Utc(2026, 1, 1, 9, 0, 0));
            Write(root, "Components/notes.txt", Utc(2026, 2, 1, 9, 0, 0));

            Assert.Equal(Utc(2026, 1, 1, 9, 0, 0), DevLoop.NewestSourceWrite(root));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("lib")]
    [InlineData("node_modules")]
    public void GeneratedAndVendoredDirectoriesAreSkipped(string directory)
    {
        var root = Root();
        try
        {
            Write(root, "Components/Page.razor", Utc(2026, 1, 1, 9, 0, 0));
            Write(root, $"wwwroot/{directory}/noise.js", Utc(2026, 3, 1, 9, 0, 0));

            Assert.Equal(Utc(2026, 1, 1, 9, 0, 0), DevLoop.NewestSourceWrite(root));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AMissingTreeHasNoSourceStamp()
    {
        var root = Path.Combine(Path.GetTempPath(), "climb-devloop-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Null(DevLoop.NewestSourceWrite(root));
    }

    [Fact]
    public void ThePayloadCarriesNoPath()
    {
        var root = Root();
        try
        {
            Write(root, "Components/Page.razor", Utc(2026, 1, 1, 9, 0, 0));

            // Mirror the endpoint's wire format: ASP.NET Core's default JSON is camelCase.
            var json = JsonSerializer.Serialize(
                DevLoop.Stamp(root),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.Contains("\"pid\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain(root, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ClimberWeb\\\\", json, StringComparison.Ordinal);
        }
        finally
        {
            Delete(root);
        }
    }

    private static string Root() =>
        Path.Combine(Path.GetTempPath(), "climb-devloop-" + Guid.NewGuid().ToString("N"));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc), TimeSpan.Zero);

    private static DateTime Write(string root, string relativePath, DateTimeOffset written)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// test");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return written.UtcDateTime;
    }

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: a temp tree left behind is harmless.
        }
    }
}