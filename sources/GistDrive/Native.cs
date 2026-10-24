using System.Runtime.InteropServices;

namespace GistDrive;

/// <summary>
/// The few Win32 calls GistDrive needs.
/// </summary>
internal static partial class Native
{
    private const int ShcneAssocChanged = 0x08000000;
    private const int ShcneUpdateItem = 0x00002000;
    private const uint ShcnfPathW = 0x0005;
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Tells File Explorer that registrations changed, so the sidebar entry and the menu
    /// entries appear or go without a sign-out.
    /// </summary>
    public static void NotifyAssociationsChanged() => SHChangeNotify(ShcneAssocChanged, 0, 0, 0);

    /// <summary>
    /// Tells File Explorer to redraw one file or folder, for example after its icon changed.
    /// </summary>
    public static void NotifyItemChanged(string path) => SHChangeNotifyPath(ShcneUpdateItem, ShcnfPathW, path, 0);

    /// <summary>
    /// Lets the WinExe print to the console it was started from, for install and uninstall.
    /// </summary>
    public static void AttachToParentConsole() => AttachConsole(AttachParentProcess);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);

    [LibraryImport("shell32.dll", EntryPoint = "SHChangeNotify", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotifyPath(int eventId, uint flags, string item1, nint item2);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
