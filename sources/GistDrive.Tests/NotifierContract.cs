namespace GistDrive.Tests;

/// <summary>
/// What every INotifier must do, run against the fake on every test run and against Windows
/// when GISTDRIVE_REAL_NOTIFIER=1. Windows answers these even while the PC is locked.
/// </summary>
public abstract class NotifierContract
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    protected abstract INotifier Notifier { get; }

    protected abstract bool IsShowing(string tag);

    /// <summary>
    /// The remaining time the countdown shows now, as text such as "10 s".
    /// </summary>
    protected abstract string? ShownCountdown(string tag);

    [Fact]
    public void A_countdown_shows_until_removed()
    {
        var tag = Guid.NewGuid().ToString("N");
        Notifier.ShowCountdown(tag, "GistDrive test", "Notifier contract", Window, Window);
        Assert.True(IsShowing(tag));
        Notifier.Remove(tag);
        Assert.False(IsShowing(tag));
    }

    [Fact]
    public async Task A_shown_countdown_shows_each_update()
    {
        // Windows accepts an update it then drops, so the test reads back what is shown.
        var tag = Guid.NewGuid().ToString("N");
        Notifier.ShowCountdown(tag, "GistDrive test", "Notifier contract", Window, Window);
        try
        {
            Assert.Equal("30 s", ShownCountdown(tag));
            Assert.True(Notifier.UpdateCountdown(tag, TimeSpan.FromSeconds(20), Window));
            Assert.True(Notifier.UpdateCountdown(tag, TimeSpan.FromSeconds(10), Window));
            await Eventually.True(() => ShownCountdown(tag) == "10 s", "the countdown shows the latest update", 3_000);
        }
        finally
        {
            Notifier.Remove(tag);
        }
    }

    [Fact]
    public void An_update_after_removal_reports_the_countdown_gone()
    {
        var tag = Guid.NewGuid().ToString("N");
        Notifier.ShowCountdown(tag, "GistDrive test", "Notifier contract", Window, Window);
        Notifier.Remove(tag);
        Assert.False(Notifier.UpdateCountdown(tag, TimeSpan.FromSeconds(10), Window));
    }
}

public sealed class FakeNotifierContractTests : NotifierContract
{
    private readonly FakeNotifier _notifier = new();

    protected override INotifier Notifier => _notifier;

    protected override bool IsShowing(string tag) => _notifier.IsShowing(tag);

    protected override string? ShownCountdown(string tag) =>
        _notifier.Countdowns.Where(c => c.Tag == tag).Select(c => $"{Math.Ceiling(c.Remaining.TotalSeconds)} s").FirstOrDefault();
}

public sealed class WindowsNotifierContractTests : NotifierContract, IDisposable
{
    private const string AppId = "GistDrive.Tests";
    private readonly WindowsNotifier? _notifier;

    public WindowsNotifierContractTests()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("GISTDRIVE_REAL_NOTIFIER") == "1", "Set GISTDRIVE_REAL_NOTIFIER=1 to run against Windows notifications.");
        _notifier = new WindowsNotifier(AppId, "GistDrive tests");
    }

    protected override INotifier Notifier => _notifier!;

    protected override bool IsShowing(string tag) => _notifier!.IsShowing(tag);

    protected override string? ShownCountdown(string tag) => _notifier!.ShownCountdown(tag);

    public void Dispose() => WindowsNotifier.Unregister(AppId);
}
