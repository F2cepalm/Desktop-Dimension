using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace InfiniteSpace.Spaces;

/// <summary>
/// A space saved to a file of its own (.spsf): UTF-8 JSON naming this app, holding the space as shown,
/// its top-left at (0, 0), with every note's text inside (opened files too) and no undo history.
/// </summary>
internal static class SpaceFile
{
    /// <summary>The extension saved spaces have.</summary>
    public const string Extension = ".spsf";

    /// <summary>What the file says it is: anything else is refused.</summary>
    public const string Format = "InfiniteSpace.Space";

    /// <summary>The newest layout this version reads (and the one it writes).</summary>
    public const int Version = 1;

    /// <summary>The Save and Open dialogs' file type.</summary>
    public const string Filter = "Desktop_Dimension (*.spsf)|*.spsf";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    /// <summary>The folder saves go to unless another was picked: one of this app's own, in Documents.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Desktop_Dimension", "Saved spaces");

    /// <summary>
    /// A copy of <paramref name="shown"/> (in screen DIPs) ready to save: its top-left at (0, 0), on no app,
    /// without undo history, and with opened files' text kept inline rather than in the app's own folder.
    /// </summary>
    public static SpaceRecord Snapshot(SpaceRecord shown)
    {
        var copy = JsonSerializer.Deserialize<SpaceRecord>(JsonSerializer.Serialize(shown, JsonOptions), JsonOptions)!;
        copy.Host = null;
        foreach (var stroke in copy.Strokes)
            stroke.Previous = null;
        foreach (var note in copy.Notes)
        {
            note.Previous = null;
            if (note.File is { } file)
                file.Stored = null;
        }
        var bounds = SpaceGeometry.Decode(copy).Bounds;
        if (!bounds.IsEmpty)
            copy.Transform(new Matrix(1, 0, 0, 1, -bounds.Left, -bounds.Top));
        return copy;
    }

    /// <summary>Writes a snapshot (see <see cref="Snapshot"/>).</summary>
    public static void Write(Stream stream, SpaceRecord snapshot, string theme) =>
        JsonSerializer.Serialize(stream, new Document { Format = Format, Version = Version, SavedAt = DateTimeOffset.Now, Theme = theme, Space = snapshot }, JsonOptions);

    /// <summary>Reads a saved space, ready to be placed: a new id, its top-left at (0, 0).</summary>
    /// <exception cref="SpaceFileException">It isn't a saved space, or is from a newer version.</exception>
    public static SpaceRecord Read(Stream stream)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or DecoderFallbackException)
        {
            throw new SpaceFileException("This isn't a saved Desktop_Dimension space.", ex);
        }
        if (document?.Format != Format || document.Space is not { Figures.Count: > 0 } space)
            throw new SpaceFileException("This isn't a saved Desktop_Dimension space.");
        if (document.Version > Version)
            throw new SpaceFileException("This space was saved by a newer Desktop_Dimension. Update the app to open it.");
        space.Id = Guid.NewGuid();
        space.Kind = SpaceKind.Space;
        space.Host = null;
        return space;
    }

    /// <summary>A file name for a space: its first line of text, or the time, without characters names can't hold.</summary>
    public static string SuggestedName(SpaceRecord space, DateTime now)
    {
        string? line = space.Notes.Where(n => n.Text.Trim().Length > 0).OrderBy(n => n.Y).ThenBy(n => n.X)
            .Select(n => n.Text.Trim().Split('\n')[0].Trim()).FirstOrDefault();
        if (line is { Length: > 0 })
        {
            var invalid = Path.GetInvalidFileNameChars();
            string clean = new string([.. line.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c)]);
            clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length > 40)
                clean = clean[..40].TrimEnd();
            clean = clean.TrimEnd('.', ' ');
            if (clean.Length > 0)
                return clean;
        }
        return $"Space {now:yyyy-MM-dd HH-mm}";
    }

    /// <summary>The file as written: what it is, then the space.</summary>
    private sealed class Document
    {
        /// <summary>Always <see cref="SpaceFile.Format"/>.</summary>
        public string? Format { get; set; }

        /// <summary>The layout it was written in (see <see cref="SpaceFile.Version"/>).</summary>
        public int Version { get; set; }

        /// <summary>When it was saved.</summary>
        public DateTimeOffset SavedAt { get; set; }

        /// <summary>The theme it was saved in, for information.</summary>
        public string? Theme { get; set; }

        /// <summary>The space, its top-left at (0, 0).</summary>
        public SpaceRecord? Space { get; set; }
    }
}

/// <summary>A file that can't be opened as a saved space; the message says why, for the user.</summary>
internal sealed class SpaceFileException(string message, Exception? inner = null) : Exception(message, inner);
