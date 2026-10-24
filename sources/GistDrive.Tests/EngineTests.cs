namespace GistDrive.Tests;

public sealed class UploadTests
{
    [Fact]
    public async Task A_file_saved_into_secret_becomes_a_secret_gist_and_a_shortcut()
    {
        await using var h = new EngineHarness();
        h.Start();
        await File.WriteAllTextAsync(h.Secret("notes.md"), "hello", EngineHarness.Token);

        await Eventually.True(() => h.OnlyGist is { IsPublic: false } gist && gist.File("notes.md")?.Content == "hello", "a secret gist holds notes.md");
        await Eventually.True(() => EngineHarness.Shortcut(h.Secret("notes.md.url"))?.GistId == h.OnlyGist!.Id && !File.Exists(h.Secret("notes.md")), "the shortcut replaces the file");
    }

    [Fact]
    public async Task A_public_file_waits_for_the_undo_window_with_a_countdown()
    {
        await using var h = new EngineHarness(undoSeconds: 1);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "hello", EngineHarness.Token);

        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        Assert.Empty(h.Api.All);
        Assert.Equal("Publishing notes.md", h.Notifier.Countdowns[0].Title);
        await Eventually.True(() => h.OnlyGist is { IsPublic: true }, "the public gist exists");
        await Eventually.True(() => h.Notifier.Countdowns.Count == 0, "the countdown goes");
    }

    [Fact]
    public async Task The_countdown_counts_down()
    {
        await using var h = new EngineHarness(undoSeconds: 3);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "hello", EngineHarness.Token);

        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        Assert.True(h.Notifier.Countdowns[0].Remaining > TimeSpan.FromSeconds(2.5));
        await Eventually.True(() => h.Notifier.Countdowns is [{ Remaining: var left }] && left < TimeSpan.FromSeconds(2), "the remaining time goes down");
    }

    [Fact]
    public async Task Cancel_keeps_the_file_until_the_next_save()
    {
        await using var h = new EngineHarness(undoSeconds: 1.5);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "hello", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");

        h.Notifier.SelectCancel(h.Notifier.Countdowns[0].Tag);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 0, "the countdown goes");
        await Eventually.Stays(() => h.Api.All.Count == 0 && File.Exists(h.Public("notes.md")), "nothing uploaded and the file stays", 2_000);

        await File.WriteAllTextAsync(h.Public("notes.md"), "hello again", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a new save brings a new countdown");
    }

    [Fact]
    public async Task A_cancel_outlives_a_restart()
    {
        await using var h = new EngineHarness(undoSeconds: 1.5);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "hello", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        h.Notifier.SelectCancel(h.Notifier.Countdowns[0].Tag);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 0, "the countdown goes");

        await h.RestartAsync();
        await Eventually.Stays(() => h.Notifier.Countdowns.Count == 0 && h.Api.All.Count == 0, "the startup scan leaves the cancelled file alone", 2_000);

        await File.WriteAllTextAsync(h.Public("notes.md"), "hello again", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a new save brings a new countdown");
    }

    [Fact]
    public async Task A_rename_is_not_a_save()
    {
        await using var h = new EngineHarness(undoSeconds: 1.5);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "hello", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        h.Notifier.SelectCancel(h.Notifier.Countdowns[0].Tag);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 0, "the countdown goes");

        File.Move(h.Public("notes.md"), h.Public("ideas.md"));
        await Eventually.Stays(() => h.Notifier.Countdowns.Count == 0 && h.Api.All.Count == 0, "the renamed file stays cancelled", 1_500);
        await h.RestartAsync();
        await Eventually.Stays(() => h.Notifier.Countdowns.Count == 0 && h.Api.All.Count == 0, "and stays cancelled after a restart", 1_500);
    }

    [Fact]
    public async Task Repeated_saves_upload_the_latest_content_once()
    {
        await using var h = new EngineHarness(undoSeconds: 1);
        h.Start();
        await File.WriteAllTextAsync(h.Public("notes.md"), "v1", EngineHarness.Token);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        await File.WriteAllTextAsync(h.Public("notes.md"), "v2", EngineHarness.Token);

        await Eventually.True(() => h.OnlyGist?.File("notes.md")?.Content == "v2", "one gist with the latest content");
        await Eventually.Stays(() => h.Api.All.Count == 1, "no second gist", 1_000);
    }

    [Fact]
    public async Task Saving_next_to_a_shortcut_updates_its_gist()
    {
        await using var h = new EngineHarness();
        var gist = await h.SeedAsync(false, string.Empty, ("notes.md", "v1"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the mirror shows the gist");

        await File.WriteAllTextAsync(h.Secret("notes.md"), "v2", EngineHarness.Token);
        await Eventually.True(() => h.Api.All.Single().File("notes.md")?.Content == "v2" && !File.Exists(h.Secret("notes.md")), "the gist has v2 and the file is gone");
        Assert.Equal(gist.Id, h.Api.All.Single().Id);
    }

    [Fact]
    public async Task A_folder_becomes_one_gist_described_by_its_name()
    {
        await using var h = new EngineHarness();
        h.Start();
        var folder = h.Secret("Project");
        Directory.CreateDirectory(Path.Combine(h.Outside, "Project"));
        await File.WriteAllTextAsync(Path.Combine(h.Outside, "Project", "a.md"), "a", EngineHarness.Token);
        await File.WriteAllTextAsync(Path.Combine(h.Outside, "Project", "b.md"), "b", EngineHarness.Token);
        Directory.Move(Path.Combine(h.Outside, "Project"), folder);

        await Eventually.True(() => h.OnlyGist is { Description: "Project", Files.Count: 2 }, "one gist with both files");
        await Eventually.True(() => File.Exists(Path.Combine(folder, "a.md.url")) && !File.Exists(Path.Combine(folder, "a.md")), "the folder holds shortcuts");
    }

    [Fact]
    public async Task A_file_saved_into_a_gist_folder_joins_the_gist()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, "Notes", ("a.md", "a"), ("b.md", "b"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret(@"Notes\a.md.url")), "the gist folder appears");

        await File.WriteAllTextAsync(h.Secret(@"Notes\c.md"), "c", EngineHarness.Token);
        await Eventually.True(() => h.Api.All.Single().Files.Count == 3 && File.Exists(h.Secret(@"Notes\c.md.url")), "the gist has three files");
    }

    [Fact]
    public async Task Scratch_files_are_left_alone()
    {
        await using var h = new EngineHarness();
        h.Start();
        await File.WriteAllTextAsync(h.Secret("~$notes.md"), "lock", EngineHarness.Token);
        await File.WriteAllTextAsync(h.Secret("notes.md.tmp"), "temp", EngineHarness.Token);

        await Eventually.Stays(() => h.Api.All.Count == 0, "nothing uploaded", 1_000);
    }
}

