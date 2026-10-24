using System.Text;

namespace GistDrive;

/// <summary>
/// Custom icons for the root folder and the top folders (REQ-SHELL-13). File Explorer reads a
/// folder's icon from the desktop.ini inside it, as long as the folder has the read-only
/// attribute, which on a folder only means "customized". The icons ship next to the exe.
/// GistDrive owns one line of that file, IconResource, and only while it names a GistDrive
/// icon: an icon the owner picked in the folder's Properties stays, and so does anything else
/// Windows keeps in the file (REQ-SHELL-16, REQ-SHELL-17).
/// </summary>
public static class FolderIcons
{
    public const string IconFolder = "icons";
    public const string RootIcon = "gistdrive.ico";
    public const string SecretIcon = "secret.ico";
    public const string PublicIcon = "public.ico";
    public const string NotificationImage = "gistdrive.png";
    public const string DesktopIni = "desktop.ini";
    private const string Section = "[.ShellClassInfo]";
    private const string Key = "IconResource";

    /// <summary>
    /// The icon folder next to an executable.
    /// </summary>
    public static string Beside(string exePath) => Path.Combine(Path.GetDirectoryName(exePath)!, IconFolder);

    /// <summary>
    /// Points the three folders at their icons. Skips an icon file that is missing, and writes
    /// nothing when the folders already show them.
    /// </summary>
    public static void Apply(Folders folders, string iconFolder)
    {
        foreach (var (folder, icon) in Pairs(folders)) Set(folder, iconFolder, icon);
    }

    /// <summary>
    /// Takes the icons off again.
    /// </summary>
    public static void Remove(Folders folders)
    {
        foreach (var (folder, icon) in Pairs(folders)) Clear(folder, icon);
    }

    /// <summary>
    /// The IconResource value in the [.ShellClassInfo] section, or null when there is none.
    /// </summary>
    internal static string? IconResource(IReadOnlyList<string> lines)
    {
        var inSection = false;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.StartsWith('[')) inSection = text.Equals(Section, StringComparison.OrdinalIgnoreCase);
            else if (inSection && IsKey(text)) return text[(text.IndexOf('=') + 1)..].Trim();
        }

        return null;
    }

    /// <summary>
    /// The lines with IconResource set to the value, or taken out when the value is null.
    /// Every other line stays where it was.
    /// </summary>
    internal static List<string> WithIconResource(IReadOnlyList<string> lines, string? value)
    {
        var result = new List<string>();
        var insertAt = -1;
        var inSection = false;
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.StartsWith('['))
            {
                inSection = text.Equals(Section, StringComparison.OrdinalIgnoreCase);
                result.Add(line);
                if (inSection && insertAt < 0) insertAt = result.Count;
            }
            else if (!(inSection && IsKey(text)))
            {
                result.Add(line);
            }
        }

        if (value is null) return result;
        if (insertAt < 0)
        {
            result.Insert(0, Section);
            insertAt = 1;
        }

        result.Insert(insertAt, $"{Key}={value}");
        return result;
    }

    private static (string Folder, string Icon)[] Pairs(Folders folders) =>
        [(folders.Root, RootIcon), (folders.Secret, SecretIcon), (folders.Public, PublicIcon)];

    private static void Set(string folder, string iconFolder, string icon)
    {
        var file = Path.Combine(iconFolder, icon);
        if (!File.Exists(file) || !Directory.Exists(folder)) return;

        var ini = Path.Combine(folder, DesktopIni);
        var lines = File.Exists(ini) ? File.ReadAllLines(ini) : [];
        var current = IconResource(lines);
        if (current is not null && !IsOurs(current, icon)) return;

        var value = $"{file},0";
        var changed = !string.Equals(current, value, StringComparison.OrdinalIgnoreCase);
        if (changed) Write(ini, WithIconResource(lines, value));

        // Hidden and System keep the file out of sight; the folder's read-only attribute is what
        // makes Explorer read it at all.
        const FileAttributes hidden = FileAttributes.Hidden | FileAttributes.System;
        var attributes = File.GetAttributes(ini);
        if ((attributes & hidden) != hidden)
        {
            File.SetAttributes(ini, attributes | hidden);
            changed = true;
        }

        var directory = new DirectoryInfo(folder);
        if (!directory.Attributes.HasFlag(FileAttributes.ReadOnly))
        {
            directory.Attributes |= FileAttributes.ReadOnly;
            changed = true;
        }

        if (changed) Native.NotifyItemChanged(folder);
    }

    private static void Clear(string folder, string icon)
    {
        var ini = Path.Combine(folder, DesktopIni);
        if (!File.Exists(ini)) return;

        var lines = File.ReadAllLines(ini);
        if (IconResource(lines) is not { } current || !IsOurs(current, icon)) return;

        var rest = WithIconResource(lines, null);
        if (rest.Any(line => line.Contains('=')))
        {
            Write(ini, rest);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        }
        else
        {
            File.SetAttributes(ini, FileAttributes.Normal);
            File.Delete(ini);
            var directory = new DirectoryInfo(folder);
            directory.Attributes &= ~FileAttributes.ReadOnly;
        }

        Native.NotifyItemChanged(folder);
    }

    // A hidden file refuses to be overwritten, so its attributes go first. UTF-16 is what
    // Windows reads for a desktop.ini that names paths outside the ANSI code page.
    private static void Write(string ini, IEnumerable<string> lines)
    {
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
        File.WriteAllLines(ini, lines, Encoding.Unicode);
    }

    private static bool IsKey(string text)
    {
        var equals = text.IndexOf('=');
        return equals > 0 && text[..equals].Trim().Equals(Key, StringComparison.OrdinalIgnoreCase);
    }

    // A GistDrive icon is the named file in an icons folder, wherever the exe lived at the time.
    private static bool IsOurs(string iconResource, string icon)
    {
        var comma = iconResource.LastIndexOf(',');
        var file = comma < 0 ? iconResource : iconResource[..comma];
        return Path.GetFileName(file).Equals(icon, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(file)), IconFolder, StringComparison.OrdinalIgnoreCase);
    }
}
