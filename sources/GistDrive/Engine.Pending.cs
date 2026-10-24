namespace GistDrive;

/// <summary>
/// Pending actions: each upload, deletion and Make public waits for its window with a countdown
/// notification (REQ-UNDO-1 to REQ-UNDO-13), then runs, retries, or gives up.
/// </summary>
public sealed partial class Engine
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(2);

    private readonly Dictionary<Guid, Pending> _pending = [];
    private readonly Dictionary<string, Guid> _pendingByKey = new(StringComparer.OrdinalIgnoreCase);

    private void Schedule(Plan plan, string? key)
    {
        var window = plan switch
        {
            UploadPlan upload => WindowFor(upload.Visibility),
            FolderPlan folder => WindowFor(folder.Visibility),
            _ => _config.UndoWindow,
        };
        if (key is not null && _pendingByKey.TryGetValue(key, out var id) && _pending.TryGetValue(id, out var existing))
        {
            // Another save restarts the window, and the upload sends the latest content (REQ-UNDO-9, OPEN-10).
            existing.Plan = plan;
            existing.Window = window;
            existing.DueAt = DateTimeOffset.UtcNow + window;
            if (existing.Notified) _notifier.UpdateCountdown(existing.Tag, window, window);
            PostAfter(window, new PendingDue(existing.Id));
            return;
        }

        var pending = new Pending(plan, key, window);
        _pending[pending.Id] = pending;
        if (key is not null) _pendingByKey[key] = pending.Id;
        if (window > TimeSpan.Zero)
        {
            // OPEN-30: a secret upload delay above 0 seconds gets the same countdown.
            var (title, body) = Describe(plan);
            _notifier.ShowCountdown(pending.Tag, title, body, window, window);
            pending.Notified = true;
            Log.Info($"waiting {window.TotalSeconds:0} s: {title}");
        }

        PostAfter(window, new PendingDue(pending.Id));
    }

    private TimeSpan WindowFor(Visibility visibility) => visibility == Visibility.Public ? _config.UndoWindow : _config.SecretUploadDelay;

    private async Task OnPendingDueAsync(Guid id)
    {
        if (!_pending.TryGetValue(id, out var pending) || pending.DueAt > DateTimeOffset.UtcNow + Slack) return;

        HideCountdown(pending);
        try
        {
            await ExecuteAsync(pending.Plan);
            Forget(pending);
            RequestRefresh();
            Log.Info($"done: {Describe(pending.Plan).Title}");
        }
        catch (GistApiException ex) when (ex.IsTransient && pending.StillRetrying(_config.RetryPeriod))
        {
            var delay = TimeSpan.FromTicks(Math.Min(_timings.RetryDelay.Ticks << Math.Min(pending.Attempts++, 10), MaxRetryDelay.Ticks));
            pending.DueAt = DateTimeOffset.UtcNow + delay;
            PostAfter(delay, new PendingDue(pending.Id));
            Log.Info($"retrying in {delay.TotalSeconds:0} s: {ex.Message}");
        }
        catch (Exception ex) when (ex is GistApiException or NotUploadableException or IOException or UnauthorizedAccessException)
        {
            Forget(pending);
            GiveUp(pending.Plan, ex);
            RequestRefresh();
        }
    }

    private void OnCancel(Guid id)
    {
        if (!_pending.TryGetValue(id, out var pending)) return;

        Drop(pending);
        switch (pending.Plan)
        {
            // OPEN-11: the file stays, and only a new save uploads it, also after a restart.
            case UploadPlan { KeepLocal: false } upload when File.Exists(upload.LocalPath):
                _cancelled.Add(upload.LocalPath, File.GetLastWriteTimeUtc(upload.LocalPath));
                break;
            case FolderPlan folder when Directory.Exists(folder.FolderPath):
                _cancelled.Add(folder.FolderPath, FolderStamp(folder.FolderPath));
                break;
            case DeletePlan { LocalGone: true } deletion:
                foreach (var target in deletion.Targets) Recreate(target);
                break;
        }

        Log.Info($"cancelled {pending.Plan}");
    }

    /// <summary>
    /// Stops the pending upload of a file or folder that went away during its window.
    /// </summary>
    private void DropPendingUpload(string path)
    {
        if (_pendingByKey.TryGetValue(path, out var id) && _pending.TryGetValue(id, out var pending) && pending.Plan is UploadPlan or FolderPlan) Drop(pending);
    }

    private void Drop(Pending pending)
    {
        HideCountdown(pending);
        Forget(pending);
    }

    private void HideCountdown(Pending pending)
    {
        if (!pending.Notified) return;
        _notifier.Remove(pending.Tag);
        pending.Notified = false;
    }

    private void Forget(Pending pending)
    {
        _pending.Remove(pending.Id);
        if (pending.Key is not null && _pendingByKey.TryGetValue(pending.Key, out var id) && id == pending.Id) _pendingByKey.Remove(pending.Key);
    }

    /// <summary>
    /// True for a gist file with a pending deletion. A refresh leaves it alone, so a deleted
    /// shortcut does not come back during its undo window.
    /// </summary>
    private bool IsHeld(string gistId, string? fileName) =>
        _pending.Values
            .Select(p => p.Plan)
            .OfType<DeletePlan>()
            .SelectMany(d => d.Targets)
            .Any(t => t.GistId == gistId && (t.FileName is null || t.FileName == fileName));

    private Task ExecuteAsync(Plan plan) => plan switch
    {
        UploadPlan upload => UploadAsync(upload),
        FolderPlan folder => UploadFolderAsync(folder),
        DeletePlan deletion => DeleteAsync(deletion),
        MakePublicPlan makePublic => MakePublicAsync(makePublic),
        _ => throw new InvalidOperationException($"No way to run {plan}"),
    };

    private async Task UploadAsync(UploadPlan plan)
    {
        if (!File.Exists(plan.LocalPath)) return;

        var read = TextFile.Read(plan.LocalPath);
        if (!read.IsText) throw new NotUploadableException(read.Reason!);

        var name = Path.GetFileName(plan.LocalPath);
        var text = read.Text!;
        if (plan.GistId is null)
        {
            var gist = await _api.CreateAsync(plan.Visibility == Visibility.Public, string.Empty, new Dictionary<string, string> { [name] = text }, _stop.Token);
            var folder = plan.KeepLocal ? _folders.Top(plan.Visibility) : Path.GetDirectoryName(plan.LocalPath)!;
            WriteShortcut(folder, new GistShortcut(gist.Id, name, gist.HtmlUrl, plan.SourcePath));
        }
        else if (plan.GistFile is null)
        {
            var gist = await _api.UpdateAsync(plan.GistId, GistUpdate.Content(name, text), _stop.Token);
            WriteShortcut(Path.GetDirectoryName(plan.LocalPath)!, new GistShortcut(gist.Id, name, Layout.FileUrl(gist.HtmlUrl, name)));
        }
        else
        {
            var current = await _api.GetAsync(plan.GistId, _stop.Token);
            if (current.File(plan.GistFile) is { Truncated: false, Content: { } content } && content == text)
            {
                // Nothing changed, as with a fresh Edit download (REQ-EDIT-5): the file stays where it is.
                if (!plan.KeepLocal) _editCopies[plan.LocalPath] = Hash(plan.LocalPath);
                return;
            }

            await _api.UpdateAsync(plan.GistId, GistUpdate.Content(plan.GistFile, text), _stop.Token);
        }

        if (plan.KeepLocal) return;

        // REQ-UP-8: the gist shortcut takes the place of the uploaded file.
        ExpectDelete(plan.LocalPath);
        File.Delete(plan.LocalPath);
        _editCopies.Remove(plan.LocalPath);
        _cancelled.Remove(plan.LocalPath);
    }

    private async Task UploadFolderAsync(FolderPlan plan)
    {
        if (!Directory.Exists(plan.FolderPath)) return;

        var files = FolderFiles(plan.FolderPath);
        if (files.Count == 0) return;

        // REQ-UP-6, REQ-UP-7: the folder becomes one gist, described by the folder name.
        var gist = await _api.CreateAsync(plan.Visibility == Visibility.Public, Path.GetFileName(plan.FolderPath), files.ToDictionary(f => f.Name, f => f.Text), _stop.Token);
        foreach (var file in files)
        {
            ExpectDelete(file.Path);
            File.Delete(file.Path);
            var url = gist.Files.Count > 1 ? Layout.FileUrl(gist.HtmlUrl, file.Name) : gist.HtmlUrl;
            WriteShortcut(plan.FolderPath, new GistShortcut(gist.Id, file.Name, url));
        }

        _cancelled.Remove(plan.FolderPath);
    }

    private async Task DeleteAsync(DeletePlan plan)
    {
        foreach (var target in plan.Targets)
        {
            try
            {
                if (target.FileName is null) await _api.DeleteAsync(target.GistId, _stop.Token);
                else await _api.UpdateAsync(target.GistId, GistUpdate.Remove(target.FileName), _stop.Token);
            }
            catch (GistApiException ex) when (ex.Kind == GistErrorKind.NotFound)
            {
                // Already gone from GitHub, which is the point.
            }

            ForgetLocal(target);
        }
    }

    private async Task MakePublicAsync(MakePublicPlan plan)
    {
        var secret = await _api.GetAsync(plan.GistId, _stop.Token);
        if (secret.IsPublic) return;
        if (secret.Files.Any(f => f.Truncated || f.Content is null)) throw new NotUploadableException("a file in the gist is larger than the 1 MB GitHub returns through its API");

        // REQ-PUB-4, REQ-PUB-5: a public copy first, then the secret gist goes.
        await _api.CreateAsync(true, secret.Description, secret.Files.ToDictionary(f => f.Name, f => f.Content!), _stop.Token);
        try
        {
            await _api.DeleteAsync(secret.Id, _stop.Token);
        }
        catch (GistApiException ex)
        {
            // No retry here: a retry would make a second public copy.
            _notifier.ShowMessage("Secret gist still there", $"{DisplayName(plan.LocalPath)} is public now, but GistDrive could not delete the secret gist: {ex.Explanation}.");
            return;
        }

        // ceiling: a Send To source path does not carry over to the public copy.
        ForgetLocal(new DeleteTarget(secret.Id, null, plan.LocalPath, plan.IsFolder));
    }

    /// <summary>
    /// After a failure that retries cannot fix: an upload's file goes to the failed folder
    /// (REQ-FAIL-3, REQ-FAIL-4), anything else gets a notification (OPEN-19).
    /// </summary>
    private void GiveUp(Plan plan, Exception ex)
    {
        var reason = ex is GistApiException api ? api.Explanation : ex.Message;
        switch (plan)
        {
            case UploadPlan { KeepLocal: false } upload when File.Exists(upload.LocalPath):
                MoveToFailed(upload.LocalPath, upload.Visibility, $"GistDrive could not upload {Path.GetFileName(upload.LocalPath)} because {reason}.");
                break;
            case UploadPlan upload:
                _notifier.ShowMessage("Upload failed", $"{Path.GetFileName(upload.LocalPath)} was not uploaded: {reason}.");
                break;
            case FolderPlan folder when Directory.Exists(folder.FolderPath):
                MoveToFailed(folder.FolderPath, folder.Visibility, $"GistDrive could not upload the folder {Path.GetFileName(folder.FolderPath)} because {reason}.");
                break;
            case DeletePlan deletion:
                foreach (var target in deletion.Targets) Recreate(target);
                _notifier.ShowMessage("Deletion failed", $"{Subject(deletion)} stays on GitHub: {reason}.");
                break;
            case MakePublicPlan makePublic:
                _notifier.ShowMessage("Make public failed", $"{DisplayName(makePublic.LocalPath)} stays secret: {reason}.");
                break;
        }

        Log.Info($"gave up on {plan}: {reason}");
    }

    /// <summary>
    /// Moves a file or folder that cannot be uploaded into the failed folder of its top folder
    /// and writes the reason file next to it (REQ-FAIL-5, OPEN-15).
    /// </summary>
    private void MoveToFailed(string path, Visibility visibility, string reason)
    {
        var origin = Path.GetDirectoryName(path)!;
        var failed = _folders.Failed(visibility);
        Directory.CreateDirectory(failed);
        var target = UniquePath(Path.Combine(failed, Path.GetFileName(path)));
        StopSettling(path);
        ExpectMove(path, target);
        if (Directory.Exists(path)) Directory.Move(path, target);
        else File.Move(path, target);

        var back = SamePath(origin, _folders.Top(visibility)) ? $"the {Path.GetFileName(origin)} folder" : $"the gist folder {Path.GetFileName(origin)}";
        var reasonFile = target + Naming.ReasonSuffix;
        ExpectWrite(reasonFile);
        File.WriteAllText(reasonFile, $"{reason}{Environment.NewLine}{Environment.NewLine}To try again, move {Path.GetFileName(target)} back into {back}.{Environment.NewLine}");
        Log.Info($"moved {path} to {target}: {reason}");
    }

    private string WriteShortcut(string folder, GistShortcut shortcut)
    {
        var name = Naming.Sanitize(shortcut.FileName);
        var path = Path.Combine(folder, Naming.ShortcutFileName(name));
        if (File.Exists(path) && _index.Get(path)?.GistId != shortcut.GistId) path = Path.Combine(folder, Naming.ShortcutFileName(Naming.WithShortId(name, shortcut.GistId)));
        ExpectWrite(path);
        shortcut.Write(path);
        _index.Set(path, shortcut);
        return path;
    }

    private void ForgetLocal(DeleteTarget target)
    {
        if (!target.IsFolder)
        {
            RemoveShortcutFile(target.LocalPath);
            return;
        }

        foreach (var (path, _) in _index.In(target.LocalPath).ToList()) RemoveShortcutFile(path);
        if (Directory.Exists(target.LocalPath) && !Directory.EnumerateFileSystemEntries(target.LocalPath).Any())
        {
            ExpectDelete(target.LocalPath);
            Directory.Delete(target.LocalPath);
        }
    }

    private void RemoveShortcutFile(string path)
    {
        _index.Remove(path);
        if (!File.Exists(path)) return;
        ExpectDelete(path);
        File.Delete(path);
    }

    private (string Title, string Body) Describe(Plan plan) => plan switch
    {
        UploadPlan { GistId: null } upload => (
            $"{Verb(upload.Visibility)} {Path.GetFileName(upload.LocalPath)}",
            $"As a new {Kind(upload.Visibility)} gist."),
        UploadPlan { GistFile: null } upload => (
            $"Adding {Path.GetFileName(upload.LocalPath)}",
            $"To the {Kind(upload.Visibility)} gist {Path.GetFileName(Path.GetDirectoryName(upload.LocalPath))}."),
        UploadPlan upload => (
            $"Updating {Path.GetFileName(upload.LocalPath)}",
            $"In its {Kind(upload.Visibility)} gist."),
        FolderPlan folder => (
            $"{Verb(folder.Visibility)} {Path.GetFileName(folder.FolderPath)}",
            $"The folder becomes one {Kind(folder.Visibility)} gist."),
        DeletePlan deletion => (
            $"Deleting {Subject(deletion)}",
            deletion.Targets is [{ FileName: not null }] ? "The file goes from its gist on GitHub." : "Deleted gists cannot come back."),
        MakePublicPlan makePublic => (
            $"Making {DisplayName(makePublic.LocalPath)} public",
            "A public copy replaces the secret gist, and the link changes."),
        _ => ("GistDrive", plan.ToString()),
    };

    private static string Verb(Visibility visibility) => visibility == Visibility.Public ? "Publishing" : "Uploading";

    private static string Kind(Visibility visibility) => visibility == Visibility.Public ? "public" : "secret";

    private static string Subject(DeletePlan deletion) =>
        deletion.Targets.Count == 1 ? DisplayName(deletion.Targets[0].LocalPath) : $"{deletion.Targets.Count} items";

    private abstract record Plan;

    // Uploads a file: a new gist when GistId is null, a new file of that gist when GistFile is
    // null, an update of GistFile otherwise. KeepLocal is for Send To, whose original stays.
    private sealed record UploadPlan(string LocalPath, Visibility Visibility, string? GistId, string? GistFile, string? SourcePath, bool KeepLocal) : Plan;

    private sealed record FolderPlan(string FolderPath, Visibility Visibility) : Plan;

    // A whole gist when FileName is null, one file of it otherwise. LocalPath is the gist shortcut or gist folder.
    private sealed record DeleteTarget(string GistId, string? FileName, string LocalPath, bool IsFolder);

    // LocalGone is true when the owner deleted the shortcut in Explorer, false for Delete gist.
    private sealed record DeletePlan(IReadOnlyList<DeleteTarget> Targets, bool LocalGone) : Plan;

    private sealed record MakePublicPlan(string GistId, string LocalPath, bool IsFolder) : Plan;

    private sealed class Pending(Plan plan, string? key, TimeSpan window)
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Tag => Id.ToString("N");

        public Plan Plan { get; set; } = plan;

        public string? Key { get; } = key;

        public TimeSpan Window { get; set; } = window;

        public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow + window;

        public bool Notified { get; set; }

        public int Attempts { get; set; }

        private DateTimeOffset? FirstFailure { get; set; }

        /// <summary>
        /// True while transient failures are still inside the retry period (REQ-FAIL-2).
        /// </summary>
        public bool StillRetrying(TimeSpan period)
        {
            FirstFailure ??= DateTimeOffset.UtcNow;
            return DateTimeOffset.UtcNow - FirstFailure < period;
        }
    }
}
