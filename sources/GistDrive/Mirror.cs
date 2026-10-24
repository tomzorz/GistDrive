namespace GistDrive;

/// <summary>
/// The gist shortcuts on disk, as GistDrive last read or wrote them, keyed by path. Because a
/// gist shortcut holds all state of its gist (REQ-LAY-9), scanning the top folders rebuilds it.
/// </summary>
public sealed class MirrorIndex
{
    private readonly Dictionary<string, GistShortcut> _shortcuts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, GistShortcut> Shortcuts => _shortcuts;

    /// <summary>
    /// Reads every gist shortcut directly in a top folder or in a gist folder.
    /// </summary>
    public static MirrorIndex Scan(Folders folders)
    {
        var index = new MirrorIndex();
        foreach (var top in folders.TopFolders.Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(top, "*.url").Where(Naming.IsShortcut)) index.Read(path);
            foreach (var folder in Directory.EnumerateDirectories(top).Where(d => !Path.GetFileName(d).Equals(Folders.FailedName, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var path in Directory.EnumerateFiles(folder, "*.url").Where(Naming.IsShortcut)) index.Read(path);
            }
        }

        return index;
    }

    /// <summary>
    /// Reads one gist shortcut from disk into the index, or drops the path when the file is not one.
    /// </summary>
    public GistShortcut? Read(string path)
    {
        var shortcut = GistShortcut.TryRead(path);
        if (shortcut is null) _shortcuts.Remove(path);
        else _shortcuts[path] = shortcut;
        return shortcut;
    }

    public GistShortcut? Get(string path) => _shortcuts.GetValueOrDefault(path);

    public void Set(string path, GistShortcut shortcut) => _shortcuts[path] = shortcut;

    public void Remove(string path) => _shortcuts.Remove(path);

    /// <summary>
    /// The gist shortcuts directly in a folder.
    /// </summary>
    public IEnumerable<KeyValuePair<string, GistShortcut>> In(string folder) =>
        _shortcuts.Where(p => string.Equals(Path.GetDirectoryName(p.Key), folder, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The gist that a gist folder stands for, or null when no gist shortcut sits in the folder.
    /// </summary>
    // ceiling: linear scan per call, fine for hundreds of gists; index by folder if it ever is not.
    public string? GistIdOfFolder(string folder) => In(folder).Select(p => p.Value.GistId).FirstOrDefault();

    /// <summary>
    /// The gist shortcut in a folder whose shortcut name matches, ignoring case.
    /// </summary>
    public KeyValuePair<string, GistShortcut>? Named(string folder, string shortcutName) =>
        In(folder).Where(p => Naming.ShortcutName(p.Key).Equals(shortcutName, StringComparison.OrdinalIgnoreCase)).Cast<KeyValuePair<string, GistShortcut>?>().FirstOrDefault();

    /// <summary>
    /// Re-keys the gist shortcuts of a folder that moved.
    /// </summary>
    public void MoveFolder(string from, string to)
    {
        foreach (var (path, shortcut) in In(from).ToList())
        {
            _shortcuts.Remove(path);
            _shortcuts[Path.Combine(to, Path.GetFileName(path))] = shortcut;
        }
    }
}

/// <summary>
/// Announces a change GistDrive is about to make, so the file watcher can tell it apart from
/// the owner's own changes.
/// </summary>
public sealed record ChangeAnnouncer(Action<string> Write, Action<string> Delete, Action<string, string> Move);

/// <summary>
/// Makes the disk match a planned mirror (REQ-MIR-4 to REQ-MIR-8). It touches gist shortcuts and gist
/// folders only: a file the owner put in a top folder is never moved or deleted by a refresh.
/// </summary>
public sealed class Reconciler(Folders folders, MirrorIndex index, ChangeAnnouncer announce)
{
    /// <summary>
    /// Applies the plan. Held gist files (with a pending deletion) are neither recreated nor removed.
    /// </summary>
    public void Apply(IReadOnlyList<MirrorEntry> plan, Func<string, string?, bool> isHeld)
    {
        var planned = plan.Where(e => e.FileName is not null).ToDictionary(e => (e.GistId, e.FileName!));
        var plannedFolders = plan.Where(e => e.FileName is null).ToDictionary(e => e.GistId, e => e.Path);
        var sources = index.Shortcuts.Values
            .Where(s => s.SourcePath is not null)
            .GroupBy(s => (s.GistId, s.FileName))
            .ToDictionary(g => g.Key, g => g.First().SourcePath);

        MoveRenamedFolders(plannedFolders);
        var emptied = RemoveWrongShortcuts(planned, isHeld);
        WriteMissing(plan, sources, isHeld);
        foreach (var folder in emptied.Where(f => Directory.Exists(f) && !Directory.EnumerateFileSystemEntries(f).Any()))
        {
            announce.Delete(folder);
            Directory.Delete(folder);
        }
    }

    // A gist folder whose description changed moves as a whole, keeping whatever else sits in it.
    private void MoveRenamedFolders(Dictionary<string, string> plannedFolders)
    {
        foreach (var (gistId, target) in plannedFolders)
        {
            var current = index.Shortcuts
                .Where(p => p.Value.GistId == gistId && folders.Locate(p.Key).Place == Place.InSubfolder)
                .Select(p => Path.GetDirectoryName(p.Key)!)
                .FirstOrDefault();
            if (current is null || string.Equals(current, target, StringComparison.OrdinalIgnoreCase) || Directory.Exists(target) || !Directory.Exists(current)) continue;

            announce.Move(current, target);
            Directory.Move(current, target);
            index.MoveFolder(current, target);
        }
    }

    private HashSet<string> RemoveWrongShortcuts(Dictionary<(string, string), MirrorEntry> planned, Func<string, string?, bool> isHeld)
    {
        var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, shortcut) in index.Shortcuts.ToList())
        {
            if (isHeld(shortcut.GistId, shortcut.FileName)) continue;
            if (planned.TryGetValue((shortcut.GistId, shortcut.FileName), out var entry)
                && string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)
                && entry.Url == shortcut.Url) continue;

            announce.Delete(path);
            File.Delete(path);
            index.Remove(path);
            if (folders.Locate(path).Place == Place.InSubfolder) emptied.Add(Path.GetDirectoryName(path)!);
        }

