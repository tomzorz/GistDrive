namespace GistDrive.Tests;

/// <summary>
/// A folder under %TEMP% that is deleted with everything in it at the end of a test.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GistDrive.Tests", Guid.NewGuid().ToString("N")[..8]);

    public TempFolder() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            // A folder with a custom icon carries the read-only attribute, and that stops a delete.
            foreach (var folder in new DirectoryInfo(Path).EnumerateDirectories("*", SearchOption.AllDirectories)) folder.Attributes &= ~FileAttributes.ReadOnly;
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file still open somewhere: %TEMP% gets cleaned eventually.
        }
    }
}

/// <summary>
/// Polls a condition until it holds, so tests wait for the engine without fixed sleeps.
/// </summary>
public static class Eventually
{
    public static async Task True(Func<bool> condition, string what, int timeoutMilliseconds = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < deadline)
        {
            if (Check(condition)) return;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out after {timeoutMilliseconds} ms waiting until {what}.");
    }

    /// <summary>
    /// Asserts that a condition holds for the whole period, for "nothing happens" checks.
    /// </summary>
    public static async Task Stays(Func<bool> condition, string what, int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Assert.True(Check(condition), $"Expected {what} to hold, and it stopped holding.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    // A file being written or moved by the engine can throw while a condition reads it.
    private static bool Check(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (IOException)
        {
            return false;
        }
    }
}