public sealed class MirrorTests
{
    [Fact]
    public async Task Existing_gists_appear_as_shortcuts_and_gist_folders()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, string.Empty, ("secret.md", "s"));
        await h.SeedAsync(true, string.Empty, ("public.md", "p"));
        await h.SeedAsync(false, "My notes", ("x.md", "x"), ("y.md", "y"));
        h.Start();

        await Eventually.True(() => File.Exists(h.Secret("secret.md.url")) && File.Exists(h.Public("public.md.url")), "single-file gists are shortcuts");
        await Eventually.True(() => File.Exists(h.Secret(@"My notes\x.md.url")) && File.Exists(h.Secret(@"My notes\y.md.url")), "the multi-file gist is a folder");
    }

    [Fact]
    public async Task Shortcuts_and_gist_folders_carry_the_gist_times()
    {
        await using var h = new EngineHarness();
        var created = new DateTimeOffset(2024, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var updated = new DateTimeOffset(2025, 6, 2, 11, 30, 0, TimeSpan.Zero);
        var single = await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        var multi = await h.SeedAsync(false, "Notes", ("a.md", "a"), ("b.md", "b"));
        h.Api.Backdate(single.Id, created, updated);
        h.Api.Backdate(multi.Id, created, updated);
        h.Start();

        bool Stamped(FileSystemInfo item) => item.Exists && item.CreationTimeUtc == created.UtcDateTime && item.LastWriteTimeUtc == updated.UtcDateTime;
        await Eventually.True(() => Stamped(new FileInfo(h.Secret("notes.md.url"))), "the shortcut has the gist's times");
        await Eventually.True(() => Stamped(new FileInfo(h.Secret(@"Notes\a.md.url"))) && Stamped(new DirectoryInfo(h.Secret("Notes"))), "the gist folder and its shortcuts have the gist's times");

        await File.WriteAllTextAsync(h.Secret("notes.md"), "changed", EngineHarness.Token);
        await Eventually.True(() => new FileInfo(h.Secret("notes.md.url")) is var f && f.LastWriteTimeUtc > updated.UtcDateTime.AddDays(1) && f.CreationTimeUtc == created.UtcDateTime, "an update moves the modification time and keeps the creation time");
    }

    [Fact]
    public async Task Clashing_names_carry_the_short_gist_id()
    {
        await using var h = new EngineHarness();
        var first = await h.SeedAsync(false, string.Empty, ("gistfile1.txt", "1"));
        var second = await h.SeedAsync(false, string.Empty, ("gistfile1.txt", "2"));
        h.Start();

        await Eventually.True(
            () => File.Exists(h.Secret($"gistfile1 ({first.Id[..7]}).txt.url")) && File.Exists(h.Secret($"gistfile1 ({second.Id[..7]}).txt.url")),
            "both shortcuts carry their short ids");
    }

    [Fact]
    public async Task A_gist_deleted_on_github_loses_its_shortcut_at_the_next_refresh()
    {
        await using var h = new EngineHarness();
        var gist = await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        await h.Api.DeleteAsync(gist.Id, EngineHarness.Token);
        h.Submit("refresh");
        await Eventually.True(() => !File.Exists(h.Secret("notes.md.url")), "the shortcut goes");
    }

    [Fact]
    public async Task Renaming_a_shortcut_renames_the_gist_file()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        File.Move(h.Secret("notes.md.url"), h.Secret("ideas.md.url"));
        await Eventually.True(() => h.Api.All.Single().File("ideas.md")?.Content == "n", "the gist file is renamed");
        await Eventually.True(() => EngineHarness.Shortcut(h.Secret("ideas.md.url"))?.FileName == "ideas.md", "the shortcut points at the new name");
    }

    [Fact]
    public async Task Renaming_a_gist_folder_changes_the_description()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, "Old", ("a.md", "a"), ("b.md", "b"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret(@"Old\a.md.url")), "the gist folder appears");

        Directory.Move(h.Secret("Old"), h.Secret("New"));
        await Eventually.True(() => h.Api.All.Single().Description == "New", "the description follows the folder name");
    }

    [Fact]
    public async Task A_shortcut_moved_to_the_other_top_folder_goes_back()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        File.Move(h.Secret("notes.md.url"), h.Public("notes.md.url"));
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")) && !File.Exists(h.Public("notes.md.url")), "the shortcut goes back");
        Assert.Contains(h.Notifier.Messages, m => m.Body.Contains("Make public", StringComparison.Ordinal));
        Assert.Single(h.Api.All);
    }

    [Fact]
    public async Task A_gist_folder_moved_to_the_other_top_folder_goes_back_and_is_not_uploaded()
    {
        await using var h = new EngineHarness(deletion: DeletionSetting.On);
        await h.SeedAsync(false, "Notes", ("a.md", "a"), ("b.md", "b"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret(@"Notes\a.md.url")), "the gist folder appears");

        Directory.Move(h.Secret("Notes"), h.Public("Notes"));
        await Eventually.True(() => File.Exists(h.Secret(@"Notes\a.md.url")) && !Directory.Exists(h.Public("Notes")), "the gist folder goes back");
        await Eventually.Stays(() => h.Api.All.Count == 1 && h.Api.All[0].Files.Count == 2, "no gist deleted, none uploaded", 1_000);
    }
}

