using System.Text.Json;

namespace GistDrive;

/// <summary>
/// The uploads the owner cancelled, each with the write time of its file or folder at the
/// moment of the cancel (OPEN-11). The record lives in cancelled.json next to the executable,
/// so a cancel outlives a restart: every startup scans the top folders, and without the record
/// a cancelled public file would get a fresh countdown that nobody might be watching.
/// </summary>
public sealed class CancelledUploads
{
    public const string FileName = "cancelled.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, DateTime> _stamps;

    private CancelledUploads(string path, Dictionary<string, DateTime> stamps)
    {
        _path = path;
        _stamps = stamps;
    }

    /// <summary>
    /// Reads the record, or starts an empty one when the file is missing or unreadable.
    /// </summary>
    public static CancelledUploads Load(string path)
    {
        var stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(path)) is { } loaded)
            {
                foreach (var (item, stamp) in loaded) stamps[item] = stamp;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error($"could not read {path}, so no upload counts as cancelled", ex);
        }

        return new CancelledUploads(path, stamps);
    }

    /// <summary>
    /// True when the owner cancelled the upload of the file or folder and it has not changed
    /// since: the stamp is still the one from the moment of the cancel.
    /// </summary>
    public bool Holds(string path, DateTime stamp) => _stamps.TryGetValue(path, out var held) && held == stamp;

    public void Add(string path, DateTime stamp)
    {
        _stamps[path] = stamp;
        Save();
    }

    public void Remove(string path)
    {
        if (_stamps.Remove(path)) Save();
    }

    /// <summary>
    /// Moves a cancel to the new name of a renamed file or folder, because a rename is not a save.
    /// </summary>
    public void Rename(string from, string to)
    {
        if (!_stamps.Remove(from, out var stamp)) return;
        _stamps[to] = stamp;
        Save();
    }

    /// <summary>
    /// Forgets the files and folders that are gone, for example deleted while GistDrive was not
    /// running.
    /// </summary>
    public void Prune()
    {
        var gone = _stamps.Keys.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
        if (gone.Count == 0) return;
        foreach (var path in gone) _stamps.Remove(path);
        Save();
    }

    // Through a temporary file, so a crash halfway through cannot leave half a record behind.
    private void Save()
    {
        var temp = _path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(_stamps, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"could not write {_path}, so a restart forgets the latest cancel", ex);
        }
    }
}