        return emptied;
    }

    private void WriteMissing(IReadOnlyList<MirrorEntry> plan, Dictionary<(string, string), string?> sources, Func<string, string?, bool> isHeld)
    {
        var skipped = new HashSet<string>();
        var gistFolders = new List<MirrorEntry>();
        foreach (var entry in plan)
        {
            if (skipped.Contains(entry.GistId) || isHeld(entry.GistId, entry.FileName)) continue;

            if (entry.FileName is null)
            {
                // A folder of the owner's that has the gist folder's name and is not that gist folder: leave it alone.
                if (Directory.Exists(entry.Path) && index.In(entry.Path).All(p => p.Value.GistId != entry.GistId) && Directory.EnumerateFileSystemEntries(entry.Path).Any())
                {
                    Log.Info($"refresh: {entry.Path} holds other files, gist {entry.GistId} waits for the next refresh");
                    skipped.Add(entry.GistId);
                    continue;
                }

                if (!Directory.Exists(entry.Path))
                {
                    announce.Write(entry.Path);
                    Directory.CreateDirectory(entry.Path);
                }

                gistFolders.Add(entry);
                continue;
            }

            if (index.Get(entry.Path) is not { } existing || existing.GistId != entry.GistId || existing.FileName != entry.FileName || existing.Url != entry.Url)
            {
                var shortcut = new GistShortcut(entry.GistId, entry.FileName, entry.Url, sources.GetValueOrDefault((entry.GistId, entry.FileName)));
                announce.Write(entry.Path);
                shortcut.Write(entry.Path);
                index.Set(entry.Path, shortcut);
            }

            Stamp(entry);
        }

        // Writing into a folder moves its modification time, so the gist folders get theirs last.
        foreach (var folder in gistFolders) Stamp(folder);
    }

    /// <summary>
    /// Gives a gist shortcut or gist folder the gist's creation and update times from GitHub
    /// (REQ-LAY-12, REQ-LAY-13). A failure goes to the log and the refresh carries on
    /// (REQ-LAY-14, REQ-LAY-15).
    /// </summary>
    private void Stamp(MirrorEntry entry)
    {
        var created = entry.CreatedAt.UtcDateTime;
        var updated = entry.UpdatedAt.UtcDateTime;
        try
        {
            FileSystemInfo item = entry.FileName is null ? new DirectoryInfo(entry.Path) : new FileInfo(entry.Path);
            if (!item.Exists || (Near(item.CreationTimeUtc, created) && Near(item.LastWriteTimeUtc, updated))) return;

            announce.Write(entry.Path);
            item.CreationTimeUtc = created;
            item.LastWriteTimeUtc = updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"refresh: could not set the times of {entry.Path}: {ex.Message}");
        }
    }

    private static bool Near(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) < 1;
}