public sealed class DeletionTests
{
    [Fact]
    public async Task With_deletion_off_a_deleted_shortcut_comes_back_with_a_notification()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        File.Delete(h.Secret("notes.md.url"));
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut comes back", timeoutMilliseconds: 2_000);
        Assert.Contains(h.Notifier.Messages, m => m.Title == "Gist deletion is off");
        Assert.Single(h.Api.All);
    }

    [Fact]
    public async Task With_deletion_on_a_deleted_shortcut_deletes_the_gist_after_the_window()
    {
        await using var h = new EngineHarness(undoSeconds: 0.5, deletion: DeletionSetting.On);
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        File.Delete(h.Secret("notes.md.url"));
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        await Eventually.True(() => h.Api.All.Count == 0, "the gist is deleted");
        await Eventually.Stays(() => !File.Exists(h.Secret("notes.md.url")), "the shortcut stays gone", 500);
    }

    [Fact]
    public async Task Restoring_a_deleted_shortcut_stops_the_deletion()
    {
        await using var h = new EngineHarness(undoSeconds: 2, deletion: DeletionSetting.On);
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        // Moving out and back in looks to the watcher like a delete and a restore from the Recycle Bin.
        var parked = Path.Combine(h.Outside, "notes.md.url");
        File.Move(h.Secret("notes.md.url"), parked);
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1, "a countdown shows");
        File.Move(parked, h.Secret("notes.md.url"));

        await Eventually.True(() => h.Notifier.Countdowns.Count == 0, "the countdown goes");
        await Eventually.Stays(() => h.Api.All.Count == 1, "the gist stays", 2_500);
    }

    [Fact]
    public async Task Removing_a_shortcut_from_a_gist_folder_removes_that_file()
    {
        await using var h = new EngineHarness(deletion: DeletionSetting.On);
        await h.SeedAsync(false, "Notes", ("a.md", "a"), ("b.md", "b"), ("c.md", "c"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret(@"Notes\a.md.url")), "the gist folder appears");

        File.Delete(h.Secret(@"Notes\a.md.url"));
        await Eventually.True(() => h.Api.All.Single().Files.Select(f => f.Name).SequenceEqual(["b.md", "c.md"]), "a.md is gone from the gist");
    }

    [Fact]
    public async Task Delete_gist_deletes_whatever_the_setting()
    {
        await using var h = new EngineHarness(undoSeconds: 0.5);
        await h.SeedAsync(false, string.Empty, ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        h.Submit("delete", h.Secret("notes.md.url"));
        await Eventually.True(() => h.Notifier.Countdowns.Count == 1 && File.Exists(h.Secret("notes.md.url")), "a countdown shows and the shortcut stays meanwhile");
        await Eventually.True(() => h.Api.All.Count == 0 && !File.Exists(h.Secret("notes.md.url")), "the gist and its shortcut are gone");
    }
}

