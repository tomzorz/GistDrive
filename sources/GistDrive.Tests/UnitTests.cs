using System.Text;

namespace GistDrive.Tests;

public sealed class NamingTests
{
    [Theory]
    [InlineData("notes.md", "notes.md")]
    [InlineData("a:b?c*.md", "a_b_c_.md")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("...", "_")]
    public void Sanitize_makes_names_safe_for_windows(string name, string expected) => Assert.Equal(expected, Naming.Sanitize(name));

    [Theory]
    [InlineData("gistfile1.txt", "gistfile1 (a1b2c3d).txt")]
    [InlineData("Makefile", "Makefile (a1b2c3d)")]
    [InlineData(".gitignore", ".gitignore (a1b2c3d)")]
    public void The_short_id_goes_before_the_extension(string name, string expected) =>
        Assert.Equal(expected, Naming.WithShortId(name, "a1b2c3d4e5f60718293a4b5c6d7e8f90"));

    [Theory]
    [InlineData("gistfile1 (a1b2c3d).txt", "gistfile1.txt")]
    [InlineData(".gitignore (a1b2c3d)", ".gitignore")]
    [InlineData("notes (2).md", "notes (2).md")]
    [InlineData("notes.md", "notes.md")]
    public void Only_the_gists_own_short_id_comes_off(string name, string expected) =>
        Assert.Equal(expected, Naming.WithoutShortId(name, "a1b2c3d4e5f60718293a4b5c6d7e8f90"));

    [Theory]
    [InlineData("~$notes.docx")]
    [InlineData("notes.md.tmp")]
    [InlineData(".notes.md.swp")]
    [InlineData("notes.md~")]
    [InlineData("4913")]
    [InlineData("notes.md___jb_tmp___")]
    [InlineData("desktop.ini")]
    [InlineData("notes.md.why.txt")]
    public void Editor_scratch_files_are_recognised(string name) => Assert.True(Naming.IsScratch(name));

    [Theory]
    [InlineData("notes.md")]
    [InlineData("notes (2).md")]
    [InlineData("tmp.md")]
    public void Real_files_are_not_scratch(string name) => Assert.False(Naming.IsScratch(name));

    [Fact]
    public void A_gist_folder_is_named_after_the_description_or_the_first_file()
    {
        static Gist Gist(string description) => new("1", false, description, "u", [new("b.md", 1, null, false), new("a.md", 1, null, false)], default, default);
        Assert.Equal("My notes", Naming.GistFolderName(Gist(" My notes ")));
        Assert.Equal("a.md", Naming.GistFolderName(Gist(string.Empty)));
        Assert.Equal(Naming.MaxFolderNameLength, Naming.GistFolderName(Gist(new string('x', 300))).Length);
    }
}

public sealed class GistShortcutTests
{
    [Fact]
    public void A_shortcut_round_trips_through_ascii_text()
    {
        var shortcut = new GistShortcut("abc123", "ünïcødé notes.md", "https://gist.github.com/abc123", @"D:\Sources\Ünïcødé\notes.md");
        var text = shortcut.Render();
        Assert.True(text.All(c => c < 128));
        Assert.Equal(shortcut, GistShortcut.Parse(text.Split("\r\n")));
    }

    [Fact]
    public void The_sections_windows_adds_are_ignored()
    {
        var lines = new[]
        {
            "[InternetShortcut]", "URL=https://gist.github.com/abc", "IDList=",
            "[GistDrive]", "Id=abc", "File=notes.md",
            "[{000214A0-0000-0000-C000-000000000046}]", "Prop3=19,11",
        };
        Assert.Equal(new GistShortcut("abc", "notes.md", "https://gist.github.com/abc"), GistShortcut.Parse(lines));
    }

    [Fact]
    public void A_url_file_from_elsewhere_is_not_a_gist_shortcut() =>
        Assert.Null(GistShortcut.Parse(["[InternetShortcut]", "URL=https://example.com"]));
}

