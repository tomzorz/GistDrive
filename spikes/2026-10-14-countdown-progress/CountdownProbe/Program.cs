using GistDrive;

// Spike probe: shows a 10 s countdown through GistDrive's own WindowsNotifier and updates it
// every second, the way the engine's tick does. sample-popup.ps1 reads the popup meanwhile.
const string AppId = "GistDrive.Spike";
var window = TimeSpan.FromSeconds(10);
var notifier = new WindowsNotifier(AppId, "GistDrive spike");
try
{
    var tag = Guid.NewGuid().ToString("N");
    notifier.ShowCountdown(tag, "Publishing notes.md", "Countdown progress spike", window, window);
    var start = DateTime.UtcNow;
    while (DateTime.UtcNow - start < window)
    {
        await Task.Delay(1000);
        var remaining = window - (DateTime.UtcNow - start);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var accepted = notifier.UpdateCountdown(tag, remaining, window);

        // What Windows keeps for the notification after the update, as Action Center would read it.
        var stored = Windows.UI.Notifications.ToastNotificationManager.History.GetHistory(AppId).FirstOrDefault(t => t.Tag == tag)?.Data;
        var storedText = stored is null ? "no data" : $"sequence {stored.SequenceNumber}, value {stored.Values["progressValue"]}, text {stored.Values["progressText"]}";
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} update to {Math.Ceiling(remaining.TotalSeconds)} s: {accepted}; stored: {storedText}");
    }

    notifier.Remove(tag);
}
finally
{
    WindowsNotifier.Unregister(AppId);
}