public sealed class FailureTests
{
    [Fact]
    public async Task A_binary_file_goes_to_the_failed_folder_with_a_reason()
    {
        await using var h = new EngineHarness();
        h.Start();
        await File.WriteAllBytesAsync(h.Secret("image.bin"), [0x89, 0x50, 0x00, 0x47], EngineHarness.Token);

        await Eventually.True(() => File.Exists(h.Secret(@"_failed\image.bin")) && File.Exists(h.Secret(@"_failed\image.bin.why.txt")), "the file and its reason are in _failed");
        Assert.Contains("NUL", await File.ReadAllTextAsync(h.Secret(@"_failed\image.bin.why.txt"), EngineHarness.Token));
        Assert.Empty(h.Api.All);
    }

    [Fact]
    public async Task Github_out_of_reach_retries_then_gives_up_into_the_failed_folder()
    {
        await using var h = new EngineHarness(retrySeconds: 0.5);
        h.Api.Offline = true;
        h.Start();
        await File.WriteAllTextAsync(h.Secret("notes.md"), "hello", EngineHarness.Token);

        await Eventually.True(() => File.Exists(h.Secret(@"_failed\notes.md.why.txt")), "the file lands in _failed");
        Assert.Contains("could not be reached", await File.ReadAllTextAsync(h.Secret(@"_failed\notes.md.why.txt"), EngineHarness.Token));
    }

