namespace GistDrive;

/// <summary>
/// A gist's visibility, which is also the top folder that holds it.
/// </summary>
public enum Visibility
{
    Secret,
    Public,
}

/// <summary>
/// Where a path sits under the root folder.
/// </summary>
public enum Place
{
    /// <summary>
    /// Not inside a top folder: the root folder itself, a file directly in it, or anywhere else.
    /// </summary>
    Outside,

    /// <summary>
    /// A top folder itself.
    /// </summary>
    TopFolder,

    /// <summary>
    /// A failed folder or anything inside it.
    /// </summary>
    Failed,

    /// <summary>
    /// Directly in a top folder: a gist shortcut, a gist folder, or something the owner added.
    /// </summary>
    InTop,

    /// <summary>
    /// Directly in a folder that sits in a top folder.
    /// </summary>
    InSubfolder,

    /// <summary>
    /// Deeper than InSubfolder.
    /// </summary>
    Deeper,
}

/// <summary>
/// Where a path sits: the place, the visibility of its top folder, and for InSubfolder and
/// Deeper the folder directly in the top folder that contains the path.
/// </summary>
public readonly record struct Location(Place Place, Visibility Visibility, string? Subfolder);

/// <summary>
/// The fixed layout under the root folder: Public and Secret, each with a _failed folder.
/// </summary>
public sealed class Folders
{
    public const string FailedName = "_failed";

    public Folders(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Public = Path.Combine(Root, "Public");
        Secret = Path.Combine(Root, "Secret");
    }

    public string Root { get; }

    public string Public { get; }

    public string Secret { get; }

    public IEnumerable<string> TopFolders => [Public, Secret];

    public string Top(Visibility visibility) => visibility == Visibility.Public ? Public : Secret;

    public string Failed(Visibility visibility) => Path.Combine(Top(visibility), FailedName);

    public void EnsureTopFolders()
    {
        Directory.CreateDirectory(Public);
        Directory.CreateDirectory(Secret);
    }

    public Location Locate(string path)
    {
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(path));
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return new(Place.Outside, default, null);

        var parts = relative.Split(Path.DirectorySeparatorChar);
        Visibility? visibility = parts[0].Equals("Public", StringComparison.OrdinalIgnoreCase) ? Visibility.Public
            : parts[0].Equals("Secret", StringComparison.OrdinalIgnoreCase) ? Visibility.Secret
            : null;
        if (visibility is not { } top) return new(Place.Outside, default, null);
        if (parts.Length == 1) return new(Place.TopFolder, top, null);
        if (parts[1].Equals(FailedName, StringComparison.OrdinalIgnoreCase)) return new(Place.Failed, top, null);
        if (parts.Length == 2) return new(Place.InTop, top, null);

        var subfolder = Path.Combine(Top(top), parts[1]);
        return new(parts.Length == 3 ? Place.InSubfolder : Place.Deeper, top, subfolder);
    }
}
