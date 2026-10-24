using System.Security.Cryptography;
using System.Threading.Channels;

namespace GistDrive;

/// <summary>
/// Durations that are fixed in production and short in tests.
/// </summary>
public sealed record Timings(
    TimeSpan Settle,
    TimeSpan FolderSettle,
    TimeSpan RefreshInterval,
    TimeSpan RefreshDebounce,
    TimeSpan DeletionBatch,
    TimeSpan CountdownTick,
    TimeSpan RetryDelay,
    TimeSpan OwnChangeQuiet)
{
    /// <summary>
    /// Settle per OPEN-8, refresh per REQ-MIR-2, and a deletion batch short enough that a
    /// deleted shortcut comes back within the 2 seconds of REQ-DEL-5.
    /// </summary>
    public static Timings Production { get; } = new(
        Settle: TimeSpan.FromSeconds(2),
        FolderSettle: TimeSpan.FromSeconds(4),
        RefreshInterval: TimeSpan.FromMinutes(5),
        RefreshDebounce: TimeSpan.FromSeconds(1),
        DeletionBatch: TimeSpan.FromSeconds(1),
        CountdownTick: TimeSpan.FromSeconds(1),
        RetryDelay: TimeSpan.FromSeconds(15),
        OwnChangeQuiet: TimeSpan.FromSeconds(2));
}

/// <summary>
/// The heart of GistDrive. It watches the root folder, keeps the mirror in step with GitHub,
/// and runs every upload, deletion, rename and menu request. One loop makes every change, fed
/// by the file watcher, timers, notification clicks and requests from the menu entries:
///
///   watcher ─┐
///   timers  ─┼─► channel ─► loop ─► IGistApi, disk, INotifier
///   clicks  ─┤
///   verbs   ─┘
/// </summary>
public sealed partial class Engine : IAsyncDisposable
{
    private const string OwnWrite = "write|";
    private const string OwnDelete = "delete|";
    private const string OwnMove = "move|";
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(50);

    private readonly Config _config;
    private readonly Folders _folders;
    private readonly Timings _timings;
    private readonly IGistApi _api;
    private readonly INotifier _notifier;
    private readonly CancelledUploads _cancelled;
    private readonly Func<string, Task> _open;
    private readonly Channel<Work> _work = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _background = [];
    private readonly Dictionary<string, DateTimeOffset> _expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _settling = new(StringComparer.OrdinalIgnoreCase);
    private MirrorIndex _index = new();
    private FileSystemWatcher? _watcher;
    private long _settleCounter;
    private bool _refreshQueued;

    /// <summary>
    /// Creates the engine. The open callback shows a file in its default app; Edit uses it.
    /// </summary>
    public Engine(Config config, Folders folders, Timings timings, IGistApi api, INotifier notifier, CancelledUploads cancelled, Func<string, Task> open)
    {
        _config = config;
        _folders = folders;
        _timings = timings;
        _api = api;
        _notifier = notifier;
        _cancelled = cancelled;
        _open = open;
        _notifier.CancelSelected += tag =>
        {
            if (Guid.TryParseExact(tag, "N", out var id)) Post(new CancelPending(id));
        };
    }

    /// <summary>
    /// Reads the mirror from disk, starts watching, and queues the first refresh and the files
    /// that arrived while GistDrive was not running.
    /// </summary>
    public void Start()
    {
        _folders.EnsureTopFolders();
        _index = MirrorIndex.Scan(_folders);
        _watcher = new FileSystemWatcher(_folders.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (_, e) => Post(new Changed(e.FullPath));
        _watcher.Changed += (_, e) => Post(new Changed(e.FullPath));
        _watcher.Deleted += (_, e) => Post(new Deleted(e.FullPath));
        _watcher.Renamed += (_, e) => Post(new Renamed(e.OldFullPath, e.FullPath));
        _watcher.Error += (_, e) => Post(new Overflow(e.GetException()));
        _watcher.EnableRaisingEvents = true;

        _background.Add(Task.Run(RunAsync));
        _background.Add(RepeatAsync(_timings.CountdownTick, () => new CountdownTick()));
        _background.Add(RepeatAsync(_timings.RefreshInterval, () => new RefreshDue()));
        Post(new Startup());
    }

    /// <summary>
    /// Queues a request from a menu entry, a Send To entry or the command line.
    /// </summary>
    public void Submit(VerbRequest request) => Post(new VerbWork(request));

    public async ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        await _stop.CancelAsync();
        _work.Writer.TryComplete();
        await Task.WhenAll(_background);
        _stop.Dispose();
    }

    private void Post(Work work) => _work.Writer.TryWrite(work);

    private void PostAfter(TimeSpan delay, Work work) => _ = PostLaterAsync(delay, work);

    private async Task PostLaterAsync(TimeSpan delay, Work work)
    {
        try
        {
            await Task.Delay(delay, _stop.Token);
            Post(work);
        }
        catch (OperationCanceledException)
        {
            // GistDrive is stopping.
        }
    }

