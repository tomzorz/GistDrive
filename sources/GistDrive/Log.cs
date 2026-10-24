namespace GistDrive;

/// <summary>
/// Appends lines to gistdrive.log next to the executable. The file starts over once it passes
/// 1 MB. Until Open runs (in tests, for example) logging does nothing.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _path;

    public static void Open(string directory)
    {
        _path = Path.Combine(directory, "gistdrive.log");
        if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes) File.Delete(_path);
    }

    public static void Info(string message) => Write(message);

    public static void Error(string message, Exception exception) => Write($"{message}: {exception}");

    private static void Write(string line)
    {
        if (_path is null) return;

        lock (Gate)
        {
            try
            {
                File.AppendAllText(_path, $"{DateTime.Now:O} {line}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Losing a log line beats crashing the engine.
            }
        }
    }
}
