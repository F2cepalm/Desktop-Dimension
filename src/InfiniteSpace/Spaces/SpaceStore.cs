using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace InfiniteSpace.Spaces;

/// <summary>Keeps spaces in a JSON file so they survive restarts until erased.</summary>
/// <remarks>
/// Text of files opened on spaces (see <see cref="NoteFile"/>) is kept in files of its own under
/// <c>notes\</c>, each written once: spaces.json only names them, so writing it out after every change
/// stays quick however long the files are. Stored texts no note needs any more are removed on saving.
/// </remarks>
internal sealed class SpaceStore(string dataDirectory, ILogger<SpaceStore> logger)
{
    /// <summary>The folder (in the data folder) where opened files' text is kept.</summary>
    public const string NotesFolder = "notes";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutStoredText } },
    };

    public string DataDirectory { get; } = dataDirectory;

    public string FilePath { get; } = Path.Combine(dataDirectory, "spaces.json");

    public List<SpaceRecord> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        List<SpaceRecord> records;
        try
        {
            using var stream = File.OpenRead(FilePath);
            records = JsonSerializer.Deserialize<SpacesDocument>(stream, JsonOptions)?.Spaces ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            logger.LogError(ex, "Could not read {File}; keeping a copy and starting empty", FilePath);
            try { File.Copy(FilePath, FilePath + ".corrupt", overwrite: true); } catch (IOException) { }
            return [];
        }

        foreach (var note in FileNotes(records))
            note.Text = ReadStored(note.File!);
        return records;
    }

    public void Save(IEnumerable<SpaceRecord> spaces)
    {
        Directory.CreateDirectory(DataDirectory);
        var records = spaces.ToList();
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in FileNotes(records))
        {
            var file = note.File!;
            if (file.Stored is null || !File.Exists(Path.Combine(DataDirectory, file.Stored)))
                file.Stored = WriteStored(note.Text, file);
            kept.Add(Path.GetFileName(file.Stored));
        }

        string temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
            JsonSerializer.Serialize(stream, new SpacesDocument { Spaces = records }, JsonOptions);
        File.Move(temp, FilePath, overwrite: true);
        RemoveUnused(kept);
    }

    /// <summary>Every note opened from a file, with every earlier version of it (for undo).</summary>
    private static IEnumerable<TextNote> FileNotes(IEnumerable<SpaceRecord> records)
    {
        foreach (var record in records)
        {
            foreach (var note in record.Notes)
            {
                for (var version = note; version is not null; version = version.Previous)
                {
                    if (version.File is not null)
                        yield return version;
                }
            }
        }
    }

    /// <summary>Writes an opened file's text once, under a name of its own, whole or not at all.</summary>
    /// <returns>Where it went, relative to the data folder.</returns>
    private string WriteStored(string text, NoteFile file)
    {
        string folder = Path.Combine(DataDirectory, NotesFolder);
        Directory.CreateDirectory(folder);
        string name = $"{Guid.NewGuid():N}{(file.Markdown ? ".md" : ".txt")}";
        string path = Path.Combine(folder, name);
        File.WriteAllText(path + ".tmp", text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(path + ".tmp", path, overwrite: true);
        return Path.Combine(NotesFolder, name);
    }

    private string ReadStored(NoteFile file)
    {
        try
        {
            if (file.Stored is { Length: > 0 } stored)
                return File.ReadAllText(Path.Combine(DataDirectory, stored), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not read the text of {File}", file.Path);
        }
        file.Stored = null; // written again from what is shown
        return $"({Path.GetFileName(file.Path)} could not be read back.)";
    }

    /// <summary>Stored texts no note (nor an earlier version of one) needs any more.</summary>
    private void RemoveUnused(HashSet<string> kept)
    {
        string folder = Path.Combine(DataDirectory, NotesFolder);
        if (!Directory.Exists(folder))
            return;
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            if (kept.Contains(Path.GetFileName(path)))
                continue;
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove {File}", path);
            }
        }
    }

    /// <summary>A note opened from a file is saved without its text, which is kept in a file of its own.</summary>
    private static void LeaveOutStoredText(JsonTypeInfo type)
    {
        if (type.Type != typeof(TextNote))
            return;
        foreach (var property in type.Properties)
        {
            if (property.Name == nameof(TextNote.Text))
                property.ShouldSerialize = (note, _) => ((TextNote)note).File is null;
        }
    }
}
