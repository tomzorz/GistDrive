namespace GistDrive.Tests;

/// <summary>
/// Read-only checks against the owner's real GitHub account through the GitHub CLI, when
/// GISTDRIVE_LIVE_READONLY=1. They list and read gists and write nothing to GitHub; the mirror
/// goes to a temporary folder.
/// </summary>
[Collection("Live GitHub")]
public sealed class LiveReadOnlyTests
{
    public LiveReadOnlyTests() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable("GISTDRIVE_LIVE_READONLY") == "1", "Set GISTDRIVE_LIVE_READONLY=1 to read the real gist list (no writes to GitHub).");

    [Fact]
    public async Task The_real_gist_list_parses_and_a_gist_reads_back_with_content()
    {
        var api = new GhGistApi();
        var gists = await api.ListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(gists);
        Assert.All(gists, g =>
        {
            Assert.False(string.IsNullOrEmpty(g.Id));
            Assert.StartsWith("https://gist.github.com/", g.HtmlUrl);
            Assert.NotEmpty(g.Files);
        });

        var read = await api.GetAsync(gists[0].Id, TestContext.Current.CancellationToken);
        Assert.All(read.Files, f => Assert.True(f.Content is not null || f.Truncated));
    }

    [Fact]
    public async Task The_real_gists_mirror_into_a_temporary_folder()
    {
        using var temp = new TempFolder();
        var folders = new Folders(Path.Combine(temp.Path, "GistDrive"));
        var api = new GhGistApi();
        var gists = await api.ListAsync(TestContext.Current.CancellationToken);
        var config = new Config(folders.Root, 30, DeletionSetting.Off, 5);
        var held = HeldFiles.Load(Path.Combine(temp.Path, HeldFiles.FileName));
        await using var engine = new Engine(config, folders, EngineHarness.Fast, api, new FakeNotifier(), held, _ => Task.CompletedTask);
        engine.Start();

        var expected = gists.Sum(g => g.Files.Count);
        await Eventually.True(() => MirrorIndex.Scan(folders).Shortcuts.Count == expected, $"{expected} gist shortcuts appear", 30_000);
        Assert.Equal(gists.Count(g => g.Files.Count > 1), Directory.EnumerateDirectories(folders.Secret).Concat(Directory.EnumerateDirectories(folders.Public)).Count());
    }
}
