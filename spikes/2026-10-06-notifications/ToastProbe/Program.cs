using System.Globalization;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

// Spike probe: a notification with buttons and a live countdown from an unpackaged .NET 10 process,
// with the AUMID registered in HKCU only (no Start menu shortcut, no Windows App SDK).
// The first argument is how many seconds to wait for a click on Cancel or Publish now.
const string Aumid = "GistDrive.Spike";
const string KeyPath = @"Software\Classes\AppUserModelId\" + Aumid;
var waitSeconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10;

var keyExisted = Registry.CurrentUser.OpenSubKey(KeyPath) is not null;
using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue("DisplayName", "GistDrive Spike");
try
{
    var xml = new XmlDocument();
    xml.LoadXml("""
        <toast scenario="reminder">
          <visual>
            <binding template="ToastGeneric">
              <text>Publishing notes.md</text>
              <text>GistDrive spike: a public upload waiting in the undo window</text>
              <progress value="{progressValue}" valueStringOverride="{progressValueString}" status="{progressStatus}"/>
            </binding>
          </visual>
          <actions>
            <action content="Cancel" arguments="action=cancel&amp;id=42" activationType="foreground"/>
            <action content="Publish now" arguments="action=now&amp;id=42" activationType="foreground"/>
          </actions>
        </toast>
        """);
    var toast = new ToastNotification(xml) { Tag = "spike", Group = "gistdrive" };
    toast.Data = new NotificationData(new Dictionary<string, string>
    {
        ["progressValue"] = "1",
        ["progressValueString"] = "30 s",
        ["progressStatus"] = "Waiting to publish",
    }, 1);

    var clicked = new TaskCompletionSource<string>();
    toast.Activated += (_, e) => clicked.TrySetResult(e is ToastActivatedEventArgs a ? a.Arguments : "(no arguments)");
    toast.Dismissed += (_, e) => Console.WriteLine($"Dismissed, reason: {e.Reason}");
    toast.Failed += (_, e) => Console.WriteLine($"Failed, error: 0x{e.ErrorCode.HResult:X8}");

    var notifier = ToastNotificationManager.CreateToastNotifier(Aumid);
    try { Console.WriteLine($"notifier setting: {notifier.Setting}"); }
    catch (Exception ex) { Console.WriteLine($"notifier setting unreadable: {ex.Message}"); }
    notifier.Show(toast);
    await Task.Delay(800);

    var history = ToastNotificationManager.History;
    var shown = history.GetHistory(Aumid);
    Console.WriteLine($"history after Show: {shown.Count} notification(s), tags: {string.Join(",", shown.Select(t => t.Tag))}");

    for (uint s = 2; s <= 4; s++)
    {
        var remaining = 30 - (int)(s - 1) * 10;
        var data = new NotificationData(new Dictionary<string, string>
        {
            ["progressValue"] = (remaining / 30.0).ToString(CultureInfo.InvariantCulture),
            ["progressValueString"] = $"{remaining} s",
        }, s);
        Console.WriteLine($"update to {remaining} s: {notifier.Update(data, "spike", "gistdrive")}");
        await Task.Delay(500);
    }

    Console.WriteLine($"waiting up to {waitSeconds} s for a click");
    var finished = await Task.WhenAny(clicked.Task, Task.Delay(TimeSpan.FromSeconds(waitSeconds)));
    Console.WriteLine(finished == clicked.Task ? $"Activated with arguments: {clicked.Task.Result}" : "no click arrived");

    history.Remove("spike", "gistdrive", Aumid);
    Console.WriteLine($"history after Remove: {history.GetHistory(Aumid).Count} notification(s)");
}
finally
{
    if (!keyExisted) Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
    Console.WriteLine($"AUMID registration removed: {Registry.CurrentUser.OpenSubKey(KeyPath) is null}");
}