public sealed class TextFileTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Utf8_text_passes_and_loses_its_byte_order_mark()
    {
        var path = Write([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("héllo")]);
        Assert.Equal("héllo", TextFile.Read(path).Text);
    }

    [Fact]
    public void A_nul_byte_fails_the_check() => Assert.Contains("NUL", TextFile.Read(Write([0x41, 0x00, 0x42])).Reason);

    [Fact]
    public void Invalid_utf8_fails_the_check() => Assert.Contains("UTF-8", TextFile.Read(Write([0x41, 0xC3, 0x28])).Reason);

    [Fact]
    public void More_than_1_mb_fails_the_check() => Assert.Contains("1 MB", TextFile.Read(Write(Enumerable.Repeat((byte)'a', TextFile.MaxBytes + 1).ToArray())).Reason);

    [Fact]
    public void An_empty_file_is_empty_text() => Assert.Equal(string.Empty, TextFile.Read(Write([])).Text);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_with_a_byte_order_mark_passes_as_text(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, byteOrderMark: true);
        var path = Write([.. encoding.GetPreamble(), .. encoding.GetBytes("héllo ✓ 𝄞\r\n")]);
        Assert.Equal("héllo ✓ 𝄞\r\n", TextFile.Read(path).Text);
    }

    [Fact]
    public void Utf16_counts_its_size_as_utf8()
    {
        // 600,000 ASCII letters take 1.2 MB as UTF-16 but only 600 kB as UTF-8, which is what GitHub stores.
        Assert.True(TextFile.Read(Write([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(new string('a', 600_000))])).IsText);

        // 400,000 euro signs take 800 kB as UTF-16 and 1.2 MB as UTF-8.
        Assert.Contains("1 MB", TextFile.Read(Write([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(new string('€', 400_000))])).Reason);
    }

    [Fact]
    public void Broken_utf16_fails_the_check() => Assert.Contains("UTF-16", TextFile.Read(Write([0xFF, 0xFE, 0x00, 0xD8])).Reason);

    [Fact]
    public void Utf16_with_a_nul_character_fails_the_check() => Assert.Contains("NUL", TextFile.Read(Write([0xFF, 0xFE, 0x41, 0x00, 0x00, 0x00])).Reason);

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(_temp.Path, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

public sealed class FolderIconsTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly Folders _folders;
    private readonly string _icons;

    public FolderIconsTests()
    {
        _folders = new Folders(Path.Combine(_temp.Path, "GistDrive"));
        _folders.EnsureTopFolders();
        _icons = Icons(Path.Combine(_temp.Path, "app"));
    }

    public void Dispose()
    {
        FolderIcons.Remove(_folders);
        _temp.Dispose();
    }

    [Fact]
    public void Apply_gives_each_folder_its_icon_and_remove_takes_it_off()
    {
        FolderIcons.Apply(_folders, _icons);

        foreach (var (folder, icon) in new[] { (_folders.Root, FolderIcons.RootIcon), (_folders.Secret, FolderIcons.SecretIcon), (_folders.Public, FolderIcons.PublicIcon) })
        {
            Assert.Equal($"{Path.Combine(_icons, icon)},0", FolderIcons.IconResource(File.ReadAllLines(Ini(folder))));
            Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(Ini(folder)) & (FileAttributes.Hidden | FileAttributes.System));
            Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReadOnly), "Explorer reads desktop.ini only in a read-only folder");
        }

        FolderIcons.Remove(_folders);

        Assert.False(File.Exists(Ini(_folders.Secret)));
        Assert.False(new DirectoryInfo(_folders.Secret).Attributes.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void An_icon_the_owner_picked_stays()
    {
        File.WriteAllLines(Ini(_folders.Secret), ["[.ShellClassInfo]", @"IconResource=C:\Mine\vault.ico,0"]);

        FolderIcons.Apply(_folders, _icons);
        Assert.Equal(@"C:\Mine\vault.ico,0", FolderIcons.IconResource(File.ReadAllLines(Ini(_folders.Secret))));

        FolderIcons.Remove(_folders);
        Assert.True(File.Exists(Ini(_folders.Secret)));
    }

    [Fact]
    public void The_icon_follows_a_moved_install_and_other_settings_stay()
    {
        var old = Icons(Path.Combine(_temp.Path, "old"));
        FolderIcons.Apply(_folders, old);
        var ini = Ini(_folders.Public);
        File.SetAttributes(ini, FileAttributes.Normal);
        File.AppendAllLines(ini, ["[ViewState]", "FolderType=Documents"], Encoding.Unicode);

        FolderIcons.Apply(_folders, _icons);
        var lines = File.ReadAllLines(ini);
        Assert.Equal($"{Path.Combine(_icons, FolderIcons.PublicIcon)},0", FolderIcons.IconResource(lines));
        Assert.Contains("FolderType=Documents", lines);

        FolderIcons.Remove(_folders);
        Assert.Equal(["[.ShellClassInfo]", "[ViewState]", "FolderType=Documents"], File.ReadAllLines(ini));
    }

    [Fact]
    public void The_icon_line_goes_first_in_its_section()
    {
        Assert.Equal(["[.ShellClassInfo]", "IconResource=x.ico,0"], FolderIcons.WithIconResource([], "x.ico,0"));
        Assert.Equal(["[.ShellClassInfo]", "IconResource=y.ico,0", "InfoTip=Gists"], FolderIcons.WithIconResource(["[.ShellClassInfo]", "iconresource = x.ico,0", "InfoTip=Gists"], "y.ico,0"));
        Assert.Equal(["[.ShellClassInfo]", "IconResource=x.ico,0", "[ViewState]", "Mode="], FolderIcons.WithIconResource(["[ViewState]", "Mode="], "x.ico,0"));
    }

    private static string Ini(string folder) => Path.Combine(folder, FolderIcons.DesktopIni);

    private static string Icons(string appFolder)
    {
        var icons = Path.Combine(appFolder, FolderIcons.IconFolder);
        Directory.CreateDirectory(icons);
        foreach (var icon in new[] { FolderIcons.RootIcon, FolderIcons.SecretIcon, FolderIcons.PublicIcon }) File.WriteAllBytes(Path.Combine(icons, icon), [0]);
        return icons;
    }
}

