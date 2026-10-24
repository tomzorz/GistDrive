using System.Collections.Concurrent;

namespace GistDrive.Tests;

/// <summary>
/// An engine on a temporary root folder with the fake GitHub and the fake notification
/// center, running on short timings. Everything else is real: the disk, the file watcher, the
/// timers.
/// </summary>
public sealed class EngineHarness : IAsyncDisposable
{
    public static readonly Timings Fast = new(
        Settle: TimeSpan.FromMilliseconds(150),
        FolderSettle: TimeSpan.FromMilliseconds(300),
        RefreshInterval: TimeSpan.FromHours(1),
        RefreshDebounce: TimeSpan.FromMilliseconds(50),
        DeletionBatch: TimeSpan.FromMilliseconds(150),
        CountdownTick: TimeSpan.FromMilliseconds(100),
        RetryDelay: TimeSpan.FromMilliseconds(100),
        OwnChangeQuiet: TimeSpan.FromMilliseconds(600));

    private readonly TempFolder _temp = new();
    private readonly ConcurrentQueue<string> _opened = new();
    private readonly Config _config;

    public EngineHarness(double undoSeconds = 0, DeletionSetting deletion = DeletionSetting.Off, double retrySeconds = 0.5)
    {
        Folders = new Folders(Path.Combine(_temp.Path, "GistDrive"));
        Outside = Path.Combine(_temp.Path, "outside");
        Directory.CreateDirectory(Outside);
        _config = new Config(Folders.Root, undoSeconds, deletion, retrySeconds / 60);
        Engine = NewEngine();
    }

    public FakeGistApi Api { get; } = new();

    public FakeNotifier Notifier { get; } = new();

    public Folders Folders { get; }

    public Engine Engine { get; private set; }

    /// <summary>
    /// The record of held files, next to the root folder as held.json sits next to the exe.
    /// </summary>
    public string HeldRecord => Path.Combine(_temp.Path, HeldFiles.FileName);

    /// <summary>
    /// A folder next to the root folder, for originals of Send To and for moving things out.
    /// </summary>
    public string Outside { get; }

    public IEnumerable<string> Opened => _opened;

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public string Secret(string name) => Path.Combine(Folders.Secret, name);

    public string Public(string name) => Path.Combine(Folders.Public, name);

    public Gist? OnlyGist => Api.All.Count == 1 ? Api.All[0] : null;

    public Task<Gist> SeedAsync(bool isPublic, string description, params (string Name, string Content)[] files) =>
        Api.CreateAsync(isPublic, description, files.ToDictionary(f => f.Name, f => f.Content), Token);

    public void Start() => Engine.Start();

    /// <summary>
    /// Stops the engine and starts a new one on the same folders, record and GitHub, as a
    /// reboot or an update does. Nothing carries over in memory.
    /// </summary>
    public async Task RestartAsync()
    {
        await Engine.DisposeAsync();
        Engine = NewEngine();
        Engine.Start();
    }

    public void Submit(string verb, params string[] paths) => Engine.Submit(new VerbRequest(verb, paths));

    public static GistShortcut? Shortcut(string path) => File.Exists(path) ? GistShortcut.TryRead(path) : null;

    public async ValueTask DisposeAsync()
    {
        await Engine.DisposeAsync();
        _temp.Dispose();
    }

    private Engine NewEngine() => new(_config, Folders, Fast, Api, Notifier, HeldFiles.Load(HeldRecord), path =>
    {
        _opened.Enqueue(path);
        return Task.CompletedTask;
    });
}
