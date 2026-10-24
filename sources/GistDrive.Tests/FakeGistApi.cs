using System.Text;

namespace GistDrive.Tests;

/// <summary>
/// An in-memory gist service. GistApiContract runs against it and, when enabled, against GitHub
/// itself, so its behaviour stays honest. Offline and SignedOut model the two states the engine
/// has to survive.
/// </summary>
public sealed class FakeGistApi : IGistApi
{
    private readonly Lock _gate = new();
    private readonly List<StoredGist> _gists = [];

    public bool Offline { get; set; }

    public bool SignedOut { get; set; }

    /// <summary>
    /// Every gist with content, newest first: what the GitHub website would show.
    /// </summary>
    public IReadOnlyList<Gist> All
    {
        get
        {
            lock (_gate) return [.. _gists.Select(g => g.Snapshot(withContent: true))];
        }
    }

    public Task<IReadOnlyList<Gist>> ListAsync(CancellationToken cancellationToken)
    {
        Check();
        lock (_gate) return Task.FromResult<IReadOnlyList<Gist>>([.. _gists.Select(g => g.Snapshot(withContent: false))]);
    }

    public Task<Gist> GetAsync(string id, CancellationToken cancellationToken)
    {
        Check();
        lock (_gate) return Task.FromResult(Find(id).Snapshot(withContent: true));
    }

    public Task<Gist> CreateAsync(bool isPublic, string description, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken)
    {
        Check();
        if (files.Count == 0) throw Rejected("Validation Failed: files are missing");
        foreach (var (name, content) in files) Validate(name, content);

        lock (_gate)
        {
            var gist = new StoredGist(Guid.NewGuid().ToString("N"), isPublic, description, new Dictionary<string, string>(files));
            _gists.Insert(0, gist);
            return Task.FromResult(gist.Snapshot(withContent: true));
        }
    }

    public Task<Gist> UpdateAsync(string id, GistUpdate update, CancellationToken cancellationToken)
    {
        Check();
        lock (_gate)
        {
            var gist = Find(id);
            var files = new Dictionary<string, string>(gist.Files);
            foreach (var (name, change) in update.Files)
            {
                if (change is null)
                {
                    files.Remove(name);
                    continue;
                }

                var content = change.Content ?? (files.TryGetValue(name, out var existing) ? existing : throw Rejected($"Validation Failed: {name} does not exist"));
                var newName = change.NewName ?? name;
                if (newName != name && files.ContainsKey(newName)) throw Rejected($"Validation Failed: {newName} already exists");
                Validate(newName, content);
                files.Remove(name);
                files[newName] = content;
            }

            if (files.Count == 0) throw Rejected("Validation Failed: a gist needs at least one file");
            gist.Files = files;
            if (update.Description is not null) gist.Description = update.Description;
            gist.UpdatedAt = Now();
            return Task.FromResult(gist.Snapshot(withContent: true));
        }
    }

    /// <summary>
    /// Sets a gist's times, as if GitHub had created and last updated it then.
    /// </summary>
    public void Backdate(string id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        lock (_gate)
        {
            var gist = Find(id);
            gist.CreatedAt = createdAt;
            gist.UpdatedAt = updatedAt;
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        Check();
        lock (_gate) _gists.Remove(Find(id));
        return Task.CompletedTask;
    }

    private void Check()
    {
        if (Offline) throw new GistApiException(GistErrorKind.Unavailable, "error connecting to api.github.com");
        if (SignedOut) throw new GistApiException(GistErrorKind.NotSignedIn, "To get started with GitHub CLI, please run:  gh auth login");
    }

    private StoredGist Find(string id) =>
        _gists.FirstOrDefault(g => g.Id == id) ?? throw new GistApiException(GistErrorKind.NotFound, "gh: Not Found (HTTP 404)");

    private static void Validate(string name, string content)
    {
        if (string.IsNullOrEmpty(name) || name.Contains('/')) throw Rejected($"Validation Failed: bad file name {name}");
        if (content.Length == 0) throw Rejected("Validation Failed: contents can't be blank");
    }

    private static GistApiException Rejected(string message) => new(GistErrorKind.Rejected, $"gh: {message} (HTTP 422)");

    // GitHub keeps gist times to the second.
    private static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private sealed class StoredGist(string id, bool isPublic, string description, Dictionary<string, string> files)
    {
        public string Id { get; } = id;

        public string Description { get; set; } = description;

        public Dictionary<string, string> Files { get; set; } = files;

        public DateTimeOffset CreatedAt { get; set; } = Now();

        public DateTimeOffset UpdatedAt { get; set; } = Now();

        public Gist Snapshot(bool withContent) => new(
            Id,
            isPublic,
            Description,
            $"https://gist.github.com/{Id}",
            [.. Files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => File(f.Key, f.Value, withContent))],
            CreatedAt,
            UpdatedAt);

        private static GistFile File(string name, string content, bool withContent)
        {
            var size = Encoding.UTF8.GetByteCount(content);
            var truncated = withContent && size > TextFile.MaxBytes;
            return new GistFile(name, size, withContent ? (truncated ? content[..Math.Min(content.Length, TextFile.MaxBytes)] : content) : null, truncated);
        }
    }
}