public sealed class HeldFilesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Record => Path.Combine(_temp.Path, HeldFiles.FileName);

    [Fact]
    public void A_hold_survives_a_reload_until_the_file_changes()
    {
        var file = Touch("notes.md");
        var stamp = File.GetLastWriteTimeUtc(file);
        HeldFiles.Load(Record).Add(file, stamp);

        var reloaded = HeldFiles.Load(Record);
        Assert.True(reloaded.Holds(file, stamp));
        Assert.True(reloaded.Holds(file.ToUpperInvariant(), stamp), "Windows paths ignore case");
        Assert.False(reloaded.Holds(file, stamp.AddTicks(1)), "a save moves the write time on");
    }

    [Fact]
    public void Rename_remove_and_prune_reach_the_record()
    {
        var renamed = Touch("renamed.md");
        var removed = Touch("removed.md");
        var record = HeldFiles.Load(Record);
        record.Add(Path.Combine(_temp.Path, "old name.md"), DateTime.UnixEpoch);
        record.Add(removed, DateTime.UnixEpoch);
        record.Add(Path.Combine(_temp.Path, "gone.md"), DateTime.UnixEpoch);

        record.Rename(Path.Combine(_temp.Path, "old name.md"), renamed);
        record.Remove(removed);
        record.Prune();

        var reloaded = HeldFiles.Load(Record);
        Assert.True(reloaded.Holds(renamed, DateTime.UnixEpoch));
        Assert.False(reloaded.Holds(removed, DateTime.UnixEpoch));
        Assert.False(reloaded.Holds(Path.Combine(_temp.Path, "gone.md"), DateTime.UnixEpoch));
    }

    [Fact]
    public void An_unreadable_record_starts_empty_and_gets_replaced()
    {
        File.WriteAllText(Record, "{ not json");
        var file = Touch("notes.md");

        var record = HeldFiles.Load(Record);
        Assert.False(record.Holds(file, File.GetLastWriteTimeUtc(file)));

        record.Add(file, DateTime.UnixEpoch);
        Assert.True(HeldFiles.Load(Record).Holds(file, DateTime.UnixEpoch));
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, name);
        return path;
    }
}

