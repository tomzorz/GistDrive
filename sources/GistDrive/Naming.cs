namespace GistDrive;

/// <summary>
/// Names on disk: what Windows allows, the short gist ID that keeps clashing names apart, the
/// .url extension of gist shortcuts, and the scratch files that editors leave while saving.
/// </summary>
public static class Naming
{
    public const string ShortcutExtension = ".url";
    public const string ReasonSuffix = ".why.txt";
    public const int MaxFolderNameLength = 100;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly HashSet<string> ScratchExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".swp", ".swo", ".swx", ".crdownload", ".part", ".partial",
    };

    private static readonly HashSet<string> ScratchNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "4913", "desktop.ini", "thumbs.db", ".ds_store",
    };

    /// <summary>
    /// Makes a name safe for Windows (REQ-LAY-16): each forbidden or control character becomes
    /// "_", trailing dots and spaces go, and a reserved device name gets a leading "_".
    /// </summary>
    public static string Sanitize(string name)
    {
        var replaced = new string([.. name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c)]).TrimEnd(' ', '.');
        if (replaced.Length == 0) return "_";
        return ReservedNames.Contains(replaced.Split('.')[0]) ? "_" + replaced : replaced;
    }

    /// <summary>
    /// The folder name of a multi-file gist (REQ-LAY-5, REQ-LAY-6): its description, or its
    /// first file in ordinal order when the description is empty, cut to MaxFolderNameLength.
    /// </summary>
    public static string GistFolderName(Gist gist)
    {
        var description = gist.Description.Trim();
        var source = description.Length > 0 ? description : gist.Files.Select(f => f.Name).Order(StringComparer.Ordinal).First();
        var name = Sanitize(source);
        return name.Length > MaxFolderNameLength ? Sanitize(name[..MaxFolderNameLength]) : name;
    }

    public static string ShortId(string gistId) => gistId.Length > 7 ? gistId[..7] : gistId;

    /// <summary>
    /// Inserts the short gist ID before the extension (REQ-LAY-7): gistfile1.txt becomes
    /// gistfile1 (a1b2c3d).txt.
    /// </summary>
    public static string WithShortId(string name, string gistId) => WithMarker(name, $" ({ShortId(gistId)})");

    /// <summary>
    /// Appends the short gist ID to a folder name (REQ-LAY-10). Folders have no extension.
    /// </summary>
    public static string WithFolderShortId(string name, string gistId) => $"{name} ({ShortId(gistId)})";

    /// <summary>
    /// Removes the short gist ID that WithShortId inserted (REQ-REN-3). Other parentheses, like
    /// the " (2)" of a copy, stay.
    /// </summary>
    public static string WithoutShortId(string name, string gistId)
    {
        var marker = $" ({ShortId(gistId)})";
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        if (stem.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) return stem[..^marker.Length] + extension;
        return name.EndsWith(marker, StringComparison.OrdinalIgnoreCase) ? name[..^marker.Length] : name;
    }

    /// <summary>
    /// Inserts a marker before the extension, or appends it when the name has no stem
    /// (".gitignore" becomes ".gitignore (2)").
    /// </summary>
    public static string WithMarker(string name, string marker)
    {
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        return stem.Length == 0 ? name + marker : stem + marker + extension;
    }

    public static string ShortcutFileName(string shortcutName) => shortcutName + ShortcutExtension;

    public static bool IsShortcut(string path) => path.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The shortcut name of a gist shortcut path: its file name without .url.
    /// </summary>
    public static string ShortcutName(string path) => Path.GetFileName(path)[..^ShortcutExtension.Length];

    /// <summary>
    /// The files that editors, Office and the shell write next to the real file while saving,
    /// plus GistDrive's own reason files. GistDrive never uploads them (REQ-UP-15).
    /// </summary>
    public static bool IsScratch(string fileName) =>
        fileName.StartsWith('~') || fileName.StartsWith(".~", StringComparison.Ordinal)
        || fileName.StartsWith('#') || fileName.StartsWith(".#", StringComparison.Ordinal)
        || fileName.EndsWith('~') || fileName.EndsWith("___", StringComparison.Ordinal)
        || fileName.EndsWith(ReasonSuffix, StringComparison.OrdinalIgnoreCase)
        || ScratchNames.Contains(fileName)
        || ScratchExtensions.Contains(Path.GetExtension(fileName));
}
