using System.Text;

namespace GistDrive;

/// <summary>
/// The text check (OPEN-7): GistDrive uploads UTF-8 text without NUL bytes, at most 1 MB.
/// </summary>
public static class TextFile
{
    public const int MaxBytes = 1_000_000;

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads a file and returns its text without a byte order mark, or the reason GistDrive
    /// cannot upload it. An empty file comes back as empty text, which callers treat as "not
    /// written yet" rather than as a failure.
    /// </summary>
    public static TextRead Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxBytes) return TextRead.Problem($"it is larger than 1 MB ({bytes.Length:N0} bytes)");
        if (Array.IndexOf(bytes, (byte)0) >= 0) return TextRead.Problem("it is not a text file (it contains NUL bytes)");

        try
        {
            var text = Strict.GetString(bytes);
            return TextRead.Ok(text.StartsWith('﻿') ? text[1..] : text);
        }
        catch (DecoderFallbackException)
        {
            return TextRead.Problem("it is not UTF-8 text");
        }
    }
}

/// <summary>
/// The result of the text check: the text, or the reason the file failed it.
/// </summary>
public readonly record struct TextRead(string? Text, string? Reason)
{
    public bool IsText => Reason is null;

    public static TextRead Ok(string text) => new(text, null);

    public static TextRead Problem(string reason) => new(null, reason);
}
