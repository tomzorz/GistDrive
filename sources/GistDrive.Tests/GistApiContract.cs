namespace GistDrive.Tests;

/// <summary>
/// What every IGistApi must do, run against the fake on every test run and against GitHub
/// when GISTDRIVE_LIVE_GITHUB=1. The live run creates secret gists only and deletes each one.
/// </summary>
public abstract class GistApiContract
{
    private const string Description = "GistDrive contract test, safe to delete";

    protected abstract IGistApi Api { get; }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Created_text_comes_back_unchanged()
    {
        const string text = "héllo ✓ ünïcødé\nsecond line\n";
        await WithGistAsync(new() { ["contract ünïcødé.md"] = text }, async gist =>
        {
            var read = await Api.GetAsync(gist.Id, Token);
            Assert.False(read.IsPublic);
            Assert.Equal(Description, read.Description);
            Assert.Equal(text, read.File("contract ünïcødé.md")?.Content);
        });
    }

    [Fact]
    public async Task A_new_gist_shows_up_in_the_list_without_content()
    {
        await WithGistAsync(new() { ["listed.md"] = "listed" }, async gist =>
        {
            var listed = (await Api.ListAsync(Token)).Single(g => g.Id == gist.Id);
            Assert.Equal("listed.md", Assert.Single(listed.Files).Name);
            Assert.Null(listed.Files[0].Content);
        });
    }

    [Fact]
    public async Task An_update_replaces_the_content()
    {
        await WithGistAsync(new() { ["notes.md"] = "v1" }, async gist =>
        {
            await Api.UpdateAsync(gist.Id, GistUpdate.Content("notes.md", "v2"), Token);
            Assert.Equal("v2", (await Api.GetAsync(gist.Id, Token)).File("notes.md")?.Content);
        });
    }

    [Fact]
    public async Task A_gist_carries_its_creation_and_update_times()
    {
        await WithGistAsync(new() { ["notes.md"] = "v1" }, async gist =>
        {
            Assert.InRange(gist.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
            await Api.UpdateAsync(gist.Id, GistUpdate.Content("notes.md", "v2"), Token);
            var read = await Api.GetAsync(gist.Id, Token);
            Assert.Equal(gist.CreatedAt, read.CreatedAt);
            Assert.True(read.UpdatedAt >= gist.UpdatedAt);
        });
    }

    [Fact]
    public async Task A_rename_keeps_the_content()
    {
        await WithGistAsync(new() { ["notes.md"] = "kept" }, async gist =>
        {
            await Api.UpdateAsync(gist.Id, GistUpdate.Rename("notes.md", "ideas.md"), Token);
            var read = await Api.GetAsync(gist.Id, Token);
            Assert.Null(read.File("notes.md"));
            Assert.Equal("kept", read.File("ideas.md")?.Content);
        });
    }

    [Fact]
    public async Task Files_can_be_added_and_removed()
    {
        await WithGistAsync(new() { ["a.md"] = "a", ["b.md"] = "b" }, async gist =>
        {
            await Api.UpdateAsync(gist.Id, GistUpdate.Content("c.md", "c"), Token);
            Assert.Equal(3, (await Api.GetAsync(gist.Id, Token)).Files.Count);
            await Api.UpdateAsync(gist.Id, GistUpdate.Remove("a.md"), Token);
            Assert.Equal(["b.md", "c.md"], (await Api.GetAsync(gist.Id, Token)).Files.Select(f => f.Name).Order());
        });
    }

    [Fact]
    public async Task The_description_can_change()
    {
        await WithGistAsync(new() { ["a.md"] = "a" }, async gist =>
        {
            await Api.UpdateAsync(gist.Id, GistUpdate.NewDescription($"{Description}, renamed"), Token);
            Assert.Equal($"{Description}, renamed", (await Api.GetAsync(gist.Id, Token)).Description);
        });
    }

    [Fact]
    public async Task A_deleted_gist_is_not_found()
    {
        var gist = await Api.CreateAsync(false, Description, new Dictionary<string, string> { ["gone.md"] = "gone" }, Token);
        await Api.DeleteAsync(gist.Id, Token);
        var error = await Assert.ThrowsAsync<GistApiException>(() => Api.GetAsync(gist.Id, Token));
        Assert.Equal(GistErrorKind.NotFound, error.Kind);
    }

    [Fact]
    public async Task Empty_content_is_rejected()
    {
        var error = await Assert.ThrowsAsync<GistApiException>(() => Api.CreateAsync(false, Description, new Dictionary<string, string> { ["empty.md"] = string.Empty }, Token));
        Assert.Equal(GistErrorKind.Rejected, error.Kind);
    }

    private async Task WithGistAsync(Dictionary<string, string> files, Func<Gist, Task> test)
    {
        var gist = await Api.CreateAsync(false, Description, files, Token);
        try
        {
            await test(gist);
        }
        finally
        {
            await Api.DeleteAsync(gist.Id, Token);
        }
    }
}

public sealed class FakeGistApiContractTests : GistApiContract
{
    protected override IGistApi Api { get; } = new FakeGistApi();
}

public sealed class GhGistApiContractTests : GistApiContract
{
    public GhGistApiContractTests() =>
        Assert.SkipUnless(Environment.GetEnvironmentVariable("GISTDRIVE_LIVE_GITHUB") == "1", "Set GISTDRIVE_LIVE_GITHUB=1 to run against GitHub (creates and deletes secret gists).");

    protected override IGistApi Api { get; } = new GhGistApi();
}
