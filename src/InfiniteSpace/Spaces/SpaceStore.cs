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

    /// <summary>Saves the spaces now, on this thread (see <see cref="Prepare"/> for saving without waiting on the disk).</summary>
    public void Save(IEnumerable<SpaceRecord> spaces) => Prepare(spaces).Write();

    /// <summary>
    /// Everything a save needs from the spaces, taken now on the thread that owns them: their JSON, and
    /// the text of every opened file (new ones get a name of their own). The returned save only touches
    /// the disk, so it can be written on any thread while the spaces change on.
    /// </summary>
    public PendingSave Prepare(IEnumerable<SpaceRecord> spaces)
    {
        var records = spaces.ToList();
        var texts = new List<(string Stored, string Text)>();
        foreach (var note in FileNotes(records))
        {
            var file = note.File!;
            file.Stored ??= NewStoredName(file);
            texts.Add((file.Stored, note.Text));
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new SpacesDocument { Spaces = records }, JsonOptions);
        return new PendingSave(this, json, texts);
    }

    /// <summary>A save taken by <see cref="Prepare"/>: the spaces' JSON and opened files' texts, ready for the disk.</summary>
    public sealed class PendingSave
    {
        private readonly SpaceStore _store;
        private readonly byte[] _json;
        private readonly List<(string Stored, string Text)> _texts;

        internal PendingSave(SpaceStore store, byte[] json, List<(string Stored, string Text)> texts)
        {
            _store = store;
            _json = json;
            _texts = texts;
        }

        /// <summary>
        /// Writes it, whole or not at all: opened files' texts that aren't on disk yet first, then spaces.json
        /// (written aside and moved into place), then stored texts no note needs any more go.
        /// </summary>
        public void Write()
        {
            Directory.CreateDirectory(_store.DataDirectory);
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (stored, text) in _texts)
            {
                if (!File.Exists(Path.Combine(_store.DataDirectory, stored)))
                    _store.WriteStored(stored, text);
                kept.Add(Path.GetFileName(stored));
            }

            string temp = _store.FilePath + ".tmp";
            File.WriteAllBytes(temp, _json);
            File.Move(temp, _store.FilePath, overwrite: true);
            _store.RemoveUnused(kept);
        }
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

    /// <summary>A name of its own for an opened file's text, relative to the data folder.</summary>
    private static string NewStoredName(NoteFile file) =>
        Path.Combine(NotesFolder, $"{Guid.NewGuid():N}{(file.Markdown ? ".md" : ".txt")}");

    /// <summary>Writes an opened file's text under its stored name, whole or not at all.</summary>
    private void WriteStored(string stored, string text)
    {
        string path = Path.Combine(DataDirectory, stored);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(path + ".tmp", path, overwrite: true);
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
