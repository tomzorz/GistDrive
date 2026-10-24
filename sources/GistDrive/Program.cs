using System.Diagnostics;

namespace GistDrive;

/// <summary>
/// GistDrive puts the owner's GitHub gists in File Explorer. Without arguments it runs in the
/// background. With a verb it either does one job (install, uninstall) or hands a request from
/// a menu entry or a Send To entry to the background run, starting that run if needed.
/// </summary>
public static class Program
{
    public const string AppId = "GistDrive";

    private const string MutexName = @"Local\GistDrive";

    private static readonly string[] ForwardedVerbs = ["edit", "delete", "make-public", "send-secret", "send-public", "refresh"];

    public static async Task<int> Main(string[] args)
    {
        var home = AppContext.BaseDirectory;
        Log.Open(home);
        var verb = args.Length > 0 ? args[0].ToLowerInvariant() : "run";
        try
        {
            return verb switch
            {
                "run" => await RunAsync(home),
                "install" => await InstallAsync(home),
                "uninstall" => await UninstallAsync(home),
                "quit" => await QuitAsync(),
                _ when ForwardedVerbs.Contains(verb) => await ForwardAsync(new VerbRequest(verb, [.. args.Skip(1).Select(Path.GetFullPath)])),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"{verb} failed", ex);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string home)
    {
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance) return 0;

        var config = Config.LoadOrCreate(home);
        var folders = new Folders(config.ResolvedRootFolder);
        var exe = Environment.ProcessPath!;
        var target = InstallTarget.ForCurrentUser();
        if (Installer.IsStale(target, folders, exe))
        {
            Installer.Install(target, folders, exe);
            Log.Info($"integration updated for {folders.Root}");
        }

        // Puts back a desktop.ini that went missing; does nothing when all three are in place.
        folders.EnsureTopFolders();
        FolderIcons.Apply(folders, FolderIcons.Beside(exe));
        var notifier = new WindowsNotifier(AppId, Installer.DisplayName, Installer.NotificationImage(exe));
        var cancelled = CancelledUploads.Load(Path.Combine(home, CancelledUploads.FileName));
        await using var engine = new Engine(config, folders, Timings.Production, new GhGistApi(), notifier, cancelled, OpenWithDefaultApp);
        engine.Start();
        Log.Info($"running on {folders.Root}");

        using var stop = new CancellationTokenSource();
        await Ipc.ServeAsync(Ipc.PipeName, request =>
        {
            if (request.Verb == "quit") stop.Cancel();
            else engine.Submit(request);
            return Task.CompletedTask;
        }, stop.Token);
        Log.Info("stopped");
        return 0;
    }

    private static async Task<int> InstallAsync(string home)
    {
        Native.AttachToParentConsole();

        // Installing over a running copy, as an update does: the old run stops, so the build
        // that runs afterwards is this one.
        await StopBackgroundRunAsync();
        var config = Config.LoadOrCreate(home);
        var folders = new Folders(config.ResolvedRootFolder);
        var exe = Environment.ProcessPath!;
        Installer.Install(InstallTarget.ForCurrentUser(), folders, exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        Console.WriteLine($"GistDrive is installed and running. Your gists appear in {folders.Root}.");
        return 0;
    }

    private static async Task<int> UninstallAsync(string home)
    {
        Native.AttachToParentConsole();
        await StopBackgroundRunAsync();
        Installer.Uninstall(InstallTarget.ForCurrentUser(), new Folders(Config.LoadOrCreate(home).ResolvedRootFolder));
        Console.WriteLine("GistDrive is uninstalled. The GistDrive folder and its files are still there.");
        return 0;
    }

    private static async Task<int> QuitAsync()
    {
        Native.AttachToParentConsole();
        Console.WriteLine(await StopBackgroundRunAsync() ? "GistDrive stopped." : "GistDrive was not running.");
        return 0;
    }

    /// <summary>
    /// Asks the background run to stop and waits up to 10 seconds for it to exit, which frees
    /// the exe for an update. False when no background run answered.
    /// </summary>
    private static async Task<bool> StopBackgroundRunAsync()
    {
        if (!await Ipc.SendAsync(Ipc.PipeName, new VerbRequest("quit", []), TimeSpan.FromSeconds(2))) return false;

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && IsRunning()) await Task.Delay(100);
        return true;
    }

    // The mutex lives as long as a handle to it does, and only the background run keeps one.
    private static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var mutex)) return false;
        mutex.Dispose();
        return true;
    }

    /// <summary>
    /// Hands a request to the background run. When none answers, starts one and tries again
    /// for up to 10 seconds.
    /// </summary>
    private static async Task<int> ForwardAsync(VerbRequest request)
    {
        if (await Ipc.SendAsync(Ipc.PipeName, request, TimeSpan.FromSeconds(1))) return 0;

        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false });
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(500);
            if (await Ipc.SendAsync(Ipc.PipeName, request, TimeSpan.FromSeconds(1))) return 0;
        }

        Log.Info($"{request.Verb}: no background run answered, request dropped");
        return 1;
    }

    private static Task OpenWithDefaultApp(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static int Usage()
    {
        Native.AttachToParentConsole();
        Console.WriteLine("""
            GistDrive puts your GitHub gists in File Explorer.

              GistDrive                 run in the background (the Startup shortcut does this)
              GistDrive install         add the sidebar entry, menu entries, Send To entries and Startup shortcut
              GistDrive uninstall       remove them again; your files stay
              GistDrive quit            stop the background run, for example before an update
              GistDrive refresh         read the gist list from GitHub now
            """);
        return 2;
    }
}