public sealed class FoldersTests
{
    private readonly Folders _folders = new(@"C:\Root\GistDrive");

    [Theory]
    [InlineData(@"C:\Root\GistDrive", Place.Outside)]
    [InlineData(@"C:\Root\GistDrive\loose.md", Place.Outside)]
    [InlineData(@"C:\Elsewhere\Secret\a.md", Place.Outside)]
    [InlineData(@"C:\Root\GistDrive\Secret", Place.TopFolder)]
    [InlineData(@"C:\Root\GistDrive\Secret\_failed\a.md", Place.Failed)]
    [InlineData(@"C:\Root\GistDrive\public\a.md", Place.InTop)]
    [InlineData(@"C:\Root\GistDrive\Secret\Notes\a.md.url", Place.InSubfolder)]
    [InlineData(@"C:\Root\GistDrive\Secret\Notes\deep\a.md", Place.Deeper)]
    public void Locate_names_the_place(string path, Place expected) => Assert.Equal(expected, _folders.Locate(path).Place);

    [Fact]
    public void Locate_names_the_visibility_and_the_subfolder()
    {
        var location = _folders.Locate(@"C:\Root\GistDrive\Public\Notes\a.md");
        Assert.Equal(Visibility.Public, location.Visibility);
        Assert.Equal(@"C:\Root\GistDrive\Public\Notes", location.Subfolder);
    }
}

public sealed class LayoutTests
{
    private readonly Folders _folders = new(@"C:\Root\GistDrive");

    [Fact]
    public void A_single_file_gist_is_a_shortcut_in_its_top_folder()
    {
        var entry = Assert.Single(Layout.Plan(_folders, [Gist("aaaaaaa1", isPublic: true, "", "notes.md")]));
        Assert.Equal(@"C:\Root\GistDrive\Public\notes.md.url", entry.Path);
        Assert.Equal("notes.md", entry.FileName);
        Assert.Equal("https://gist.github.com/aaaaaaa1", entry.Url);
    }

    [Fact]
    public void A_multi_file_gist_is_a_folder_of_shortcuts_with_file_anchors()
    {
        var plan = Layout.Plan(_folders, [Gist("bbbbbbb1", isPublic: false, "My notes", "b.md", "A File.md")]);
        Assert.Equal([@"C:\Root\GistDrive\Secret\My notes", @"C:\Root\GistDrive\Secret\My notes\A File.md.url", @"C:\Root\GistDrive\Secret\My notes\b.md.url"], plan.Select(e => e.Path));
        Assert.Null(plan[0].FileName);
        Assert.Equal("https://gist.github.com/bbbbbbb1#file-a-file-md", plan[1].Url);
    }

    [Fact]
    public void Every_shortcut_in_a_clash_gets_its_short_id()
    {
        var plan = Layout.Plan(_folders, [Gist("1111111aaa", false, "", "gistfile1.txt"), Gist("2222222bbb", false, "", "GISTFILE1.txt"), Gist("3333333ccc", false, "", "other.txt")]);
        Assert.Equal(
            [@"C:\Root\GistDrive\Secret\gistfile1 (1111111).txt.url", @"C:\Root\GistDrive\Secret\GISTFILE1 (2222222).txt.url", @"C:\Root\GistDrive\Secret\other.txt.url"],
            plan.Select(e => e.Path));
    }

