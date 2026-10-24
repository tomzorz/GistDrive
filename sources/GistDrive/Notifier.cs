using System.Globalization;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace GistDrive;

/// <summary>
/// Windows notifications as GistDrive uses them: a countdown with a Cancel button for each
/// pending action (REQ-UNDO-10), and plain messages. WindowsNotifier is the real one; the tests
/// run the same contract suite against an in-memory fake.
/// </summary>
public interface INotifier
{
    /// <summary>
    /// Raised with the tag of a countdown notification when the owner selects Cancel.
    /// </summary>
    event Action<string>? CancelSelected;

    void ShowCountdown(string tag, string title, string body, TimeSpan remaining, TimeSpan window);

    /// <summary>
    /// Moves a countdown along. Returns false when the notification is gone, for example
    /// because the owner dismissed it.
    /// </summary>
    bool UpdateCountdown(string tag, TimeSpan remaining, TimeSpan window);

    void ShowMessage(string title, string body);

    void Remove(string tag);
}

/// <summary>
/// Notifications through the Windows notification platform for an unpackaged executable. The
/// app ID is registered under HKCU only; no Start menu shortcut and no Windows App SDK
/// (spikes/2026-10-06-notifications).
/// </summary>
public sealed class WindowsNotifier : INotifier
{
    private const string Group = "gistdrive";

    private readonly string _appId;
    private readonly ToastNotifier _notifier;
    private readonly Lock _gate = new();

    // The click handlers live on these objects, so they stay referenced until removed.
    private readonly Dictionary<string, ToastNotification> _countdowns = [];

    public WindowsNotifier(string appId, string displayName, string? iconPath = null)
    {
        _appId = appId;
        Register(appId, displayName, iconPath);
        _notifier = ToastNotificationManager.CreateToastNotifier(appId);
    }

    public event Action<string>? CancelSelected;

    /// <summary>
    /// Registers the app ID with the display name, and the image when there is one, that
    /// notifications show.
    /// </summary>
    public static void Register(string appId, string displayName, string? iconPath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appId}");
        key.SetValue("DisplayName", displayName);
        if (iconPath is not null) key.SetValue("IconUri", iconPath);
    }

    public static void Unregister(string appId) =>
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{appId}", throwOnMissingSubKey: false);

    public void ShowCountdown(string tag, string title, string body, TimeSpan remaining, TimeSpan window)
    {
        var xml = new XmlDocument();
        xml.LoadXml($$"""
            <toast scenario="reminder">
              <visual>
                <binding template="ToastGeneric">
                  <text>{{SecurityElement.Escape(title)}}</text>
                  <text>{{SecurityElement.Escape(body)}}</text>
                  <progress value="{progressValue}" valueStringOverride="{progressText}" status="{progressStatus}"/>
                </binding>
              </visual>
              <actions>
                <action content="Cancel" arguments="cancel" activationType="foreground"/>
              </actions>
            </toast>
            """);
        var toast = new ToastNotification(xml) { Tag = tag, Group = Group, Data = Countdown(remaining, window) };
        toast.Activated += (sender, args) =>
        {
            if (args is ToastActivatedEventArgs { Arguments: "cancel" }) CancelSelected?.Invoke(sender.Tag);
        };
        lock (_gate) _countdowns[tag] = toast;
        _notifier.Show(toast);
    }

    public bool UpdateCountdown(string tag, TimeSpan remaining, TimeSpan window) =>
        _notifier.Update(Countdown(remaining, window), tag, Group) == NotificationUpdateResult.Succeeded;

    public void ShowMessage(string title, string body)
    {
        var xml = new XmlDocument();
        xml.LoadXml($$"""
            <toast>
              <visual>
                <binding template="ToastGeneric">
                  <text>{{SecurityElement.Escape(title)}}</text>
                  <text>{{SecurityElement.Escape(body)}}</text>
                </binding>
              </visual>
            </toast>
            """);
        _notifier.Show(new ToastNotification(xml) { Tag = Guid.NewGuid().ToString("N"), Group = Group });
    }

    public void Remove(string tag)
    {
        lock (_gate) _countdowns.Remove(tag);
        ToastNotificationManager.History.Remove(tag, Group, _appId);
    }

    /// <summary>
    /// True while a notification with the tag is in the notification center.
    /// </summary>
    public bool IsShowing(string tag) => Stored(tag) is not null;

    /// <summary>
    /// The countdown text Windows holds for a notification now, for example "10 s".
    /// </summary>
    public string? ShownCountdown(string tag) =>
        Stored(tag)?.Data is { } data && data.Values.TryGetValue("progressText", out var text) ? text : null;

    private ToastNotification? Stored(string tag) => ToastNotificationManager.History.GetHistory(_appId).FirstOrDefault(t => t.Tag == tag);

    // Sequence number 0 means "apply regardless of order". Windows swaps its own counter in when
    // a notification is shown, so counting up from 1 made every update look stale: accepted, and
    // silently dropped (spikes/2026-10-14-countdown-progress).
    private static NotificationData Countdown(TimeSpan remaining, TimeSpan window)
    {
        var fraction = window > TimeSpan.Zero ? Math.Clamp(remaining / window, 0, 1) : 0;
        var values = new Dictionary<string, string>
        {
            ["progressValue"] = fraction.ToString(CultureInfo.InvariantCulture),
            ["progressText"] = $"{Math.Ceiling(remaining.TotalSeconds)} s",
            ["progressStatus"] = "Select Cancel to stop",
        };
        return new NotificationData(values, 0);
    }
}
