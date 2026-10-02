using System.Globalization;
using System.IO;
using System.Text;

namespace InfiniteSpace.Spaces;

/// <summary>Reading the text and Markdown files that can be opened on a space.</summary>
internal static class TextFiles
{
    /// <summary>The biggest file opened: past this it would be a book, not a note.</summary>
    public const long MaxBytes = 1024 * 1024;

    private static readonly string[] Markdown = [".md", ".markdown", ".mdown", ".mkd"];
    private static readonly string[] Plain = [".txt", ".text", ".log"];

    static TextFiles() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Whether a file can be opened on a space (by its extension).</summary>
    public static bool IsSupported(string path) => IsMarkdown(path) || Plain.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsMarkdown(string path) => Markdown.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads a file's text.</summary>
    /// <exception cref="TextFileException">Too big, or it can't be read.</exception>
    public static string Read(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new TextFileException($"{Path.GetFileName(path)} is gone.");
            if (info.Length > MaxBytes)
                throw new TextFileException($"{info.Name} is {info.Length / 1024} KB; files up to {MaxBytes / 1024} KB can be opened on a space.");
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TextFileException($"{Path.GetFileName(path)} could not be read: {ex.Message}", ex);
        }
        if (bytes.Length > MaxBytes)
            throw new TextFileException($"{Path.GetFileName(path)} is over {MaxBytes / 1024} KB.");
        return Decode(bytes);
    }

    /// <summary>
    /// Text from a file's bytes: in the encoding its byte-order mark names (UTF-8, UTF-16, UTF-32),
    /// else UTF-8 if it is valid UTF-8, else the system's code page. Line breaks become "\n".
    /// </summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        string text;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            text = Encoding.UTF8.GetString(bytes[3..]);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
            text = new UTF32Encoding(bigEndian: false, byteOrderMark: true).GetString(bytes[4..]);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
            text = new UTF32Encoding(bigEndian: true, byteOrderMark: true).GetString(bytes[4..]);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            text = Encoding.Unicode.GetString(bytes[2..]);
        else if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
        else
        {
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
            }
        }
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "");
    }
}

/// <summary>A file that can't be opened on a space; the message says why, for the user.</summary>
internal sealed class TextFileException(string message, Exception? inner = null) : Exception(message, inner);