    [Fact]
    public void Same_names_in_different_top_folders_do_not_clash()
    {
        var plan = Layout.Plan(_folders, [Gist("1111111aaa", false, "", "a.md"), Gist("2222222bbb", true, "", "a.md")]);
        Assert.Equal([@"C:\Root\GistDrive\Secret\a.md.url", @"C:\Root\GistDrive\Public\a.md.url"], plan.Select(e => e.Path));
    }

    [Fact]
    public void A_gist_folder_never_takes_the_failed_folder_name()
    {
        var plan = Layout.Plan(_folders, [Gist("1111111aaa", false, "_failed", "a.md", "b.md")]);
        Assert.Equal(@"C:\Root\GistDrive\Secret\_failed (1111111)", plan[0].Path);
    }

    [Fact]
    public void Every_entry_carries_its_gists_times()
    {
        var plan = Layout.Plan(_folders, [Gist("1111111aaa", false, "Notes", "a.md", "b.md")]);
        Assert.All(plan, e => Assert.Equal((Created, Updated), (e.CreatedAt, e.UpdatedAt)));
    }

    private static readonly DateTimeOffset Created = new(2024, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = new(2025, 6, 2, 11, 30, 0, TimeSpan.Zero);

    private static Gist Gist(string id, bool isPublic, string description, params string[] files) =>
        new(id, isPublic, description, $"https://gist.github.com/{id}", [.. files.Select(f => new GistFile(f, 1, null, false))], Created, Updated);
}

public sealed class GistParsingTests
{
    // The shape of GET /gists/{id} from GitHub's REST reference, cut to the fields GistDrive reads.
    private const string Response = """
        {
          "id": "aa5a315d61ae9438b18d",
          "html_url": "https://gist.github.com/aa5a315d61ae9438b18d",
          "public": false,
          "description": null,
          "created_at": "2010-04-14T02:15:15Z",
          "updated_at": "2011-06-20T11:34:15Z",
          "files": {
            "hello_world.rb": { "filename": "hello_world.rb", "size": 167, "truncated": false, "content": "puts 'hi'" },
            "big.txt": { "filename": "big.txt", "size": 2000000, "truncated": true, "content": "partial" }
          }
        }
        """;

    [Fact]
    public void A_github_gist_parses_with_its_times_and_files()
    {
        var gist = GhGistApi.ParseGist(Response);
        Assert.Equal("aa5a315d61ae9438b18d", gist.Id);
        Assert.False(gist.IsPublic);
        Assert.Equal(string.Empty, gist.Description);
        Assert.Equal(new DateTimeOffset(2010, 4, 14, 2, 15, 15, TimeSpan.Zero), gist.CreatedAt);
        Assert.Equal(new DateTimeOffset(2011, 6, 20, 11, 34, 15, TimeSpan.Zero), gist.UpdatedAt);
        Assert.Equal("puts 'hi'", gist.File("hello_world.rb")?.Content);
        Assert.True(gist.File("big.txt")?.Truncated);
    }
}

public sealed class ClassifyTests
{
    [Theory]
    [InlineData("gh: Not Found (HTTP 404)", GistErrorKind.NotFound)]
    [InlineData("gh: Bad credentials (HTTP 401)", GistErrorKind.NotSignedIn)]
    [InlineData("gh: Validation Failed (HTTP 422)", GistErrorKind.Rejected)]
    [InlineData("gh: Service Unavailable (HTTP 503)", GistErrorKind.Unavailable)]
    [InlineData("gh: API rate limit exceeded (HTTP 403)", GistErrorKind.RateLimited)]
    [InlineData("To get started with GitHub CLI, please run:  gh auth login", GistErrorKind.NotSignedIn)]
    [InlineData("error connecting to api.github.com", GistErrorKind.Unavailable)]
    public void Gh_errors_map_to_kinds(string stderr, GistErrorKind expected) =>
        Assert.Equal(expected, GhGistApi.Classify(["api", "gists"], stderr).Kind);
}
