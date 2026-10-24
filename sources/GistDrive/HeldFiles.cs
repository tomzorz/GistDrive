using System.Text.Json;

namespace GistDrive;

/// <summary>
/// The files and folders GistDrive must not upload until they change: uploads the owner
/// cancelled (REQ-UNDO-16) and Edit downloads nobody has changed yet (REQ-EDIT-6). Each comes with its
/// stamp at the moment it was held. The record lives in held.json next to the executable, so a
/// hold outlives a restart: every startup scans the top folders, and without the record a
/// cancelled public file would get a fresh countdown that nobody might be watching.
/// </summary>
public sealed class HeldFiles
{
    public const string FileName = "held.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Dictionary<string, DateTime> _stamps;

    private HeldFiles(string path, Dictionary<string, DateTime> stamps)
    {
        _path = path;
        _stamps = stamps;
    }

    /// <summary>
    /// Reads the record, or starts an empty one when the file is missing or unreadable.
    /// </summary>
    public static HeldFiles Load(string path)
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
            Log.Error($"could not read {path}, so nothing counts as held", ex);
        }

        return new HeldFiles(path, stamps);
    }

    /// <summary>
    /// True when the file or folder is held and has not changed since: the stamp is still the
    /// one from the moment it was held.
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
    /// Moves a hold to the new name of a renamed file or folder, because a rename is not a save.
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
            Log.Error($"could not write {_path}, so a restart forgets the latest hold", ex);
        }
    }
}
