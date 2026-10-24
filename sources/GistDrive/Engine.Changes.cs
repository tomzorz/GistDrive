namespace GistDrive;

/// <summary>
/// What the engine does when something changes on disk: settle new and saved files, upload
/// them, and react to deleted and renamed gist shortcuts and gist folders.
/// </summary>
public sealed partial class Engine
{
    // Downloads from Edit, with the hash of what GistDrive wrote (REQ-EDIT-5).
    private readonly Dictionary<string, string> _editCopies = new(StringComparer.OrdinalIgnoreCase);

    // Deleted gist shortcuts and gist folders that wait for the batch window (OPEN-14).
    private readonly List<(string Path, bool ByVerb)> _deletions = [];

    private void OnChanged(string path)
    {
        // A restore comes first: GistDrive never writes a shortcut that waits for deletion, so
        // one reappearing is always the owner's doing, however soon after GistDrive's own write.
        if (RestoreDeletionTarget(path) || IsOwnWrite(path)) return;

        var location = _folders.Locate(path);
        switch (location.Place)
        {
            case Place.InTop when Directory.Exists(path):
                if (!IsGistFolder(path)) Settle(path, _timings.FolderSettle);
                break;
            case Place.InSubfolder or Place.Deeper when !IsGistFolder(location.Subfolder!):
                Settle(location.Subfolder!, _timings.FolderSettle);
                break;
            case Place.InTop or Place.InSubfolder when Naming.IsShortcut(path):
                _index.Read(path);
                break;
            case Place.InTop or Place.InSubfolder when File.Exists(path) && !Naming.IsScratch(Path.GetFileName(path)) && !IsHiddenOrSystem(path):
                Settle(path, _timings.Settle);
                break;
        }
    }

    private void OnDeleted(string path)
    {
        if (IsOwnDelete(path)) return;

        var location = _folders.Locate(path);
        if (location.Place == Place.TopFolder)
        {
            // A top folder is never a gist. Put it back and let a refresh refill it.
            _folders.EnsureTopFolders();
            RequestRefresh();
            return;
        }

        if (location.Place == Place.Failed)
        {
            RemoveReasonFile(path);
            return;
        }

        StopSettling(path);
        _editCopies.Remove(path);
        _cancelled.Remove(path);
        DropPendingUpload(path);
        var isGistShortcut = _index.Get(path) is not null;
        var isGistFolder = location.Place == Place.InTop && IsGistFolder(path);
        if (isGistShortcut || isGistFolder) QueueDeletion(path, byVerb: false);
    }

    private async Task OnRenamedAsync(string oldPath, string newPath)
    {
        if (IsOwnMove(oldPath, newPath)) return;

        if (_folders.Locate(oldPath).Place == Place.Failed)
        {
            // A file that leaves a failed folder leaves its reason file behind (OPEN-18) and counts as new where it lands.
            RemoveReasonFile(oldPath);
            OnChanged(newPath);
            return;
        }

        if (_index.Get(oldPath) is { } shortcut)
        {
            await OnShortcutRenamedAsync(oldPath, newPath, shortcut);
            return;
        }

        if (_folders.Locate(oldPath).Place == Place.InTop && IsGistFolder(oldPath))
        {
            await OnGistFolderRenamedAsync(oldPath, newPath);
            return;
        }

        // Anything else: an editor renaming its temporary file into place, or a new folder getting its name.
        StopSettling(oldPath);
        DropPendingUpload(oldPath);
        _cancelled.Rename(oldPath, newPath);
        OnChanged(newPath);
    }

    private void OnSettled(Settled settled)
    {
        if (!_settling.TryGetValue(settled.Path, out var generation) || generation != settled.Generation) return;

        _settling.Remove(settled.Path);
        if (Directory.Exists(settled.Path)) OnFolderSettled(settled.Path);
        else if (File.Exists(settled.Path)) OnFileSettled(settled.Path);
    }

