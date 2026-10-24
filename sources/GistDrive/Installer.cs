using Microsoft.Win32;

namespace GistDrive;

/// <summary>
/// Where the integration goes. ForCurrentUser is the real thing; the tests point the same code
/// at a sandbox key and temporary folders.
/// </summary>
public sealed record InstallTarget(string ClassesKey, string ExplorerKey, Guid SidebarClsid, string SendToFolder, string StartupFolder, string AppId)
{
    public static InstallTarget ForCurrentUser() => new(
        @"Software\Classes",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer",
        new Guid("943CAC70-26CD-4B04-9507-11EF13C2D983"),
        Environment.GetFolderPath(Environment.SpecialFolder.SendTo),
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Program.AppId);
}

/// <summary>
/// Adds and removes the integration (REQ-INST-1): the sidebar entry (REQ-SHELL-2 to REQ-SHELL-4),
/// the menu entries (REQ-SHELL-9 to REQ-SHELL-11), the Send To entries (REQ-SEND-1), the
/// Startup shortcut and the notification app ID. Everything lives under HKCU or in the user's
/// own shell folders, so no admin rights are needed.
/// </summary>
public static class Installer
{
    public const string DisplayName = "GistDrive";
    public const string SecretSendTo = "Secret gist.lnk";
    public const string PublicSendTo = "Public gist.lnk";
    public const string StartupShortcut = "GistDrive.lnk";

    // shell32's file system folder: Explorer treats the target folder like any other folder.
    private const string FileSystemFolder = "{0E5AAE11-A475-4c5b-AB00-C66DE400274E}";

    // shell32's plain folder, for a copy of GistDrive that lost its icons folder (REQ-SHELL-19).
    private const string FallbackIcon = @"%SystemRoot%\system32\shell32.dll,4";

    private static readonly (string Parent, string Name)[] MenuKeys =
    [
        ("AllFilesystemObjects", "GistDrive.Edit"),
        ("AllFilesystemObjects", "GistDrive.DeleteGist"),
        ("AllFilesystemObjects", "GistDrive.MakePublic"),
        ("Directory", "GistDrive.DeleteGist"),
        ("Directory", "GistDrive.MakePublic"),
    ];

    public static void Install(InstallTarget target, Folders folders, string exePath)
    {
        folders.EnsureTopFolders();
        WriteSidebar(target, folders, exePath);
        WriteMenus(target, folders, exePath);
        FolderIcons.Apply(folders, FolderIcons.Beside(exePath));
        Directory.CreateDirectory(target.SendToFolder);
        Directory.CreateDirectory(target.StartupFolder);
        ShellLink.Create(Path.Combine(target.SendToFolder, SecretSendTo), exePath, "send-secret", "Upload a copy as a secret gist");
        ShellLink.Create(Path.Combine(target.SendToFolder, PublicSendTo), exePath, "send-public", "Upload a copy as a public gist");
        ShellLink.Create(Path.Combine(target.StartupFolder, StartupShortcut), exePath, string.Empty, "Start GistDrive");
        var appId = $@"{target.ClassesKey}\AppUserModelId\{target.AppId}";
        Set(appId, "DisplayName", DisplayName);
        if (NotificationImage(exePath) is { } image) Set(appId, "IconUri", image);
        Native.NotifyAssociationsChanged();
    }

