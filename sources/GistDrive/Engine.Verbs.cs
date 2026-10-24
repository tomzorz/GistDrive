using System.Text;

namespace GistDrive;

/// <summary>
/// Requests from the menu entries (Edit, Delete gist, Make public), the Send To entries, and
/// the command line.
/// </summary>
public sealed partial class Engine
{
    private async Task OnVerbAsync(VerbRequest request)
    {
        switch (request.Verb)
        {
            case "edit":
                foreach (var path in request.Paths) await EditAsync(path);
                break;
            case "delete":
                foreach (var path in request.Paths) RequestDeletion(path);
                break;
            case "make-public":
                foreach (var path in request.Paths) MakePublic(path);
                break;
            case "send-secret":
                Send(request.Paths, Visibility.Secret);
                break;
            case "send-public":
                Send(request.Paths, Visibility.Public);
                break;
            case "refresh":
                RequestRefresh();
                break;
            default:
                Log.Info($"unknown verb {request.Verb}");
                break;
        }
    }

    /// <summary>
    /// REQ-EDIT-2 to REQ-EDIT-4: downloads the gist file next to its shortcut, under the
    /// shortcut name, and opens it. A file already there just opens.
    /// </summary>
    private async Task EditAsync(string path)
    {
        if (_index.Get(path) is not { } shortcut)
        {
            _notifier.ShowMessage("Not a gist", $"{Path.GetFileName(path)} is not a GistDrive gist shortcut.");
            return;
        }

        var name = Naming.ShortcutName(path);
        var target = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (!File.Exists(target))
        {
            GistFile? file;
            try
            {
                file = (await _api.GetAsync(shortcut.GistId, _stop.Token)).File(shortcut.FileName);
            }
            catch (GistApiException ex)
            {
                _notifier.ShowMessage("Edit failed", $"{name}: {ex.Explanation}.");
                return;
            }

            if (file is not { Truncated: false, Content: { } content })
            {
                _notifier.ShowMessage("Edit failed", file is null ? $"{name} is no longer in its gist." : $"{name} is larger than the 1 MB GitHub returns through its API.");
                return;
            }

            ExpectWrite(target);
            await File.WriteAllTextAsync(target, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), _stop.Token);

            // REQ-EDIT-6: the download stays quiet, across restarts too, until the owner saves it.
            _held.Add(target, File.GetLastWriteTimeUtc(target));
        }

        await _open(target);
    }

    /// <summary>
    /// REQ-DEL-11: Delete gist deletes, whatever the deletion setting says. It goes through the
    /// same batch as plain deletions, so a multi-selection shows one notification.
    /// </summary>
    private void RequestDeletion(string path)
    {
        var isGistFolder = _folders.Locate(path).Place == Place.InTop && IsGistFolder(path);
        if (_index.Get(path) is not null || isGistFolder) QueueDeletion(path, byVerb: true);
        else _notifier.ShowMessage("Not a gist", $"{Path.GetFileName(path)} is not a gist shortcut or a gist folder.");
    }

    /// <summary>
    /// REQ-PUB-1 to REQ-PUB-5: works on a gist shortcut or gist folder directly in the secret
    /// folder (REQ-PUB-7).
    /// </summary>
    private void MakePublic(string path)
    {
        var isFolder = Directory.Exists(path);
        var gistId = _folders.Locate(path) is { Place: Place.InTop, Visibility: Visibility.Secret }
            ? isFolder ? _index.GistIdOfFolder(path) : _index.Get(path)?.GistId
            : null;
        if (gistId is null)
        {
            _notifier.ShowMessage("Not a secret gist", "Make public works on gist shortcuts and gist folders directly in the Secret folder.");
            return;
        }

        Schedule(new MakePublicPlan(gistId, path, isFolder), $"make-public:{gistId}");
    }

    /// <summary>
    /// REQ-SEND-2 to REQ-SEND-8: uploads a copy and leaves the original alone. The same original
    /// sent again updates its gist; another file with the same name becomes a new gist.
    /// </summary>
    private void Send(IEnumerable<string> paths, Visibility visibility)
    {
        var top = _folders.Top(visibility);
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                // REQ-SEND-10: a sent folder behaves like a folder added to the top folder.
                CopyFolder(path, UniquePath(Path.Combine(top, Path.GetFileName(path))));
                continue;
            }

            if (!File.Exists(path)) continue;

            var read = TextFile.Read(path);
            if (!read.IsText || read.Text!.Length == 0)
            {
                _notifier.ShowMessage("Not sent", $"{Path.GetFileName(path)} was not sent: {(read.IsText ? "it is empty" : read.Reason)}.");
                continue;
            }

            var sentBefore = _index.Shortcuts.FirstOrDefault(p => IsUnder(p.Key, top) && SamePath(p.Value.SourcePath, path)).Value;
            Schedule(new UploadPlan(path, visibility, sentBefore?.GistId, sentBefore?.FileName, path, KeepLocal: true), $"send:{visibility}:{path}");
        }
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(source)) CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
    }
}