    private void OnFileSettled(string path)
    {
        if (!IsFree(path))
        {
            Settle(path, _timings.Settle);
            return;
        }

        var location = _folders.Locate(path);
        if (location.Place is not (Place.InTop or Place.InSubfolder)) return;
        if (_editCopies.TryGetValue(path, out var copy) && copy == Hash(path)) return;
        if (_cancelled.Holds(path, File.GetLastWriteTimeUtc(path))) return;

        var read = TextFile.Read(path);
        if (read.Text?.Length == 0) return; // An empty file is still being written; wait for content.
        if (!read.IsText)
        {
            MoveToFailed(path, location.Visibility, $"GistDrive did not upload {Path.GetFileName(path)} because {read.Reason}.");
            return;
        }

        // A file with the shortcut name of a gist shortcut updates that gist file (REQ-UP-4).
        // Otherwise it becomes a new gist (REQ-UP-1) or joins the gist of its gist folder (REQ-UP-5).
        var folder = Path.GetDirectoryName(path)!;
        var plan = _index.Named(folder, Path.GetFileName(path)) is { Value: var shortcut }
            ? new UploadPlan(path, location.Visibility, shortcut.GistId, shortcut.FileName, null, KeepLocal: false)
            : new UploadPlan(path, location.Visibility, location.Place == Place.InSubfolder ? _index.GistIdOfFolder(folder) : null, null, null, KeepLocal: false);
        Schedule(plan, path);
    }

    private void OnFolderSettled(string folder)
    {
        var location = _folders.Locate(folder);
        if (location.Place != Place.InTop || IsGistFolder(folder) || !Directory.EnumerateFileSystemEntries(folder).Any()) return;

        // A folder of gist shortcuts is a gist folder on the move or a copy of one, never a new gist.
        if (HoldsShortcutsOf(folder, null)) return;
        if (Directory.EnumerateFiles(folder).Any(f => !IsFree(f)))
        {
            Settle(folder, _timings.FolderSettle);
            return;
        }

        if (_cancelled.Holds(folder, FolderStamp(folder))) return;

        try
        {
            if (FolderFiles(folder).Count == 0) return;
        }
        catch (NotUploadableException ex)
        {
            // OPEN-21: a folder that cannot become a gist goes to the failed folder whole.
            MoveToFailed(folder, location.Visibility, $"GistDrive did not upload the folder {Path.GetFileName(folder)} because {ex.Message}.");
            return;
        }

        Schedule(new FolderPlan(folder, location.Visibility), folder);
    }

    private async Task OnShortcutRenamedAsync(string oldPath, string newPath, GistShortcut shortcut)
    {
        var name = Naming.ShortcutName(oldPath);
        if (!SamePath(Path.GetDirectoryName(oldPath), Path.GetDirectoryName(newPath)) || !Naming.IsShortcut(newPath))
        {
            // OPEN-22: gist shortcuts stay where the mirror puts them.
            PutBackWithNotice(newPath, oldPath);
            return;
        }

        _index.Remove(oldPath);
        _index.Set(newPath, shortcut);
        var newName = Naming.WithoutShortId(Naming.ShortcutName(newPath), shortcut.GistId);
        if (newName == shortcut.FileName) return;

        try
        {
            // REQ-REN-1: the gist file takes the new shortcut name.
            var gist = await _api.UpdateAsync(shortcut.GistId, GistUpdate.Rename(shortcut.FileName, newName), _stop.Token);
            var renamed = shortcut with { FileName = newName, Url = gist.Files.Count > 1 ? Layout.FileUrl(gist.HtmlUrl, newName) : gist.HtmlUrl };
            ExpectWrite(newPath);
            renamed.Write(newPath);
            _index.Set(newPath, renamed);
            RequestRefresh();
            Log.Info($"done: renamed {shortcut.FileName} to {newName}");
        }
        catch (GistApiException ex)
        {
            // OPEN-19: the old name comes back, with the reason.
            PutBack(newPath, oldPath);
            _notifier.ShowMessage("Rename failed", $"{name} keeps its name: {ex.Explanation}.");
        }
    }

    private async Task OnGistFolderRenamedAsync(string oldPath, string newPath)
    {
        var gistId = _index.GistIdOfFolder(oldPath)!;
        var name = Path.GetFileName(oldPath);
        if (_folders.Locate(newPath).Place != Place.InTop || !SamePath(Path.GetDirectoryName(oldPath), Path.GetDirectoryName(newPath)))
        {
            PutBackWithNotice(newPath, oldPath);
            return;
        }

        _index.MoveFolder(oldPath, newPath);
        try
        {
            // REQ-REN-2: the gist description takes the new folder name.
            await _api.UpdateAsync(gistId, GistUpdate.NewDescription(Naming.WithoutShortId(Path.GetFileName(newPath), gistId)), _stop.Token);
            RequestRefresh();
            Log.Info($"done: described gist {gistId} as {Path.GetFileName(newPath)}");
        }
        catch (GistApiException ex)
        {
            PutBack(newPath, oldPath);
            _notifier.ShowMessage("Rename failed", $"{name} keeps its name: {ex.Explanation}.");
        }
    }