    /// <summary>
    /// True when GistDrive is installed for another root folder, another executable or other
    /// icons, for example after a change to RootFolder in config.json (REQ-SHELL-12), a move of
    /// the exe, or an update that brought new icons.
    /// </summary>
    public static bool IsStale(InstallTarget target, Folders folders, string exePath)
    {
        var key = $@"{target.ClassesKey}\CLSID\{Clsid(target)}";
        using var bag = Registry.CurrentUser.OpenSubKey($@"{key}\Instance\InitPropertyBag");
        if (bag is null) return false;

        using var icon = Registry.CurrentUser.OpenSubKey($@"{key}\DefaultIcon");
        using var command = Registry.CurrentUser.OpenSubKey($@"{target.ClassesKey}\AllFilesystemObjects\shell\GistDrive.Edit\command");
        var registeredRoot = bag.GetValue("TargetFolderPath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var registeredIcon = icon?.GetValue(string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        var registeredCommand = command?.GetValue(string.Empty) as string;
        return !string.Equals(registeredRoot, folders.Root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(registeredIcon, SidebarIcon(exePath), StringComparison.OrdinalIgnoreCase)
            || registeredCommand?.StartsWith($"\"{exePath}\"", StringComparison.OrdinalIgnoreCase) != true;
    }

    /// <summary>
    /// Removes everything Install added. The root folder and the files in it stay.
    /// </summary>
    public static void Uninstall(InstallTarget target, Folders folders)
    {
        FolderIcons.Remove(folders);
        var clsid = Clsid(target);
        var user = Registry.CurrentUser;
        user.DeleteSubKeyTree($@"{target.ClassesKey}\CLSID\{clsid}", throwOnMissingSubKey: false);
        user.DeleteSubKeyTree($@"{target.ExplorerKey}\Desktop\NameSpace\{clsid}", throwOnMissingSubKey: false);
        using (var hidden = user.OpenSubKey($@"{target.ExplorerKey}\HideDesktopIcons\NewStartPanel", writable: true)) hidden?.DeleteValue(clsid, throwOnMissingValue: false);
        foreach (var (parent, name) in MenuKeys) user.DeleteSubKeyTree($@"{target.ClassesKey}\{parent}\shell\{name}", throwOnMissingSubKey: false);
        user.DeleteSubKeyTree($@"{target.ClassesKey}\AppUserModelId\{target.AppId}", throwOnMissingSubKey: false);
        foreach (var link in new[] { Path.Combine(target.SendToFolder, SecretSendTo), Path.Combine(target.SendToFolder, PublicSendTo), Path.Combine(target.StartupFolder, StartupShortcut) })
        {
            File.Delete(link);
        }

        Native.NotifyAssociationsChanged();
    }

    // The steps of Microsoft's "Integrate a Cloud Storage Provider", HKCU only (spikes/2026-10-06-sidebar-hkcu).
    private static void WriteSidebar(InstallTarget target, Folders folders, string exePath)
    {
        var clsid = Clsid(target);
        var key = $@"{target.ClassesKey}\CLSID\{clsid}";
        Set(key, string.Empty, DisplayName);
        Set($@"{key}\DefaultIcon", string.Empty, SidebarIcon(exePath), RegistryValueKind.ExpandString);
        Set(key, "System.IsPinnedToNameSpaceTree", 1, RegistryValueKind.DWord);
        Set(key, "SortOrderIndex", 0x42, RegistryValueKind.DWord);
        Set($@"{key}\InProcServer32", string.Empty, @"%systemroot%\system32\shell32.dll", RegistryValueKind.ExpandString);
        Set($@"{key}\Instance", "CLSID", FileSystemFolder);
        Set($@"{key}\Instance\InitPropertyBag", "Attributes", 0x11, RegistryValueKind.DWord);
        Set($@"{key}\Instance\InitPropertyBag", "TargetFolderPath", folders.Root, RegistryValueKind.ExpandString);
        Set($@"{key}\ShellFolder", "FolderValueFlags", 0x28, RegistryValueKind.DWord);
        Set($@"{key}\ShellFolder", "Attributes", unchecked((int)0xF080004D), RegistryValueKind.DWord);
        Set($@"{target.ExplorerKey}\Desktop\NameSpace\{clsid}", string.Empty, DisplayName);
        Set($@"{target.ExplorerKey}\HideDesktopIcons\NewStartPanel", clsid, 1, RegistryValueKind.DWord);
    }

    // File entries under AllFilesystemObjects reach .url files whatever ProgID owns them; folder
    // entries go under Directory (spikes/2026-10-06-menu-appliesto).
    private static void WriteMenus(InstallTarget target, Folders folders, string exePath)
    {
        var gistShortcut = $"System.ItemPathDisplay:~<\"{folders.Root}\\\" AND System.FileExtension:=\".url\"";
        var secretGistShortcut = $"System.ItemFolderPathDisplay:=\"{folders.Secret}\" AND System.FileExtension:=\".url\"";
        var notFailed = $"NOT System.ItemNameDisplay:=\"{Folders.FailedName}\"";
        var gistFolder = $"(System.ItemFolderPathDisplay:=\"{folders.Secret}\" OR System.ItemFolderPathDisplay:=\"{folders.Public}\") AND {notFailed}";
        var secretGistFolder = $"System.ItemFolderPathDisplay:=\"{folders.Secret}\" AND {notFailed}";

        WriteMenu(target, "AllFilesystemObjects", "GistDrive.Edit", "Edit", gistShortcut, exePath, "edit");
        WriteMenu(target, "AllFilesystemObjects", "GistDrive.DeleteGist", "Delete gist", gistShortcut, exePath, "delete");
        WriteMenu(target, "AllFilesystemObjects", "GistDrive.MakePublic", "Make public", secretGistShortcut, exePath, "make-public");
        WriteMenu(target, "Directory", "GistDrive.DeleteGist", "Delete gist", gistFolder, exePath, "delete");
        WriteMenu(target, "Directory", "GistDrive.MakePublic", "Make public", secretGistFolder, exePath, "make-public");
    }

    private static void WriteMenu(InstallTarget target, string parent, string name, string label, string appliesTo, string exePath, string verb)
    {
        var key = $@"{target.ClassesKey}\{parent}\shell\{name}";
        Set(key, "MUIVerb", label);
        Set(key, "AppliesTo", appliesTo);
        Set($@"{key}\command", string.Empty, $"\"{exePath}\" {verb} \"%1\"");
    }

    private static string Clsid(InstallTarget target) => target.SidebarClsid.ToString("B").ToUpperInvariant();

    /// <summary>
    /// The sidebar entry's icon (REQ-SHELL-14): GistDrive's own next to the exe.
    /// </summary>
    private static string SidebarIcon(string exePath)
    {
        var icon = Path.Combine(FolderIcons.Beside(exePath), FolderIcons.RootIcon);
        return File.Exists(icon) ? $"{icon},0" : FallbackIcon;
    }

    /// <summary>
    /// The image that notifications show for the app ID, when the exe has its icons beside it
    /// (REQ-SHELL-15).
    /// </summary>
    public static string? NotificationImage(string exePath)
    {
        var image = Path.Combine(FolderIcons.Beside(exePath), FolderIcons.NotificationImage);
        return File.Exists(image) ? image : null;
    }

    private static void Set(string keyPath, string name, object value, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(name, value, kind);
    }
}