    private async Task RepeatAsync(TimeSpan every, Func<Work> work)
    {
        using var timer = new PeriodicTimer(every);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token)) Post(work());
        }
        catch (OperationCanceledException)
        {
            // GistDrive is stopping.
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var work in _work.Reader.ReadAllAsync(_stop.Token))
            {
                try
                {
                    await HandleAsync(work);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log.Error($"{work} failed", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // GistDrive is stopping.
        }
    }

    private async Task HandleAsync(Work work)
    {
        switch (work)
        {
            case Startup: await StartupAsync(); break;
            case Changed changed: OnChanged(changed.Path); break;
            case Deleted deleted: OnDeleted(deleted.Path); break;
            case Renamed renamed: await OnRenamedAsync(renamed.OldPath, renamed.NewPath); break;
            case Overflow overflow: OnOverflow(overflow.Error); break;
            case Settled settled: OnSettled(settled); break;
            case DeletionBatchDue: OnDeletionBatch(); break;
            case PendingDue due: await OnPendingDueAsync(due.Id); break;
            case CancelPending cancel: OnCancel(cancel.Id); break;
            case RefreshDue: await RefreshAsync(); break;
            case CountdownTick: OnTick(); break;
            case VerbWork verb: await OnVerbAsync(verb.Request); break;
        }
    }

    private async Task StartupAsync()
    {
        await RefreshAsync();
        _cancelled.Prune();
        foreach (var top in _folders.TopFolders)
        {
            foreach (var file in Directory.EnumerateFiles(top)) OnChanged(file);
            foreach (var folder in Directory.EnumerateDirectories(top).Where(f => !Path.GetFileName(f).Equals(Folders.FailedName, StringComparison.OrdinalIgnoreCase)))
            {
                if (!IsGistFolder(folder))
                {
                    OnChanged(folder);
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder)) OnChanged(file);
            }
        }
    }

    private void RequestRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        PostAfter(_timings.RefreshDebounce, new RefreshDue());
    }

    private async Task RefreshAsync()
    {
        _refreshQueued = false;
        IReadOnlyList<Gist> gists;
        try
        {
            gists = await _api.ListAsync(_stop.Token);
        }
        catch (GistApiException ex)
        {
            Log.Info($"refresh skipped: {ex.Message}");
            return;
        }

        new Reconciler(_folders, _index, new ChangeAnnouncer(ExpectWrite, ExpectDelete, ExpectMove)).Apply(Layout.Plan(_folders, gists), IsHeld);
    }

    private void OnOverflow(Exception error)
    {
        // The watcher lost events: read everything again, as at startup.
        Log.Error("file watcher overflow", error);
        Post(new Startup());
    }

    private void OnTick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pending in _pending.Values.Where(p => p.Notified)) _notifier.UpdateCountdown(pending.Tag, Clamp(pending.DueAt - now), pending.Window);

        // Announcements that no event used up would otherwise pile up over a long run.
        foreach (var key in _expected.Where(e => e.Value <= now).Select(e => e.Key).ToList()) _expected.Remove(key);
    }

    // GistDrive announces each change it makes so that the watcher events the change causes are
    // ignored (OPEN-8), and only those: the owner deleting a shortcut a moment after GistDrive
    // wrote it is a real deletion. A write raises several events, so it stays expected for the
    // quiet period. A deletion or a move raises one event, which uses the expectation up.
    private void ExpectWrite(string path) => _expected[OwnWrite + path] = DateTimeOffset.UtcNow + _timings.OwnChangeQuiet;

    private void ExpectDelete(string path) => _expected[OwnDelete + path] = DateTimeOffset.UtcNow + _timings.OwnChangeQuiet;

    /// <summary>
    /// Expects a move as one rename event, or as a delete and a create, whichever the watcher reports.
    /// </summary>
    private void ExpectMove(string from, string to)
    {
        var until = DateTimeOffset.UtcNow + _timings.OwnChangeQuiet;
        _expected[OwnMove + from] = until;
        _expected[OwnMove + to] = until;
        _expected[OwnDelete + from] = until;
        _expected[OwnWrite + to] = until;
    }

    private bool IsOwnWrite(string path) => Matches(OwnWrite + path, useUp: false);

    private bool IsOwnDelete(string path) => Matches(OwnDelete + path, useUp: true);

    private bool IsOwnMove(string from, string to)
    {
        var fromMatches = Matches(OwnMove + from, useUp: true);
        var toMatches = Matches(OwnMove + to, useUp: true);
        return fromMatches || toMatches;
    }

    private bool Matches(string key, bool useUp)
    {
        if (!_expected.TryGetValue(key, out var until)) return false;

        var live = until > DateTimeOffset.UtcNow;
        if (useUp || !live) _expected.Remove(key);
        return live;
    }

    private void Settle(string path, TimeSpan delay)
    {
        var generation = ++_settleCounter;
        _settling[path] = generation;
        PostAfter(delay, new Settled(path, generation));
    }

    private void StopSettling(string path) => _settling.Remove(path);

    private bool IsGistFolder(string path) => _index.GistIdOfFolder(path) is not null;

    private static TimeSpan Clamp(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;

    private static bool SamePath(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string folder) => path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string DisplayName(string path) => Naming.IsShortcut(path) ? Naming.ShortcutName(path) : Path.GetFileName(path);

    /// <summary>
    /// True when no other process has the file open, so a save has finished (OPEN-8).
    /// </summary>
    private static bool IsFree(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;

        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, Naming.WithMarker(name, $" ({n})"));
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private abstract record Work;

    private sealed record Startup : Work;

    private sealed record Changed(string Path) : Work;

    private sealed record Deleted(string Path) : Work;

    private sealed record Renamed(string OldPath, string NewPath) : Work;

    private sealed record Overflow(Exception Error) : Work;

    private sealed record Settled(string Path, long Generation) : Work;

    private sealed record DeletionBatchDue : Work;

    private sealed record PendingDue(Guid Id) : Work;

    private sealed record CancelPending(Guid Id) : Work;

    private sealed record RefreshDue : Work;

    private sealed record CountdownTick : Work;

    private sealed record VerbWork(VerbRequest Request) : Work;
}