    [Fact]
    public async Task Moving_a_failed_file_back_uploads_it_and_removes_the_reason()
    {
        await using var h = new EngineHarness();
        h.Api.SignedOut = true;
        h.Start();
        await File.WriteAllTextAsync(h.Secret("notes.md"), "hello", EngineHarness.Token);
        await Eventually.True(() => File.Exists(h.Secret(@"_failed\notes.md.why.txt")), "signed out sends the file straight to _failed");
        Assert.Contains("gh auth login", await File.ReadAllTextAsync(h.Secret(@"_failed\notes.md.why.txt"), EngineHarness.Token));

        h.Api.SignedOut = false;
        File.Move(h.Secret(@"_failed\notes.md"), h.Secret("notes.md"));
        await Eventually.True(() => h.OnlyGist?.File("notes.md")?.Content == "hello", "the gist is created");
        await Eventually.True(() => !File.Exists(h.Secret(@"_failed\notes.md.why.txt")), "the reason file is gone");
    }
}

public sealed class VerbTests
{
    [Fact]
    public async Task Edit_downloads_the_file_and_only_a_changed_save_uploads()
    {
        await using var h = new EngineHarness();
        await h.SeedAsync(false, string.Empty, ("notes.md", "v1"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        h.Submit("edit", h.Secret("notes.md.url"));
        await Eventually.True(() => h.Opened.Contains(h.Secret("notes.md")), "the download opens");
        Assert.Equal("v1", await File.ReadAllTextAsync(h.Secret("notes.md"), EngineHarness.Token));
        await Eventually.Stays(() => File.Exists(h.Secret("notes.md")), "an unchanged download stays", 1_000);

        await File.WriteAllTextAsync(h.Secret("notes.md"), "v2", EngineHarness.Token);
        await Eventually.True(() => h.Api.All.Single().File("notes.md")?.Content == "v2" && !File.Exists(h.Secret("notes.md")), "the change is uploaded");
    }

    [Fact]
    public async Task Make_public_replaces_the_secret_gist_with_a_public_copy()
    {
        await using var h = new EngineHarness(undoSeconds: 0.3);
        var secret = await h.SeedAsync(false, "desc", ("notes.md", "n"));
        h.Start();
        await Eventually.True(() => File.Exists(h.Secret("notes.md.url")), "the shortcut appears");

        h.Submit("make-public", h.Secret("notes.md.url"));
        await Eventually.True(() => h.OnlyGist is { IsPublic: true, Description: "desc" } gist && gist.Id != secret.Id, "only a public copy is left");
        await Eventually.True(() => File.Exists(h.Public("notes.md.url")) && !File.Exists(h.Secret("notes.md.url")), "the shortcut moves to Public");
    }

    [Fact]
    public async Task Send_to_uploads_a_copy_and_sending_it_again_updates_the_gist()
    {
        await using var h = new EngineHarness();
        h.Start();
        var original = Path.Combine(h.Outside, "notes.md");
        await File.WriteAllTextAsync(original, "v1", EngineHarness.Token);

        h.Submit("send-secret", original);
        await Eventually.True(() => h.OnlyGist?.File("notes.md")?.Content == "v1", "a secret gist holds the copy");
        await Eventually.True(() => EngineHarness.Shortcut(h.Secret("notes.md.url"))?.SourcePath == original, "the shortcut remembers the original");
        Assert.True(File.Exists(original));

        await File.WriteAllTextAsync(original, "v2", EngineHarness.Token);
        h.Submit("send-secret", original);
        await Eventually.True(() => h.OnlyGist?.File("notes.md")?.Content == "v2", "the same gist has v2");
    }

    [Fact]
    public async Task Send_to_with_another_file_of_the_same_name_makes_a_new_gist()
    {
        await using var h = new EngineHarness();
        h.Start();
        Directory.CreateDirectory(Path.Combine(h.Outside, "one"));
        Directory.CreateDirectory(Path.Combine(h.Outside, "two"));
        var first = Path.Combine(h.Outside, "one", "README.md");
        var second = Path.Combine(h.Outside, "two", "README.md");
        await File.WriteAllTextAsync(first, "first", EngineHarness.Token);
        await File.WriteAllTextAsync(second, "second", EngineHarness.Token);

        h.Submit("send-secret", first);
        await Eventually.True(() => h.Api.All.Count == 1, "the first gist exists");
        h.Submit("send-secret", second);
        await Eventually.True(() => h.Api.All.Count == 2, "the second file makes a second gist");
        await Eventually.True(() => Directory.EnumerateFiles(h.Folders.Secret, "README (*).md.url").Count() == 2, "both shortcuts carry short ids after the refresh");
    }

    [Fact]
    public async Task Send_to_refuses_a_file_that_is_not_text()
    {
        await using var h = new EngineHarness();
        h.Start();
        var original = Path.Combine(h.Outside, "image.bin");
        await File.WriteAllBytesAsync(original, [0x89, 0x00], EngineHarness.Token);

        h.Submit("send-public", original);
        await Eventually.True(() => h.Notifier.Messages.Any(m => m.Title == "Not sent"), "a notification refuses the file");
        Assert.Empty(h.Api.All);
    }
}

public sealed class InstallerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _sandbox = $@"Software\GistDrive.Tests.{Guid.NewGuid():N}";

    public void Dispose()
    {
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(_sandbox, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    [Fact]
    public void Install_writes_the_integration_and_uninstall_takes_it_all_back()
    {
        var target = new InstallTarget($@"{_sandbox}\Classes", $@"{_sandbox}\Explorer", Guid.NewGuid(), Path.Combine(_temp.Path, "SendTo"), Path.Combine(_temp.Path, "Startup"), "GistDrive.Tests");
        var folders = new Folders(Path.Combine(_temp.Path, "GistDrive"));
        var exe = Environment.ProcessPath!;
        var clsid = target.SidebarClsid.ToString("B").ToUpperInvariant();
        var user = Microsoft.Win32.Registry.CurrentUser;

        Installer.Install(target, folders, exe);

        Assert.True(Directory.Exists(folders.Secret) && Directory.Exists(folders.Public));
        using (var sidebar = user.OpenSubKey($@"{target.ClassesKey}\CLSID\{clsid}"))
        {
            Assert.Equal("GistDrive", sidebar?.GetValue(string.Empty));
            Assert.Equal(1, sidebar?.GetValue("System.IsPinnedToNameSpaceTree"));
        }

        using (var bag = user.OpenSubKey($@"{target.ClassesKey}\CLSID\{clsid}\Instance\InitPropertyBag")) Assert.Equal(folders.Root, bag?.GetValue("TargetFolderPath"));
        using (var pinned = user.OpenSubKey($@"{target.ExplorerKey}\Desktop\NameSpace\{clsid}")) Assert.NotNull(pinned);
        using (var edit = user.OpenSubKey($@"{target.ClassesKey}\AllFilesystemObjects\shell\GistDrive.Edit"))
        {
            Assert.Equal("Edit", edit?.GetValue("MUIVerb"));
            Assert.Contains($"System.ItemPathDisplay:~<\"{folders.Root}\\\"", (string?)edit?.GetValue("AppliesTo"));
        }

        using (var command = user.OpenSubKey($@"{target.ClassesKey}\Directory\shell\GistDrive.MakePublic\command")) Assert.Equal($"\"{exe}\" make-public \"%1\"", command?.GetValue(string.Empty));
        Assert.Equal((exe, "send-secret"), ShellLink.Read(Path.Combine(target.SendToFolder, Installer.SecretSendTo)));
        Assert.Equal((exe, "send-public"), ShellLink.Read(Path.Combine(target.SendToFolder, Installer.PublicSendTo)));
        Assert.Equal((exe, string.Empty), ShellLink.Read(Path.Combine(target.StartupFolder, Installer.StartupShortcut)));

        // The icons reach the test output as content of the GistDrive project.
        var icons = FolderIcons.Beside(exe);
        using (var icon = user.OpenSubKey($@"{target.ClassesKey}\CLSID\{clsid}\DefaultIcon")) Assert.Equal($"{Path.Combine(icons, FolderIcons.RootIcon)},0", icon?.GetValue(string.Empty));
        using (var appId = user.OpenSubKey($@"{target.ClassesKey}\AppUserModelId\{target.AppId}")) Assert.Equal(Path.Combine(icons, FolderIcons.NotificationImage), appId?.GetValue("IconUri"));
        Assert.Equal($"{Path.Combine(icons, FolderIcons.SecretIcon)},0", FolderIcons.IconResource(File.ReadAllLines(Path.Combine(folders.Secret, FolderIcons.DesktopIni))));

        Assert.False(Installer.IsStale(target, folders, exe));
        Assert.True(Installer.IsStale(target, new Folders(Path.Combine(_temp.Path, "Elsewhere")), exe));
        Assert.True(Installer.IsStale(target, folders, @"C:\Moved\GistDrive.exe"));

        Installer.Uninstall(target, folders);

        Assert.False(File.Exists(Path.Combine(folders.Secret, FolderIcons.DesktopIni)));
        Assert.False(Installer.IsStale(target, folders, exe));
        Assert.Null(user.OpenSubKey($@"{target.ClassesKey}\CLSID\{clsid}"));
        Assert.Null(user.OpenSubKey($@"{target.ExplorerKey}\Desktop\NameSpace\{clsid}"));
        Assert.Null(user.OpenSubKey($@"{target.ClassesKey}\AllFilesystemObjects\shell\GistDrive.Edit"));
        Assert.Null(user.OpenSubKey($@"{target.ClassesKey}\AppUserModelId\{target.AppId}"));
        Assert.Empty(Directory.EnumerateFiles(target.SendToFolder));
        Assert.True(Directory.Exists(folders.Secret), "the files stay");
    }
}

public sealed class IpcTests
{
    [Fact]
    public async Task A_request_reaches_the_server()
    {
        var pipe = $"GistDrive.Tests-{Guid.NewGuid():N}";
        using var stop = new CancellationTokenSource();
        var received = new TaskCompletionSource<VerbRequest>();
        var server = Ipc.ServeAsync(pipe, request =>
        {
            received.TrySetResult(request);
            return Task.CompletedTask;
        }, stop.Token);

        Assert.True(await Ipc.SendAsync(pipe, new VerbRequest("edit", [@"C:\x\notes.md.url"]), TimeSpan.FromSeconds(5)));
        var request = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("edit", request.Verb);
        Assert.Equal([@"C:\x\notes.md.url"], request.Paths);

        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public async Task Nobody_listening_means_false()
    {
        Assert.False(await Ipc.SendAsync($"GistDrive.Tests-{Guid.NewGuid():N}", new VerbRequest("refresh", []), TimeSpan.FromMilliseconds(200)));
    }
}
