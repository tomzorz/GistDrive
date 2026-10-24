using System.Text;

namespace GistDrive;

/// <summary>
/// The text check (REQ-UP-12): GistDrive uploads text without NUL characters, at most 1 MB once
/// encoded as UTF-8, which is what GitHub stores. A file is UTF-8, or UTF-16 with a byte order
/// mark as Windows PowerShell 5.1 and Notepad's Unicode option write it; UTF-16 text goes up as
/// UTF-8.
/// </summary>
public static class TextFile
{
    public const int MaxBytes = 1_000_000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads a file and returns its text without a byte order mark, or the reason GistDrive
    /// cannot upload it. An empty file comes back as empty text, which callers treat as "not
    /// written yet" rather than as a failure.
    /// </summary>
    public static TextRead Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return bytes switch
        {
            [0xFF, 0xFE, ..] => ReadUtf16(bytes, StrictUtf16LittleEndian),
            [0xFE, 0xFF, ..] => ReadUtf16(bytes, StrictUtf16BigEndian),
            _ => ReadUtf8(bytes),
        };
    }

    private static TextRead ReadUtf8(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) return TooLarge(bytes.Length);
        if (Array.IndexOf(bytes, (byte)0) >= 0) return NotText;

        try
        {
            var text = StrictUtf8.GetString(bytes);
            return TextRead.Ok(text.StartsWith('﻿') ? text[1..] : text);
        }
        catch (DecoderFallbackException)
        {
            return TextRead.Problem("it is not UTF-8 text");
        }
    }

    // UTF-32 starts with the same two bytes as UTF-16 little endian, and fails here on its NULs.
    private static TextRead ReadUtf16(byte[] bytes, UnicodeEncoding encoding)
    {
        string text;
        try
        {
            text = encoding.GetString(bytes, 2, bytes.Length - 2);
        }
        catch (DecoderFallbackException)
        {
            return TextRead.Problem("it is not valid UTF-16 text");
        }

        if (text.Contains('\0')) return NotText;
        var size = Encoding.UTF8.GetByteCount(text);
        return size > MaxBytes ? TooLarge(size) : TextRead.Ok(text);
    }

    private static TextRead NotText => TextRead.Problem("it is not a text file (it contains NUL bytes)");

    private static TextRead TooLarge(int bytes) => TextRead.Problem($"it is larger than 1 MB ({bytes:N0} bytes as UTF-8)");
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
