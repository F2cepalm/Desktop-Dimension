using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Rendering;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace InfiniteSpace.Tests;

/// <summary>Text and Markdown files opened on spaces: reading them, Markdown, storing and drawing them.</summary>
public sealed class FileNoteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "InfiniteSpace.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Headings_AreHeadingsOfTheirLevel()
    {
        var paragraphs = NoteParagraphs.Markdown("# One\n\n### Three\n\nText");

        Assert.Equal([(ParagraphKind.Heading, 1, "One"), (ParagraphKind.Heading, 3, "Three"), (ParagraphKind.Text, 0, "Text")],
            paragraphs.Select(p => (p.Kind, p.Level, p.Text)));
    }

    [Fact]
    public void Emphasis_CodeAndStrikethrough_AreStyledRuns()
    {
        var paragraph = Assert.Single(NoteParagraphs.Markdown("a **bold** b *it* c `code` d ~~gone~~"));

        Assert.Equal("a bold b it c code d gone", paragraph.Text);
        Assert.Contains(paragraph.Runs, r => Covers(paragraph, r, "bold") && r.Style.HasFlag(TextStyle.Bold));
        Assert.Contains(paragraph.Runs, r => Covers(paragraph, r, "it") && r.Style.HasFlag(TextStyle.Italic));
        Assert.Contains(paragraph.Runs, r => Covers(paragraph, r, "code") && r.Style.HasFlag(TextStyle.Code));
        Assert.Contains(paragraph.Runs, r => Covers(paragraph, r, "gone") && r.Style.HasFlag(TextStyle.Strike));
    }

    [Fact]
    public void RawHtml_StaysText()
    {
        var paragraphs = NoteParagraphs.Markdown("Hello <b>there</b>\n\n<script>alert(1)</script>");

        Assert.Equal("Hello <b>there</b>", paragraphs[0].Text);
        Assert.Contains("<script>alert(1)</script>", string.Join("\n", paragraphs.Select(p => p.Text)));
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("relative/page.md", false)]
    public void OnlyWebLinks_CanBeOpened(string url, bool opens)
    {
        var paragraph = Assert.Single(NoteParagraphs.Markdown($"[go]({url})"));
        var link = Assert.Single(paragraph.Runs, r => r.Style.HasFlag(TextStyle.Link));

        Assert.Equal(opens ? url : null, link.Url);
    }

    [Fact]
    public void Images_AreNeverFetched_TheirDescriptionShows()
    {
        var paragraph = Assert.Single(NoteParagraphs.Markdown("![a cat](https://example.com/cat.png)"));

        Assert.Equal("[image: a cat]", paragraph.Text);
        Assert.DoesNotContain(paragraph.Runs, r => r.Url is not null);
    }

    [Fact]
    public void Lists_HaveMarkers_TasksHaveBoxes_NestedOnesGoDeeper()
    {
        var paragraphs = NoteParagraphs.Markdown("- one\n  - inner\n- [x] done\n- [ ] open\n\n3. three\n4. four");

        Assert.Equal(
            [("•", 1, "one"), ("◦", 2, "inner"), ("☑", 1, "done"), ("☐", 1, "open"), ("3.", 1, "three"), ("4.", 1, "four")],
            paragraphs.Select(p => (p.Marker, p.Indent, p.Text)));
    }

    [Fact]
    public void QuotesAndCode_AreSetApart()
    {
        var paragraphs = NoteParagraphs.Markdown("> quoted\n\n```\ncode line\n```\n\n---");

        Assert.Equal([(ParagraphKind.Text, 1, "quoted"), (ParagraphKind.Code, 0, "code line"), (ParagraphKind.Rule, 0, "")],
            paragraphs.Select(p => (p.Kind, p.Quote, p.Text)));
    }

    [Fact]
    public void PlainText_ComesInPiecesOfLines_ThatFollowOn()
    {
        string text = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"line {i}"));

        var paragraphs = NoteParagraphs.Plain(text);

        Assert.Equal(text, string.Join("\n", paragraphs.Select(p => p.Text)));
        Assert.True(paragraphs.Count > 1);
        Assert.All(paragraphs.Skip(1), p => Assert.True(p.Tight));
    }

    [Fact]
    public void Files_AreReadInTheEncodingTheirMarkSays()
    {
        const string text = "Grüße, мир, 世界";
        byte[] Marked(Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

        Assert.Equal(text, TextFiles.Decode(Marked(new UTF8Encoding(true))));
        Assert.Equal(text, TextFiles.Decode(Marked(Encoding.Unicode)));
        Assert.Equal(text, TextFiles.Decode(Marked(Encoding.BigEndianUnicode)));
        Assert.Equal(text, TextFiles.Decode(Marked(new UTF32Encoding(false, true))));
        Assert.Equal(text, TextFiles.Decode(Encoding.UTF8.GetBytes(text))); // no mark: UTF-8
    }

    [Fact]
    public void FilesThatArentUtf8_AreReadInTheSystemsCodePage()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        byte[] bytes = [0x63, 0x61, 0x66, 0xE9]; // "café" in Windows-1252, not valid UTF-8

        Assert.Equal(ansi.GetString(bytes), TextFiles.Decode(bytes));
    }

    [Fact]
    public void LineBreaks_AllBecomeOne()
    {
        Assert.Equal("a\nb\nc\nd", TextFiles.Decode(Encoding.UTF8.GetBytes("a\r\nb\rc\nd")));
    }

    [Fact]
    public void FilesOverAMegabyte_AreNotOpened()
    {
        Directory.CreateDirectory(_directory);
        string big = Path.Combine(_directory, "big.txt");
        File.WriteAllBytes(big, new byte[TextFiles.MaxBytes + 1]);
        string fine = Path.Combine(_directory, "fine.md");
        File.WriteAllText(fine, "# ok");

        var refused = Assert.Throws<TextFileException>(() => TextFiles.Read(big));
        Assert.Contains("big.txt", refused.Message);
        Assert.Equal("# ok", TextFiles.Read(fine));
        Assert.True(TextFiles.IsMarkdown(fine) && TextFiles.IsSupported(big) && !TextFiles.IsSupported("photo.png"));
    }

    [Fact]
    public void AnOpenedFilesText_IsKeptInAFileOfItsOwn_NotInSpacesJson()
    {
        var store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);
        string text = "# Notes\n\n" + new string('x', 5000);
        var note = new TextNote { Text = text, File = new NoteFile { Path = @"C:\docs\notes.md", Markdown = true, Height = 300, Scroll = 40 } };

        store.Save([new SpaceRecord { Figures = [[0, 0, 10, 0, 10, 10]], Notes = [note] }]);

        string json = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain(new string('x', 100), json);
        Assert.NotNull(note.File.Stored);
        Assert.Equal(text, File.ReadAllText(Path.Combine(_directory, note.File.Stored!)));

        var restored = Assert.Single(Assert.Single(store.Load()).Notes);
        Assert.Equal(text, restored.Text);
        Assert.Equal((@"C:\docs\notes.md", true, 300.0, 40.0), (restored.File!.Path, restored.File.Markdown, restored.File.Height, restored.File.Scroll));
    }

    [Fact]
    public void StoredTexts_NoNoteNeedsAnyMore_AreRemoved_EarlierVersionsKeepTheirs()
    {
        var store = new SpaceStore(_directory, NullLogger<SpaceStore>.Instance);
        var first = new TextNote { Text = "first", Order = 1, File = new NoteFile { Path = "a.txt" } };
        var gone = new TextNote { Text = "gone", Order = 2, File = new NoteFile { Path = "b.txt" } };
        var space = new SpaceRecord { Figures = [[0, 0, 10, 0, 10, 10]], Notes = [first, gone] };
        store.Save([space]);
        string goneFile = Path.Combine(_directory, gone.File!.Stored!);

        space.Notes.Remove(gone);                                   // removed for good
        var edited = new TextNote { Text = "edited", Order = 3, File = first.File!.ForNewText() };
        space.Replace(first, edited);                               // edited: undo brings "first" back
        store.Save([space]);

        Assert.False(File.Exists(goneFile));
        Assert.True(File.Exists(Path.Combine(_directory, first.File.Stored!)));
        Assert.True(File.Exists(Path.Combine(_directory, edited.File!.Stored!)));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_directory, SpaceStore.NotesFolder)).Length);
        var restored = Assert.Single(Assert.Single(store.Load()).Notes);
        Assert.Equal(("edited", "first"), (restored.Text, restored.Previous!.Text));
    }

    [Fact]
    public void AnOpenedFile_TakesUpItsWidthAndTheHeightItShows()
    {
        var note = new TextNote { X = 10, Y = 20, Text = "long\n" + string.Join("\n", Enumerable.Range(0, 200)), Width = 300, File = new NoteFile { Path = "a.txt", Height = 250 } };

        Assert.Equal(new Rect(10, 20, 300, 250), NoteText.Bounds(note, Matrix.Identity));
        Assert.True(NoteText.MaxScroll(note) > 200 * 16 * 1.2 - 250);
    }

    [Fact]
    public void AnOpenedFile_IsDrawnWithinTheHeightItShows()
    {
        Gpu.Run(gpu =>
        {
            var square = SpaceGeometry.FromStroke([new(0, 0), new(600, 0), new(600, 600), new(0, 600)], new Vector());
            using var ink = new InkLayer(gpu, 600, 600, 1, new Point(0, 0), square, Colors.White);
            var note = new TextNote
            {
                X = 50,
                Y = 50,
                Text = string.Join("\n", Enumerable.Range(0, 200).Select(i => "WWWWWWWW")),
                Width = 300,
                File = new NoteFile { Path = "a.txt", Height = 200 },
            };

            ink.Redraw(new SpaceRecord { Notes = [note] });
            var pixels = ink.ReadPixels();

            Assert.Contains(Row(pixels, 600, 150, 60, 300), p => p != 0);  // text in the note
            Assert.All(Row(pixels, 600, 280, 60, 300), p => Assert.Equal(0, p)); // nothing past what it shows
        });
    }

    [Fact]
    public void ScrollingAnOpenedFile_ShowsWhatWasFurtherDown()
    {
        Gpu.Run(gpu =>
        {
            var square = SpaceGeometry.FromStroke([new(0, 0), new(600, 0), new(600, 600), new(0, 600)], new Vector());
            using var ink = new InkLayer(gpu, 600, 600, 1, new Point(0, 0), square, Colors.White);
            // The first lines are blank: only scrolling brings text into view.
            var note = new TextNote
            {
                X = 50,
                Y = 50,
                Text = "top\n" + string.Concat(Enumerable.Repeat("\n", 60)) + string.Join("\n", Enumerable.Range(0, 60).Select(i => "WWWWWWWW")),
                Width = 300,
                File = new NoteFile { Path = "a.txt", Height = 200 },
            };
            var record = new SpaceRecord { Notes = [note] };

            ink.Redraw(record);
            var before = ink.ReadPixels();
            note.File.Scroll = NoteText.MaxScroll(note);
            ink.Redraw(record);
            var after = ink.ReadPixels();

            // A band as tall as a line, so it can't fall between lines.
            var rows = Enumerable.Range(170, 24);
            Assert.DoesNotContain(rows.SelectMany(y => Row(before, 600, y, 60, 300)), p => (p >>> 24) > 0x90);
            Assert.Contains(rows.SelectMany(y => Row(after, 600, y, 60, 300)), p => (p >>> 24) > 0x90);
        });
    }

    [Fact]
    public void ALinkIsFoundUnderTheMouse_AndTheNameAtTheTop()
    {
        var note = new TextNote { Text = "[open me](https://example.com) and more words after it", Width = 400, File = new NoteFile { Path = @"C:\a\read.md", Markdown = true, Height = 200 } };
        using var document = NoteDocument.Build(note);
        double firstLine = document.HeaderHeight + note.FontSize * NoteText.LineSpacing / 2;

        Assert.Equal("https://example.com", document.LinkAt(new Point(10, firstLine), scroll: 0));
        Assert.Null(document.LinkAt(new Point(300, firstLine), scroll: 0));   // past the link, on plain words
        Assert.Null(document.LinkAt(new Point(10, firstLine), scroll: 100));  // scrolled away
        Assert.True(document.IsOnHeader(new Point(20, document.HeaderHeight / 2)));
        Assert.False(document.IsOnHeader(new Point(20, firstLine)));
    }

    private static bool Covers(NoteParagraph paragraph, StyledRun run, string text) =>
        paragraph.Text.Substring(run.Start, run.Length) == text;

    private static int[] Row(int[] pixels, int width, int y, int from, int to) => pixels[(y * width + from)..(y * width + to)];
}
