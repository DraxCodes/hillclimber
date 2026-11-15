using System.Text;
using System.Text.RegularExpressions;

namespace ClimberWeb.Services;

/// <summary>One rendered markdown block. Blocks never carry raw HTML.</summary>
public abstract record MarkdownBlock;

/// <summary>A run of inline content separated from its neighbours by a blank line.</summary>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A <c>#</c> heading. Levels outside 1–6 are clamped by the parser.</summary>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>A bullet or numbered list. Items are flat; nested lists are not modelled.</summary>
public sealed record MarkdownListBlock(bool Ordered, IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;

/// <summary>One list item's inline content.</summary>
public sealed record MarkdownListItem(IReadOnlyList<MarkdownInline> Inlines);

/// <summary>A fenced code block, with its info string when the fence carried one.</summary>
public sealed record MarkdownCodeBlock(string Code, string? Language) : MarkdownBlock;

/// <summary>A <c>&gt;</c> quote, parsed recursively so it can hold paragraphs and lists.</summary>
public sealed record MarkdownQuoteBlock(IReadOnlyList<MarkdownBlock> Children) : MarkdownBlock;

/// <summary>A pipe table. Every row carries the header's column count.</summary>
public sealed record MarkdownTableBlock(
    IReadOnlyList<MarkdownTableCell> Header,
    IReadOnlyList<IReadOnlyList<MarkdownTableCell>> Rows) : MarkdownBlock;

/// <summary>One table cell. <paramref name="Align"/> comes from the delimiter row's colons.</summary>
public sealed record MarkdownTableCell(IReadOnlyList<MarkdownInline> Inlines, MarkdownTableAlign Align);

/// <summary>The alignment a table column asked for; <see cref="None"/> renders as the default left.</summary>
public enum MarkdownTableAlign
{
    None,
    Left,
    Center,
    Right,
}

/// <summary>A horizontal rule (<c>---</c>, <c>***</c>, <c>___</c>).</summary>
public sealed record MarkdownBreak : MarkdownBlock;

/// <summary>One inline run. Inline content is a tree so emphasis can contain code and links.</summary>
public abstract record MarkdownInline;

/// <summary>Literal text. Razor encodes it; there is no HTML sink.</summary>
public sealed record MarkdownText(string Text) : MarkdownInline;

/// <summary>A backtick code span.</summary>
public sealed record MarkdownCodeSpan(string Text) : MarkdownInline;

/// <summary><c>**strong**</c> or <c>__strong__</c>.</summary>
public sealed record MarkdownStrong(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary><c>*emphasis*</c> or <c>_emphasis_</c>.</summary>
public sealed record MarkdownEmphasis(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary>A link that passed the scheme check. Unsafe targets never become links.</summary>
public sealed record MarkdownLink(IReadOnlyList<MarkdownInline> Children, string Href) : MarkdownInline;

/// <summary>
/// Parses the small markdown subset opencode answers actually use — paragraphs, headings,
/// bullet and numbered lists, fenced code, quotes, pipe tables, bold, italic, inline code
/// and links —
/// into display records. Pure, so the renderer can be a thin Razor component and the tests
/// can assert every shape without a browser. It never produces raw HTML: the only HTML in
/// the output is written by Razor markup, and a link is emitted only when its target is
/// http(s), mailto, a single-slash root path, a fragment, or a colon-free relative path.
/// An unterminated fence keeps the rest of the message as code, so streaming text never
/// flashes raw backticks.
/// </summary>
public static class MarkdownParser
{
    private static readonly Regex Heading = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedItem = new(@"^(\d{1,9})[.)]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex Rule = new(@"^ {0,3}([-*_])( *\1){2,} *$", RegexOptions.Compiled);
    private static readonly Regex DelimiterCell = new(@"^:?-+:?$", RegexOptions.Compiled);

    /// <summary>Parses <paramref name="text"/> into blocks. Blank input yields no blocks.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<MarkdownBlock>();
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MarkdownBlock>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (TryFence(line, out var fence, out var fenceLength, out var language))
            {
                var code = new StringBuilder();
                index++;
                while (index < lines.Length && !IsFenceClose(lines[index], fence, fenceLength))
                {
                    if (code.Length > 0)
                    {
                        code.Append('\n');
                    }

                    code.Append(lines[index]);
                    index++;
                }

                // An unterminated fence consumes the rest of the message: that is what a
                // streaming answer looks like while the closing fence has not arrived yet.
                if (index < lines.Length)
                {
                    index++;
                }

                blocks.Add(new MarkdownCodeBlock(code.ToString(), language));
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                blocks.Add(new MarkdownHeading(level, ParseInline(heading.Groups[2].Value.Trim())));
                index++;
                continue;
            }

            if (Rule.IsMatch(line))
            {
                blocks.Add(new MarkdownBreak());
                index++;
                continue;
            }

            if (TryListItem(line, out var ordered, out var item))
            {
                var itemTexts = new List<string> { item };
                index++;
                while (index < lines.Length)
                {
                    var next = lines[index];
                    if (string.IsNullOrWhiteSpace(next))
                    {
                        break;
                    }

                    if (TryListItem(next, out var nextOrdered, out var nextItem) && nextOrdered == ordered)
                    {
                        itemTexts.Add(nextItem);
                        index++;
                        continue;
                    }

                    // An indented continuation belongs to the item above it. A new block
                    // (heading, fence, quote) ends the list instead of being swallowed.
                    if (IsContinuation(next) && !StartsBlock(next))
                    {
                        itemTexts[^1] = string.Concat(itemTexts[^1], "\n", next.Trim());
                        index++;
                        continue;
                    }

                    break;
                }

                blocks.Add(new MarkdownListBlock(
                    ordered,
                    itemTexts.Select(value => new MarkdownListItem(ParseInline(value))).ToArray()));
                continue;
            }

            if (IsQuote(line))
            {
                var quoted = new List<string>();
                while (index < lines.Length && IsQuote(lines[index]))
                {
                    quoted.Add(StripQuote(lines[index]));
                    index++;
                }

                blocks.Add(new MarkdownQuoteBlock(Parse(string.Join("\n", quoted))));
                continue;
            }

            if (TryTable(lines, index, out var table, out var tableLength))
            {
                blocks.Add(table);
                index += tableLength;
                continue;
            }

            var paragraph = new List<string>();
            while (index < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[index])
                   && !StartsBlock(lines[index])
                   && !TryTable(lines, index, out _, out _))
            {
                paragraph.Add(lines[index]);
                index++;
            }

            // A line that stops the paragraph because it starts a block must be seen again
            // by the outer loop; otherwise the block would be swallowed by the paragraph.
            if (paragraph.Count == 0)
            {
                paragraph.Add(lines[index]);
                index++;
            }

            blocks.Add(new MarkdownParagraph(ParseInline(string.Join("\n", paragraph).Trim())));
        }

        return blocks;
    }

    /// <summary>Parses an inline run. Exposed so tests can assert emphasis and link rules.</summary>
    public static IReadOnlyList<MarkdownInline> ParseInline(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var runs = new List<MarkdownInline>();
        var buffer = new StringBuilder();

        void Flush()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            runs.Add(new MarkdownText(buffer.ToString()));
            buffer.Clear();
        }

        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];

            if (ch == '\\' && index + 1 < text.Length && IsEscapable(text[index + 1]))
            {
                buffer.Append(text[index + 1]);
                index += 2;
                continue;
            }

            if (ch == '`' && TryCodeSpan(text, index, out var code, out var codeLength))
            {
                Flush();
                runs.Add(code);
                index += codeLength;
                continue;
            }

            if ((ch is '*' or '_') && TryEmphasis(text, index, out var emphasis, out var emphasisLength))
            {
                Flush();
                runs.Add(emphasis);
                index += emphasisLength;
                continue;
            }

            if (ch == '[' && TryLink(text, index, out var link, out var linkLength))
            {
                Flush();
                runs.Add(link);
                index += linkLength;
                continue;
            }

            buffer.Append(ch);
            index++;
        }

        Flush();
        return runs;
    }

    private static bool StartsBlock(string line) =>
        TryFence(line, out _, out _, out _)
        || Heading.IsMatch(line)
        || Rule.IsMatch(line)
        || TryListItem(line, out _, out _)
        || IsQuote(line);

    private static bool TryFence(string line, out char marker, out int length, out string? language)
    {
        marker = '\0';
        length = 0;
        language = null;

        var trimmed = line.TrimStart();
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~'))
        {
            return false;
        }

        marker = trimmed[0];
        while (length < trimmed.Length && trimmed[length] == marker)
        {
            length++;
        }

        if (length < 3)
        {
            return false;
        }

        var info = trimmed[length..].Trim();
        language = info.Length == 0 ? null : info;
        return true;
    }

    private static bool IsFenceClose(string line, char marker, int length)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < length)
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (ch != marker)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryListItem(string line, out bool ordered, out string content)
    {
        ordered = false;
        content = string.Empty;

        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        // Four or more spaces is a continuation, not a new top-level item.
        if (indent > 3 || indent >= line.Length)
        {
            return false;
        }

        var body = line[indent..];
        if (body.Length >= 2 && (body[0] is '-' or '*' or '+') && body[1] == ' ')
        {
            var text = body[2..].Trim();
            if (text.Length == 0)
            {
                return false;
            }

            content = text;
            return true;
        }

        var orderedMatch = OrderedItem.Match(body);
        if (!orderedMatch.Success)
        {
            return false;
        }

        ordered = true;
        content = orderedMatch.Groups[2].Value.Trim();
        return content.Length > 0;
    }

    private static bool IsContinuation(string line) => line.StartsWith(' ') || line.StartsWith('\t');

    private static bool IsQuote(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length > 0 && trimmed[0] == '>';
    }

    private static string StripQuote(string line)
    {
        var trimmed = line.TrimStart();
        var value = trimmed.Length > 0 && trimmed[0] == '>' ? trimmed[1..] : trimmed;
        return value.StartsWith(' ') ? value[1..] : value;
    }

    /// <summary>
    /// Reads a pipe table at <paramref name="start"/>: a header row, a delimiter row of dashes
    /// with optional colons, then body rows until a blank line or a different block. The
    /// delimiter row has to carry a pipe of its own, so a bare <c>---</c> stays a rule.
    /// </summary>
    private static bool TryTable(
        IReadOnlyList<string> lines,
        int start,
        out MarkdownTableBlock table,
        out int consumed)
    {
        table = null!;
        consumed = 0;

        if (start + 1 >= lines.Count)
        {
            return false;
        }

        var header = SplitRow(lines[start]);
        if (header.Count == 0 || !TryDelimiter(lines[start + 1], out var alignments))
        {
            return false;
        }

        if (alignments.Count != header.Count)
        {
            return false;
        }

        var rows = new List<IReadOnlyList<MarkdownTableCell>>();
        var index = start + 2;

        while (index < lines.Count
               && !string.IsNullOrWhiteSpace(lines[index])
               && !StartsBlock(lines[index]))
        {
            var cells = SplitRow(lines[index]);
            if (cells.Count == 0)
            {
                break;
            }

            rows.Add(PadRow(cells, alignments));
            index++;
        }

        table = new MarkdownTableBlock(PadRow(header, alignments), rows);
        consumed = index - start;
        return true;
    }

    private static bool TryDelimiter(string line, out IReadOnlyList<MarkdownTableAlign> alignments)
    {
        alignments = Array.Empty<MarkdownTableAlign>();

        if (!line.Contains('|'))
        {
            return false;
        }

        var cells = SplitRow(line);
        if (cells.Count == 0)
        {
            return false;
        }

        var result = new List<MarkdownTableAlign>(cells.Count);
        foreach (var cell in cells)
        {
            if (!DelimiterCell.IsMatch(cell))
            {
                return false;
            }

            var left = cell.StartsWith(':');
            var right = cell.EndsWith(':');
            result.Add((left, right) switch
            {
                (true, true) => MarkdownTableAlign.Center,
                (false, true) => MarkdownTableAlign.Right,
                (true, false) => MarkdownTableAlign.Left,
                _ => MarkdownTableAlign.None,
            });
        }

        alignments = result;
        return true;
    }

    /// <summary>
    /// Splits one table row on pipes that are neither escaped nor inside a code span, and drops
    /// the empty cells a leading or trailing boundary pipe produces. No pipe at all yields no
    /// cells, which is what keeps ordinary prose out of table detection.
    /// </summary>
    private static IReadOnlyList<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return Array.Empty<string>();
        }

        var cells = new List<string>();
        var cell = new StringBuilder();
        var hasPipe = false;
        var fence = 0;

        for (var index = 0; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];

            if (ch == '\\' && index + 1 < trimmed.Length && trimmed[index + 1] == '|')
            {
                cell.Append('|');
                index++;
                continue;
            }

            if (ch == '`')
            {
                var run = 1;
                while (index + run < trimmed.Length && trimmed[index + run] == '`')
                {
                    run++;
                }

                if (fence == 0)
                {
                    fence = run;
                }
                else if (run == fence)
                {
                    fence = 0;
                }

                cell.Append(trimmed, index, run);
                index += run - 1;
                continue;
            }

            if (ch == '|' && fence == 0)
            {
                hasPipe = true;
                cells.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            cell.Append(ch);
        }

        if (!hasPipe)
        {
            return Array.Empty<string>();
        }

        if (!trimmed.StartsWith('|'))
        {
            cells.Add(cell.ToString().Trim());
        }
        else if (cells.Count > 0)
        {
            // A leading boundary pipe produced an empty first cell, not an empty column.
            cells.RemoveAt(0);
        }

        // A row ending on a boundary pipe leaves one empty cell behind, not an extra column.
        if (cells.Count > 0 && cells[^1].Length == 0
            && trimmed.EndsWith('|') && !trimmed.EndsWith("\\|", StringComparison.Ordinal))
        {
            cells.RemoveAt(cells.Count - 1);
        }

        return cells;
    }

    private static IReadOnlyList<MarkdownTableCell> PadRow(
        IReadOnlyList<string> cells,
        IReadOnlyList<MarkdownTableAlign> alignments)
    {
        var row = new List<MarkdownTableCell>(alignments.Count);

        for (var column = 0; column < alignments.Count; column++)
        {
            var text = column < cells.Count ? cells[column] : string.Empty;
            row.Add(new MarkdownTableCell(ParseInline(text), alignments[column]));
        }

        return row;
    }

    private static bool TryCodeSpan(string text, int start, out MarkdownInline run, out int consumed)
    {
        run = null!;
        consumed = 0;

        var width = 1;
        while (start + width < text.Length && text[start + width] == '`')
        {
            width++;
        }

        for (var close = start + width; close < text.Length; close++)
        {
            if (text[close] != '`')
            {
                continue;
            }

            var closeWidth = 1;
            while (close + closeWidth < text.Length && text[close + closeWidth] == '`')
            {
                closeWidth++;
            }

            if (closeWidth != width)
            {
                close += closeWidth - 1;
                continue;
            }

            var content = text[(start + width)..close];
            if (content.Length >= 2 && content[0] == ' ' && content[^1] == ' ' && content.Trim().Length > 0)
            {
                content = content[1..^1];
            }

            run = new MarkdownCodeSpan(content.Replace('\n', ' '));
            consumed = close + width - start;
            return true;
        }

        return false;
    }

    private static bool TryEmphasis(string text, int start, out MarkdownInline run, out int consumed)
    {
        run = null!;
        consumed = 0;

        var marker = text[start];
        var strong = start + 1 < text.Length && text[start + 1] == marker;

        if (strong)
        {
            var close = FindMarker(text, start + 2, marker, strong: true);
            if (close < 0)
            {
                return false;
            }

            var content = text[(start + 2)..close];
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            run = new MarkdownStrong(ParseInline(content));
            consumed = close + 2 - start;
            return true;
        }

        if (start + 1 >= text.Length || char.IsWhiteSpace(text[start + 1]))
        {
            return false;
        }

        // Underscore inside a word is snake_case, not emphasis.
        if (marker == '_' && start > 0 && char.IsLetterOrDigit(text[start - 1]))
        {
            return false;
        }

        var single = FindMarker(text, start + 1, marker, strong: false);
        if (single <= start + 1)
        {
            return false;
        }

        var inner = text[(start + 1)..single];
        if (string.IsNullOrWhiteSpace(inner))
        {
            return false;
        }

        run = new MarkdownEmphasis(ParseInline(inner));
        consumed = single + 1 - start;
        return true;
    }

    private static int FindMarker(string text, int from, char marker, bool strong)
    {
        var width = strong ? 2 : 1;

        for (var index = from; index + width <= text.Length; index++)
        {
            if (text[index] != marker)
            {
                continue;
            }

            if (strong && (index + 1 >= text.Length || text[index + 1] != marker))
            {
                continue;
            }

            // A closing marker never starts after whitespace, and an underscore never
            // closes inside a word.
            if (index > 0 && char.IsWhiteSpace(text[index - 1]))
            {
                continue;
            }

            if (!strong && marker == '_' && index + 1 < text.Length && char.IsLetterOrDigit(text[index + 1]))
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static bool TryLink(string text, int start, out MarkdownInline run, out int consumed)
    {
        run = null!;
        consumed = 0;

        var close = FindMatching(text, start + 1, '[', ']');
        if (close < 0 || close + 1 >= text.Length || text[close + 1] != '(')
        {
            return false;
        }

        var paren = FindMatching(text, close + 2, '(', ')');
        if (paren < 0)
        {
            return false;
        }

        var label = text[(start + 1)..close];
        var href = text[(close + 2)..paren].Trim();
        if (label.Length == 0 || href.Length == 0)
        {
            return false;
        }

        // An unsafe target keeps its words but never becomes a link.
        run = IsSafeHref(href)
            ? new MarkdownLink(ParseInline(label), href)
            : new MarkdownText($"{label} ({href})");
        consumed = paren + 1 - start;
        return true;
    }

    private static int FindMatching(string text, int from, char open, char close)
    {
        var depth = 0;
        for (var index = from; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == open)
            {
                depth++;
            }
            else if (text[index] == close)
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    /// <summary>
    /// False for anything a browser would treat as a script or a foreign origin: only
    /// http(s), mailto, a single-slash root path, a fragment, or a colon-free relative path
    /// survive. Protocol-relative targets (<c>//host</c>) and backslash tricks do not.
    /// </summary>
    private static bool IsSafeHref(string href)
    {
        if (href.Length == 0
            || href.StartsWith("//", StringComparison.Ordinal)
            || href.StartsWith('\\')
            || href.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        if (href[0] is '#' or '/')
        {
            return true;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto";
        }

        return !href.Contains(':', StringComparison.Ordinal);
    }

    private static bool IsEscapable(char ch) => "\\`*_{}[]()#+-.!|>~".Contains(ch);
}