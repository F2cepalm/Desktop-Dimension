using System.Text;
using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace InfiniteSpace.Rendering;

/// <summary>What a paragraph of an opened file is: how it is laid out and decorated.</summary>
internal enum ParagraphKind
{
    Text,
    Heading,
    Code,
    Rule,
}

/// <summary>How a run of text in a paragraph is styled.</summary>
[Flags]
internal enum TextStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strike = 4,
    Code = 8,
    Link = 16,
}

/// <summary>A styled run: <see cref="Length"/> characters from <see cref="Start"/>; a link has the address it opens.</summary>
internal readonly record struct StyledRun(int Start, int Length, TextStyle Style, string? Url = null);

/// <summary>One paragraph of an opened file, before it is laid out.</summary>
/// <param name="Level">A heading's level (1–6); otherwise 0.</param>
/// <param name="Indent">How many list levels deep it is.</param>
/// <param name="Quote">How many quotes deep it is.</param>
/// <param name="Marker">What goes before it in the margin: a bullet, a number, a task's box.</param>
/// <param name="Tight">Whether it follows the one before closely (the next item of a list).</param>
internal sealed record NoteParagraph(
    ParagraphKind Kind,
    string Text,
    IReadOnlyList<StyledRun> Runs,
    int Level = 0,
    int Indent = 0,
    int Quote = 0,
    string? Marker = null,
    bool Tight = false);

/// <summary>Turns an opened file's text into paragraphs: Markdown styled, plain text as it is.</summary>
internal static class NoteParagraphs
{
    /// <summary>Plain text goes into paragraphs of this many lines, so a long file is laid out and drawn a piece at a time.</summary>
    private const int LinesPerPiece = 40;

