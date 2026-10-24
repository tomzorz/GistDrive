using System.Text;

namespace GistDrive;

/// <summary>
/// The content of a gist shortcut, a .url file. File Explorer reads the [InternetShortcut]
/// section; GistDrive keeps its own state in the [GistDrive] section (REQ-LAY-9, REQ-LAY-11).
/// </summary>
public sealed record GistShortcut(string GistId, string FileName, string Url, string? SourcePath = null)
{
    private const string OwnSection = "GistDrive";

    /// <summary>
    /// The text of the .url file. Values in the GistDrive section are percent-encoded so the
    /// whole file stays ASCII, which the shell's own .url reader and writer handle safely.
    /// </summary>
    public string Render()
    {
        var text = new StringBuilder()
            .Append("[InternetShortcut]\r\n")
            .Append($"URL={Url}\r\n")
            .Append($"[{OwnSection}]\r\n")
            .Append($"Id={Uri.EscapeDataString(GistId)}\r\n")
            .Append($"File={Uri.EscapeDataString(FileName)}\r\n");
        if (SourcePath is not null) text.Append($"Source={Uri.EscapeDataString(SourcePath)}\r\n");
        return text.ToString();
    }

    public void Write(string path) => File.WriteAllText(path, Render(), Encoding.ASCII);

    /// <summary>
    /// Reads a gist shortcut. Returns null when the file is missing or unreadable, or when it
    /// is a .url file that GistDrive did not write.
    /// </summary>
    public static GistShortcut? TryRead(string path)
    {
        try
        {
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the lines of a .url file, ignoring the sections and keys that Windows adds.
    /// </summary>
    public static GistShortcut? Parse(IEnumerable<string> lines)
    {
        var section = string.Empty;
        string? url = null, id = null, file = null, source = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;

            var key = line[..equals];
            var value = line[(equals + 1)..];
            if (section.Equals("InternetShortcut", StringComparison.OrdinalIgnoreCase) && key.Equals("URL", StringComparison.OrdinalIgnoreCase)) url = value;
            if (!section.Equals(OwnSection, StringComparison.OrdinalIgnoreCase)) continue;

            switch (key.ToUpperInvariant())
            {
                case "ID": id = Uri.UnescapeDataString(value); break;
                case "FILE": file = Uri.UnescapeDataString(value); break;
                case "SOURCE": source = Uri.UnescapeDataString(value); break;
            }
        }

        return url is not null && id is not null && file is not null ? new GistShortcut(id, file, url, source) : null;
    }
}
