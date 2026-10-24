using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace GistDrive;

/// <summary>
/// Creates and reads Windows shortcut (.lnk) files through the shell's own ShellLink object,
/// for the Send To entries and the Startup shortcut. The object wants a single-threaded
/// apartment, so each call runs on a short-lived STA thread.
/// </summary>
public static class ShellLink
{
    public static void Create(string path, string target, string arguments, string description) => OnStaThread(() =>
    {
        var link = (IShellLinkW)new ShellLinkObject();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(description);
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    });

    /// <summary>
    /// Reads the target path and the arguments of a .lnk file.
    /// </summary>
    public static (string Target, string Arguments) Read(string path)
    {
        (string, string) result = default;
        OnStaThread(() =>
        {
            var link = (IShellLinkW)new ShellLinkObject();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, 0, 0);
                var arguments = new StringBuilder(1024);
                link.GetArguments(arguments, arguments.Capacity);
                result = (target.ToString(), arguments.ToString());
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        });
        return result;
    }

    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException($"Shell link call failed: {failure.Message}", failure);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkObject
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, nint findData, int flags);

        void GetIDList(out nint idList);

        void SetIDList(nint idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int capacity);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);

        void Resolve(nint window, int flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
