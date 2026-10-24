using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GistDrive;

/// <summary>
/// The gist calls through the GitHub CLI (REQ-ENV-3). Every call runs `gh api`, so the sign-in
/// of gh is the only credential GistDrive ever uses, and GistDrive stores no token.
/// </summary>
public sealed partial class GhGistApi : IGistApi
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<IReadOnlyList<Gist>> ListAsync(CancellationToken cancellationToken)
    {
        // --slurp wraps the pages in one outer array: [[gist, ...], [gist, ...]].
        var json = await RunAsync(["api", "gists", "--paginate", "--slurp"], null, cancellationToken);
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().SelectMany(page => page.EnumerateArray()).Select(ParseGist)];
    }

    public async Task<Gist> GetAsync(string id, CancellationToken cancellationToken) =>
        ParseGist(await RunAsync(["api", $"gists/{id}"], null, cancellationToken));

    public async Task<Gist> CreateAsync(bool isPublic, string description, IReadOnlyDictionary<string, string> files, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["description"] = description,
            ["public"] = isPublic,
            ["files"] = new JsonObject(files.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)new JsonObject { ["content"] = f.Value }))),
        };
        return ParseGist(await RunAsync(["api", "gists", "--method", "POST", "--input", "-"], body.ToJsonString(), cancellationToken));
    }

    public async Task<Gist> UpdateAsync(string id, GistUpdate update, CancellationToken cancellationToken)
    {
        var body = new JsonObject();
        if (update.Description is not null) body["description"] = update.Description;
        if (update.Files.Count > 0) body["files"] = new JsonObject(update.Files.Select(f => KeyValuePair.Create(f.Key, FileChangeJson(f.Value))));
        return ParseGist(await RunAsync(["api", $"gists/{id}", "--method", "PATCH", "--input", "-"], body.ToJsonString(), cancellationToken));
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken) =>
        await RunAsync(["api", $"gists/{id}", "--method", "DELETE"], null, cancellationToken);

    private static JsonNode? FileChangeJson(FileChange? change)
    {
        if (change is null) return null;

        var node = new JsonObject();
        if (change.Content is not null) node["content"] = change.Content;
        if (change.NewName is not null) node["filename"] = change.NewName;
        return node;
    }

    internal static Gist ParseGist(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseGist(document.RootElement);
    }

    private static Gist ParseGist(JsonElement gist)
    {
        var files = gist.GetProperty("files").EnumerateObject().Select(file => new GistFile(
            file.Name,
            file.Value.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
            file.Value.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : null,
            file.Value.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True));
        var description = gist.TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : string.Empty;
        return new Gist(
            gist.GetProperty("id").GetString()!,
            gist.GetProperty("public").GetBoolean(),
            description,
            gist.GetProperty("html_url").GetString()!,
            [.. files],
            gist.GetProperty("created_at").GetDateTimeOffset(),
            gist.GetProperty("updated_at").GetDateTimeOffset());
    }

    private static async Task<string> RunAsync(IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("gh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        if (input is not null) start.StandardInputEncoding = Utf8;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        start.Environment["NO_COLOR"] = "1";

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new GistApiException(GistErrorKind.CliMissing, "gh did not start");
        }
        catch (Win32Exception ex)
        {
            throw new GistApiException(GistErrorKind.CliMissing, $"gh could not start: {ex.Message}");
        }

        using (process)
        {
            // Read both streams before writing the input, so a large reply can never block gh.
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            var stdout = await output;
            var stderr = await error;
            return process.ExitCode == 0 ? stdout : throw Classify(arguments, stderr);
        }
    }

    /// <summary>
    /// Turns what gh printed on failure into a kind (REQ-FAIL-2): network and server errors and
    /// rate limits are transient, everything else needs the owner.
    /// </summary>
    internal static GistApiException Classify(IReadOnlyList<string> arguments, string stderr)
    {
        var message = $"gh {string.Join(' ', arguments.Take(2))}: {stderr.Trim()}";
        var status = HttpStatus().Match(stderr) is { Success: true } match ? int.Parse(match.Groups[1].Value) : 0;
        var kind = status switch
        {
            401 => GistErrorKind.NotSignedIn,
            404 => GistErrorKind.NotFound,
            429 => GistErrorKind.RateLimited,
            403 when stderr.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => GistErrorKind.RateLimited,
            >= 500 => GistErrorKind.Unavailable,
            >= 400 => GistErrorKind.Rejected,
            _ when stderr.Contains("gh auth login", StringComparison.OrdinalIgnoreCase) => GistErrorKind.NotSignedIn,
            _ => GistErrorKind.Unavailable,
        };
        return new GistApiException(kind, message);
    }

    [GeneratedRegex(@"HTTP (\d{3})")]
    private static partial Regex HttpStatus();
}