    private void QueueDeletion(string path, bool byVerb)
    {
        if (_deletions.Any(d => SamePath(d.Path, path))) return;

        _deletions.Add((path, byVerb));
        if (_deletions.Count == 1) PostAfter(_timings.DeletionBatch, new DeletionBatchDue());
    }

    private void OnDeletionBatch()
    {
        var batch = _deletions.ToList();
        _deletions.Clear();

        // The watcher reports a move between folders as a deletion and a creation. A deleted gist
        // shortcut or gist folder that turned up elsewhere in the mirror was moved: it goes back (OPEN-22).
        var moved = batch.Where(d => !d.ByVerb && PutBackMoved(d.Path)).ToList();
        foreach (var group in batch.Except(moved).GroupBy(d => d.ByVerb))
        {
            var targets = Targets(group.Select(d => d.Path));
            if (targets.Count == 0) continue;

            // Delete gist always deletes (REQ-DEL-11); a plain delete follows the deletion setting (REQ-DEL-3 to REQ-DEL-9).
            if (group.Key || _config.DeletionSetting == DeletionSetting.On) Schedule(new DeletePlan(targets, LocalGone: !group.Key), null);
            else BringBack(targets);
        }
    }

    /// <summary>
    /// Turns deleted paths into deletion targets. A gist folder swallows the shortcuts in it, a
    /// shortcut in a gist folder removes one file, and a shortcut in a top folder is a whole gist.
    /// </summary>
    private List<DeleteTarget> Targets(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        var gistFolders = list.Where(p => _folders.Locate(p).Place == Place.InTop && IsGistFolder(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = gistFolders.Select(f => new DeleteTarget(_index.GistIdOfFolder(f)!, null, f, IsFolder: true)).ToList();
        var shortcuts = new List<DeleteTarget>();
        foreach (var path in list)
        {
            if (gistFolders.Contains(path) || gistFolders.Contains(Path.GetDirectoryName(path)!) || _index.Get(path) is not { } shortcut) continue;

            var inGistFolder = _folders.Locate(path).Place == Place.InSubfolder;
            shortcuts.Add(new DeleteTarget(shortcut.GistId, inGistFolder ? shortcut.FileName : null, path, IsFolder: false));
        }

        // Removing every file of a gist folder one by one means deleting the gist.
        foreach (var files in shortcuts.Where(t => t.FileName is not null).GroupBy(t => Path.GetDirectoryName(t.LocalPath)!, StringComparer.OrdinalIgnoreCase))
        {
            if (files.Count() < _index.In(files.Key).Count()) targets.AddRange(files);
            else targets.Add(new DeleteTarget(files.First().GistId, null, files.Key, IsFolder: true));
        }

        targets.AddRange(shortcuts.Where(t => t.FileName is null));
        return targets;
    }

    /// <summary>
    /// With the deletion setting off, deleted gist shortcuts come back (REQ-DEL-3 to REQ-DEL-6).
    /// </summary>
    private void BringBack(IReadOnlyList<DeleteTarget> targets)
    {
        foreach (var target in targets) Recreate(target);
        var subject = targets.Count == 1 ? DisplayName(targets[0].LocalPath) : $"{targets.Count} items";
        _notifier.ShowMessage("Gist deletion is off", $"{subject} will reappear. To delete a gist, use Delete gist in its right-click menu.");
    }

    private void Recreate(DeleteTarget target)
    {
        if (target.IsFolder)
        {
            ExpectWrite(target.LocalPath);
            Directory.CreateDirectory(target.LocalPath);
            foreach (var (path, shortcut) in _index.In(target.LocalPath).ToList()) Rewrite(path, shortcut);
            return;
        }

        if (_index.Get(target.LocalPath) is { } single) Rewrite(target.LocalPath, single);
    }

    private void Rewrite(string path, GistShortcut shortcut)
    {
        if (File.Exists(path)) return;
        ExpectWrite(path);
        shortcut.Write(path);
    }

    /// <summary>
    /// A gist shortcut or gist folder restored from the Recycle Bin during its undo window
    /// stops its pending deletion (REQ-UNDO-12).
    /// </summary>
    private bool RestoreDeletionTarget(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        foreach (var pending in _pending.Values.Where(p => p.Plan is DeletePlan { LocalGone: true }).ToList())
        {
            var plan = (DeletePlan)pending.Plan;
            var restored = plan.Targets.Where(t => SamePath(t.LocalPath, path)).ToList();
            if (restored.Count == 0) continue;

            var rest = plan.Targets.Except(restored).ToList();
            if (rest.Count > 0) pending.Plan = plan with { Targets = rest };
            else Drop(pending);
            if (Naming.IsShortcut(path)) _index.Read(path);
            Log.Info($"restored {path}, its deletion is off");
            return true;
        }

        return false;
    }

    private bool PutBackMoved(string oldPath)
    {
        if (File.Exists(oldPath) || Directory.Exists(oldPath) || FindMovedCopy(oldPath) is not { } copy) return false;

        PutBackWithNotice(copy, oldPath);
        return true;
    }

    /// <summary>
    /// Finds where a deleted gist shortcut or gist folder went inside the mirror, if it went there.
    /// </summary>
    private string? FindMovedCopy(string oldPath)
    {
        if (_index.Get(oldPath) is { } shortcut)
        {
            return _index.Shortcuts
                .Where(p => !SamePath(p.Key, oldPath) && p.Value.GistId == shortcut.GistId && p.Value.FileName == shortcut.FileName && File.Exists(p.Key))
                .Select(p => p.Key)
                .FirstOrDefault();
        }

        if (_index.GistIdOfFolder(oldPath) is not { } gistId) return null;

        return _folders.TopFolders
            .SelectMany(Directory.EnumerateDirectories)
            .FirstOrDefault(d => !SamePath(d, oldPath) && !IsGistFolder(d) && HoldsShortcutsOf(d, gistId));
    }

    private static bool HoldsShortcutsOf(string folder, string? gistId) =>
        Directory.EnumerateFiles(folder, "*.url").Any(f => GistShortcut.TryRead(f) is { } s && (gistId is null || s.GistId == gistId));

    private void PutBackWithNotice(string from, string to)
    {
        var name = DisplayName(to);
        var crossed = _folders.Locate(from) is { Place: not Place.Outside } moved && moved.Visibility != _folders.Locate(to).Visibility;
        PutBack(from, to);
        _notifier.ShowMessage("Gist shortcuts stay put", crossed
            ? $"{name} went back. To make a secret gist public, use Make public in its right-click menu."
            : $"{name} went back to where GistDrive keeps it.");
    }

    /// <summary>
    /// Moves a gist shortcut or gist folder back after a move or rename that did not stick.
    /// </summary>
    private void PutBack(string from, string to)
    {
        if (File.Exists(to) || Directory.Exists(to)) return;

        ExpectMove(from, to);
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
            _index.MoveFolder(from, to);
            return;
        }

        if (!File.Exists(from)) return;
        File.Move(from, to);
        if (_index.Get(from) is { } shortcut)
        {
            _index.Remove(from);
            _index.Set(to, shortcut);
        }
    }

    private void RemoveReasonFile(string failedPath)
    {
        var reasonFile = failedPath + Naming.ReasonSuffix;
        if (!File.Exists(reasonFile)) return;
        ExpectDelete(reasonFile);
        File.Delete(reasonFile);
    }

    /// <summary>
    /// The text files of a folder that is about to become a gist. Throws when the folder cannot
    /// become one: a subfolder, or a file that fails the text check.
    /// </summary>
    private static List<(string Path, string Name, string Text)> FolderFiles(string folder)
    {
        if (Directory.EnumerateDirectories(folder).Any()) throw new NotUploadableException("it contains a subfolder, and a gist cannot hold one");

        var files = new List<(string Path, string Name, string Text)>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (Naming.IsScratch(name) || IsHiddenOrSystem(path)) continue;

            var read = TextFile.Read(path);
            if (!read.IsText) throw new NotUploadableException($"{name} failed the text check: {read.Reason}");
            if (read.Text!.Length > 0) files.Add((path, name, read.Text));
        }

        return files;
    }

    private static DateTime FolderStamp(string folder) =>
        Directory.EnumerateFiles(folder).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
}

/// <summary>
/// A file or folder that GistDrive cannot upload, with the reason.
/// </summary>
public sealed class NotUploadableException(string reason) : Exception(reason);