    /// <summary>
    /// CommonMark with strikethrough, task lists and bare web addresses. Raw HTML stays text: nothing
    /// in a file is rendered as HTML, and no image is ever fetched.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static List<NoteParagraph> Plain(string text)
    {
        var paragraphs = new List<NoteParagraph>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i += LinesPerPiece)
        {
            string piece = string.Join('\n', lines, i, Math.Min(LinesPerPiece, lines.Length - i));
            paragraphs.Add(new NoteParagraph(ParagraphKind.Text, piece, [], Tight: i > 0));
        }
        return paragraphs;
    }

    public static List<NoteParagraph> Markdown(string text)
    {
        var paragraphs = new List<NoteParagraph>();
        var document = Markdig.Markdown.Parse(text, Pipeline);
        foreach (var block in document)
            AddBlock(paragraphs, block, indent: 0, quote: 0, marker: null, tight: false);
        return paragraphs;
    }

    /// <summary>Whether a link may be opened: only web addresses, nothing that runs or reads files.</summary>
    public static bool IsSafeLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void AddBlock(List<NoteParagraph> paragraphs, Block block, int indent, int quote, string? marker, bool tight)
    {
        switch (block)
        {
            case HeadingBlock heading:
            {
                var (text, runs) = Inlines(heading.Inline);
                paragraphs.Add(new NoteParagraph(ParagraphKind.Heading, text, runs, heading.Level, indent, quote, marker, tight));
                break;
            }
            case ParagraphBlock paragraph:
            {
                var (text, runs) = Inlines(paragraph.Inline);
                // A task list item's box leads its paragraph: it takes the bullet's place.
                if (paragraph.Inline?.FirstChild is TaskList task && marker is not null)
                    marker = task.Checked ? "☑" : "☐";
                paragraphs.Add(new NoteParagraph(ParagraphKind.Text, text.TrimStart(), ShiftRuns(runs, text.Length - text.TrimStart().Length), 0, indent, quote, marker, tight));
                break;
            }
            case FencedCodeBlock or CodeBlock:
            {
                string code = ((LeafBlock)block).Lines.ToString().TrimEnd('\n', '\r').Replace("\r\n", "\n");
                paragraphs.Add(new NoteParagraph(ParagraphKind.Code, code, [], 0, indent, quote, marker, tight));
                break;
            }
            case ThematicBreakBlock:
                paragraphs.Add(new NoteParagraph(ParagraphKind.Rule, "", [], 0, indent, quote, marker, tight));
                break;
            case QuoteBlock quoteBlock:
            {
                bool first = true;
                foreach (var child in quoteBlock)
                {
                    AddBlock(paragraphs, child, indent, quote + 1, first ? marker : null, first ? tight : false);
                    first = false;
                }
                break;
            }
            case ListBlock list:
            {
                int number = int.TryParse(list.OrderedStart, out int start) ? start : 1;
                bool firstItem = true;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    string bullet = list.IsOrdered ? $"{number++}." : indent switch { 0 => "•", 1 => "◦", _ => "▪" };
                    bool firstChild = true;
                    foreach (var child in item)
                    {
                        // The bullet goes with the item's first paragraph; later ones and nested lists hang
                        // under it. A tight list's items follow closely; the list itself keeps its distance.
                        bool closely = firstItem && firstChild ? tight : !list.IsLoose;
                        AddBlock(paragraphs, child, indent + 1, quote, firstChild ? bullet : null, closely);
                        firstChild = false;
                    }
                    firstItem = false;
                }
                break;
            }
            case HtmlBlock html: // not parsed as such with HTML off, but kept as text if it ever is
                paragraphs.Add(new NoteParagraph(ParagraphKind.Text, html.Lines.ToString(), [], 0, indent, quote, marker, tight));
                break;
            case ContainerBlock container:
                foreach (var child in container)
                    AddBlock(paragraphs, child, indent, quote, marker, tight);
                break;
            case LeafBlock leaf when leaf.Inline is not null:
            {
                var (text, runs) = Inlines(leaf.Inline);
                paragraphs.Add(new NoteParagraph(ParagraphKind.Text, text, runs, 0, indent, quote, marker, tight));
                break;
            }
        }
    }

    private static List<StyledRun> ShiftRuns(List<StyledRun> runs, int by)
    {
        if (by == 0)
            return runs;
        var shifted = new List<StyledRun>(runs.Count);
        foreach (var run in runs)
        {
            int start = Math.Max(0, run.Start - by);
            int end = Math.Max(start, run.Start + run.Length - by);
            if (end > start)
                shifted.Add(run with { Start = start, Length = end - start });
        }
        return shifted;
    }

    /// <summary>A paragraph's text as shown, with its styled runs.</summary>
    private static (string Text, List<StyledRun> Runs) Inlines(ContainerInline? inlines)
    {
        var text = new StringBuilder();
        var runs = new List<StyledRun>();
        if (inlines is not null)
            AddInlines(inlines, text, runs, TextStyle.None, null);
        return (text.ToString(), runs);
    }

    private static void AddInlines(ContainerInline container, StringBuilder text, List<StyledRun> runs, TextStyle style, string? url)
    {
        foreach (var inline in container)
        {
            int start = text.Length;
            switch (inline)
            {
                case TaskList:
                    continue; // shown as the item's marker
                case LiteralInline literal:
                    text.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    text.Append(code.Content);
                    runs.Add(new StyledRun(start, code.Content.Length, style | TextStyle.Code, url));
                    continue;
                case LineBreakInline lineBreak:
                    text.Append(lineBreak.IsHard ? '\n' : ' ');
                    continue;
                case HtmlEntityInline entity:
                    text.Append(entity.Transcoded.ToString());
                    break;
                case HtmlInline html:
                    text.Append(html.Tag); // raw HTML is only ever text
                    break;
                case AutolinkInline autolink:
                    text.Append(autolink.Url);
                    runs.Add(new StyledRun(start, autolink.Url.Length, style | TextStyle.Link, IsSafeLink(autolink.Url) ? autolink.Url : null));
                    continue;
                case LinkInline { IsImage: true } image:
                {
                    // Images are never fetched: their description stands in for them.
                    string alt = PlainText(image);
                    text.Append($"[image{(alt.Length > 0 ? ": " + alt : "")}]");
                    runs.Add(new StyledRun(start, text.Length - start, style | TextStyle.Italic, url));
                    continue;
                }
                case LinkInline link:
                {
                    string? target = IsSafeLink(link.Url) ? link.Url : null;
                    AddInlines(link, text, runs, style | TextStyle.Link, target);
                    if (link.FirstChild is null && link.Url is { Length: > 0 } address)
                        text.Append(address); // [](address): the address is all there is to show
                    if (text.Length > start)
                        runs.Add(new StyledRun(start, text.Length - start, style | TextStyle.Link, target));
                    continue;
                }
                case EmphasisInline emphasis:
                {
                    var added = emphasis.DelimiterChar == '~'
                        ? TextStyle.Strike
                        : emphasis.DelimiterCount >= 3 ? TextStyle.Bold | TextStyle.Italic
                        : emphasis.DelimiterCount == 2 ? TextStyle.Bold
                        : TextStyle.Italic;
                    AddInlines(emphasis, text, runs, style | added, url);
                    if (text.Length > start)
                        runs.Add(new StyledRun(start, text.Length - start, style | added, url));
                    continue;
                }
                case ContainerInline inner:
                    AddInlines(inner, text, runs, style, url);
                    continue;
            }
            // Plain text: styled by the emphasis or link around it, whose run covers it.
        }
    }

    private static string PlainText(ContainerInline container)
    {
        var text = new StringBuilder();
        foreach (var inline in container.Descendants<LiteralInline>())
            text.Append(inline.Content.ToString());
        return text.ToString();
    }
}
