namespace GistDrive;

/// <summary>
/// One entry of the mirror: a gist shortcut when FileName is set, a gist folder when it is null.
/// It carries the gist's GitHub times, which the entry's creation and modification times follow.
/// </summary>
public sealed record MirrorEntry(string Path, string GistId, string? FileName, string Url, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Where each gist goes in the mirror (REQ-LAY-1 through REQ-LAY-10).
/// </summary>
public static class Layout
{
    /// <summary>
    /// Plans the mirror for a list of gists. A gist folder comes before the gist shortcuts
    /// inside it, so the plan can be applied in order.
    /// </summary>
    public static IReadOnlyList<MirrorEntry> Plan(Folders folders, IEnumerable<Gist> gists)
    {
        var entries = new List<MirrorEntry>();
        foreach (var group in gists.GroupBy(g => g.Visibility))
        {
            var top = folders.Top(group.Key);
            var named = group.Select(g => (Gist: g, IsFolder: g.Files.Count > 1, Name: g.Files.Count > 1 ? Naming.GistFolderName(g) : Naming.Sanitize(g.Files[0].Name))).ToList();

            // A gist shortcut's file name ends in .url, so shortcuts only clash with shortcuts and folders with folders.
            var clashing = named
                .GroupBy(n => (n.IsFolder, Name: n.Name.ToUpperInvariant()))
                .Where(g => g.Count() > 1)
                .SelectMany(g => g.Select(n => n.Gist.Id))
                .ToHashSet();
            foreach (var (gist, isFolder, name) in named)
            {
                var clash = clashing.Contains(gist.Id);
                if (!isFolder)
                {
                    var shortcutName = clash ? Naming.WithShortId(name, gist.Id) : name;
                    entries.Add(new MirrorEntry(Path.Combine(top, Naming.ShortcutFileName(shortcutName)), gist.Id, gist.Files[0].Name, gist.HtmlUrl, gist.CreatedAt, gist.UpdatedAt));
                    continue;
                }

                var reserved = name.Equals(Folders.FailedName, StringComparison.OrdinalIgnoreCase);
                var folder = Path.Combine(top, clash || reserved ? Naming.WithFolderShortId(name, gist.Id) : name);
                entries.Add(new MirrorEntry(folder, gist.Id, null, gist.HtmlUrl, gist.CreatedAt, gist.UpdatedAt));
                entries.AddRange(ShortcutsIn(folder, gist));
            }
        }

        return entries;
    }

    /// <summary>
    /// GitHub's anchor for a file on a gist page: "file-" and the name in lower case, with each
    /// character other than a letter or digit replaced by "-".
    /// </summary>
    public static string Anchor(string fileName) =>
        "file-" + new string([.. fileName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

    public static string FileUrl(string htmlUrl, string fileName) => $"{htmlUrl}#{Anchor(fileName)}";

    private static IEnumerable<MirrorEntry> ShortcutsIn(string folder, Gist gist)
    {
        // File names are unique within a gist, but two can sanitize to the same name ("a:b" and "a_b").
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in gist.Files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var sanitized = Naming.Sanitize(file.Name);
            var name = sanitized;
            for (var n = 2; !used.Add(name); n++) name = Naming.WithMarker(sanitized, $" ({n})");
            yield return new MirrorEntry(Path.Combine(folder, Naming.ShortcutFileName(name)), gist.Id, file.Name, FileUrl(gist.HtmlUrl, file.Name), gist.CreatedAt, gist.UpdatedAt);
        }
    }
}
