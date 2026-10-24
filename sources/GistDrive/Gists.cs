namespace GistDrive;

/// <summary>
/// One file of a gist. Content is null in gist lists, which carry names and sizes only.
/// Truncated is true when GitHub returned only the first part of a large file.
/// </summary>
public sealed record GistFile(string Name, long Size, string? Content, bool Truncated);

/// <summary>
/// A gist in the owner's GitHub account. GitHub keeps one creation time and one update time per
/// gist, not per file.
/// </summary>
public sealed record Gist(string Id, bool IsPublic, string Description, string HtmlUrl, IReadOnlyList<GistFile> Files, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public Visibility Visibility => IsPublic ? Visibility.Public : Visibility.Secret;

    public GistFile? File(string name) => Files.FirstOrDefault(f => f.Name == name);
}

/// <summary>
/// A change to one gist file: new content, a new name, or both.
/// </summary>
public sealed record FileChange(string? Content = null, string? NewName = null);

/// <summary>
/// An update to a gist. A file mapped to null is removed from the gist.
/// </summary>
public sealed record GistUpdate(string? Description, IReadOnlyDictionary<string, FileChange?> Files)
{
    public static GistUpdate Content(string fileName, string content) => new(null, new Dictionary<string, FileChange?> { [fileName] = new(content) });

    public static GistUpdate Rename(string fileName, string newName) => new(null, new Dictionary<string, FileChange?> { [fileName] = new(NewName: newName) });

    public static GistUpdate Remove(string fileName) => new(null, new Dictionary<string, FileChange?> { [fileName] = null });

    public static GistUpdate NewDescription(string description) => new(description, new Dictionary<string, FileChange?>());
}

/// <summary>
/// Why a call to GitHub failed. Unavailable and RateLimited clear on their own; the rest need the owner.
/// </summary>
public enum GistErrorKind
{
    Unavailable,
    RateLimited,
    CliMissing,
    NotSignedIn,
    NotFound,
    Rejected,
}

/// <summary>
/// A failed call to GitHub, with the kind of failure and the message the GitHub CLI printed.
/// </summary>
public sealed class GistApiException(GistErrorKind kind, string message) : Exception(message)
{
    public GistErrorKind Kind { get; } = kind;

    public bool IsTransient => Kind is GistErrorKind.Unavailable or GistErrorKind.RateLimited;

    /// <summary>
    /// The failure in words for a reason file or a notification.
    /// </summary>
    public string Explanation => Kind switch
    {
        GistErrorKind.CliMissing => "the GitHub CLI (gh) is not installed or not on PATH",
        GistErrorKind.NotSignedIn => "the GitHub CLI is not signed in (run gh auth login)",
        GistErrorKind.NotFound => "the gist no longer exists on GitHub",
        GistErrorKind.RateLimited => "GitHub's rate limit stopped the call",
        GistErrorKind.Unavailable => $"GitHub could not be reached ({Message})",
        _ => $"GitHub rejected the call ({Message})",
    };
}

/// <summary>
/// The gist calls GistDrive makes. GhGistApi talks to GitHub; the tests run the same contract
/// suite against an in-memory fake.
/// </summary>
public interface IGistApi
{
    /// <summary>
    /// Lists every gist of the owner with file names and sizes, without content.
    /// </summary>
    Task<IReadOnlyList<Gist>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one gist with the content of its files.
    /// </summary>
    Task<Gist> GetAsync(string id, CancellationToken cancellationToken);

    Task<Gist> CreateAsync(bool isPublic, string description, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken);

    Task<Gist> UpdateAsync(string id, GistUpdate update, CancellationToken cancellationToken);

    Task DeleteAsync(string id, CancellationToken cancellationToken);
}
