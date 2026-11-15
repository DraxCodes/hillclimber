using ClimberWeb.Services;

namespace ClimberWeb.Tests;

public class MarkdownParserTests
{
    private static MarkdownBlock Single(string markdown)
    {
        var block = Assert.Single(MarkdownParser.Parse(markdown));
        return block;
    }

    private static MarkdownParagraph Paragraph(string markdown) => Assert.IsType<MarkdownParagraph>(Single(markdown));

    private static string Plain(IReadOnlyList<MarkdownInline> runs) => string.Concat(runs.Select(run => run switch
    {
        MarkdownText text => text.Text,
        MarkdownCodeSpan code => code.Text,
        MarkdownStrong strong => Plain(strong.Children),
        MarkdownEmphasis emphasis => Plain(emphasis.Children),
        MarkdownLink link => Plain(link.Children),
        _ => string.Empty,
    }));

    [Fact]
    public void BlankInputProducesNothing()
    {
        Assert.Empty(MarkdownParser.Parse(null));
        Assert.Empty(MarkdownParser.Parse(" \n\t "));
    }

    [Fact]
    public void AParagraphKeepsItsWordsAndSoftLineBreak()
    {
        var paragraph = Paragraph("first line\nsecond line");

        Assert.Equal("first line\nsecond line", Plain(paragraph.Inlines));
    }

    [Fact]
    public void BoldItalicAndCodeBecomeInlineRuns()
    {
        var runs = Paragraph("a **bold** and *italic* and `code`").Inlines;

        Assert.Collection(
            runs,
            run => Assert.Equal("a ", Plain([run])),
            run =>
            {
                var strong = Assert.IsType<MarkdownStrong>(run);
                Assert.Equal("bold", Plain(strong.Children));
            },
            run => Assert.Equal(" and ", Plain([run])),
            run => Assert.IsType<MarkdownEmphasis>(run),
            run => Assert.Equal(" and ", Plain([run])),
            run =>
            {
                var code = Assert.IsType<MarkdownCodeSpan>(run);
                Assert.Equal("code", code.Text);
            });
    }

    [Fact]
    public void StrongCanContainACodeSpan()
    {
        var strong = Assert.IsType<MarkdownStrong>(Assert.Single(Paragraph("**run `dotnet test`**").Inlines));

        Assert.Collection(
            strong.Children,
            child => Assert.Equal("run ", Plain([child])),
            child => Assert.Equal("dotnet test", Assert.IsType<MarkdownCodeSpan>(child).Text));
    }

    [Fact]
    public void CodeSpanKeepsMarkdownMarkersLiteral()
    {
        var code = Assert.IsType<MarkdownCodeSpan>(Assert.Single(Paragraph("`**not bold**`").Inlines));

        Assert.Equal("**not bold**", code.Text);
    }

    [Fact]
    public void UnmatchedMarkersStayLiteral()
    {
        var paragraph = Paragraph("a **still bold and a bare * star");

        Assert.Equal("a **still bold and a bare * star", Plain(paragraph.Inlines));
        Assert.All(paragraph.Inlines, run => Assert.IsType<MarkdownText>(run));
    }

    [Fact]
    public void SnakeCaseIsNotEmphasis()
    {
        var paragraph = Paragraph("call snake_case_name here");

        Assert.Equal("call snake_case_name here", Plain(paragraph.Inlines));
        Assert.All(paragraph.Inlines, run => Assert.IsType<MarkdownText>(run));
    }

    [Fact]
    public void EscapedMarkersStayLiteral()
    {
        var paragraph = Paragraph("literal \\*stars\\* and \\`ticks\\`");

        Assert.Equal("literal *stars* and `ticks`", Plain(paragraph.Inlines));
        Assert.All(paragraph.Inlines, run => Assert.IsType<MarkdownText>(run));
    }

    [Fact]
    public void ASafeLinkBecomesALink()
    {
        var link = Assert.IsType<MarkdownLink>(Assert.Single(Paragraph("[the docs](https://example.com/a)").Inlines));

        Assert.Equal("https://example.com/a", link.Href);
        Assert.Equal("the docs", Plain(link.Children));
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](//evil.example/path)")]
    [InlineData("[x](\\\\server\\share)")]
    public void AnUnsafeLinkKeepsItsWordsButNotItsTarget(string markdown)
    {
        var paragraph = Paragraph(markdown);

        Assert.All(paragraph.Inlines, run => Assert.IsType<MarkdownText>(run));
        Assert.Contains("x", Plain(paragraph.Inlines), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x](mailto:someone@example.com)")]
    [InlineData("[x](/issues/3)")]
    [InlineData("[x](docs/adr/0001-record.md)")]
    [InlineData("[x](#the-decision)")]
    public void SafeNonHttpTargetsBecomeLinks(string markdown)
    {
        Assert.IsType<MarkdownLink>(Assert.Single(Paragraph(markdown).Inlines));
    }

    [Fact]
    public void HeadingsMapTheirLevel()
    {
        var block = Assert.IsType<MarkdownHeading>(Single("## The decision"));

        Assert.Equal(2, block.Level);
        Assert.Equal("The decision", Plain(block.Inlines));
    }

    [Fact]
    public void ABulletListKeepsItemsInOrder()
    {
        var list = Assert.IsType<MarkdownListBlock>(Single("- one\n- two\n- three"));

        Assert.False(list.Ordered);
        Assert.Equal(new[] { "one", "two", "three" }, list.Items.Select(item => Plain(item.Inlines)));
    }

    [Fact]
    public void AnOrderedListKeepsItemsInOrder()
    {
        var list = Assert.IsType<MarkdownListBlock>(Single("1. one\n2. two"));

        Assert.True(list.Ordered);
        Assert.Equal(new[] { "one", "two" }, list.Items.Select(item => Plain(item.Inlines)));
    }

    [Fact]
    public void AnIndentedLineContinuesTheItemAboveIt()
    {
        var list = Assert.IsType<MarkdownListBlock>(Single("- one\n  continued\n- two"));

        Assert.Equal(new[] { "one\ncontinued", "two" }, list.Items.Select(item => Plain(item.Inlines)));
    }

    [Fact]
    public void AFenceKeepsItsCodeVerbatim()
    {
        var code = Assert.IsType<MarkdownCodeBlock>(Single("```csharp\nvar x = 1;\n**not bold**\n```"));

        Assert.Equal("csharp", code.Language);
        Assert.Equal("var x = 1;\n**not bold**", code.Code);
    }

    [Fact]
    public void AnUnterminatedFenceConsumesTheRestOfTheMessage()
    {
        var blocks = MarkdownParser.Parse("before\n\n```\nstreaming code");

        Assert.Equal(2, blocks.Count);
        var code = Assert.IsType<MarkdownCodeBlock>(blocks[^1]);
        Assert.Null(code.Language);
        Assert.Equal("streaming code", code.Code);
    }

    [Fact]
    public void AQuoteParsesItsOwnBlocks()
    {
        var quote = Assert.IsType<MarkdownQuoteBlock>(Single("> first\n>\n> - one"));

        Assert.Equal(2, quote.Children.Count);
        Assert.IsType<MarkdownParagraph>(quote.Children[0]);
        Assert.IsType<MarkdownListBlock>(quote.Children[1]);
    }

    [Fact]
    public void ARuleBecomesABreak()
    {
        Assert.IsType<MarkdownBreak>(Single("---"));
    }

    [Fact]
    public void ProseAndABlockStayInOrder()
    {
        var blocks = MarkdownParser.Parse("intro\n\n- one\n- two\n\noutro");

        Assert.Collection(
            blocks,
            block => Assert.Equal("intro", Plain(Assert.IsType<MarkdownParagraph>(block).Inlines)),
            block => Assert.IsType<MarkdownListBlock>(block),
            block => Assert.Equal("outro", Plain(Assert.IsType<MarkdownParagraph>(block).Inlines)));
    }

    [Fact]
    public void RawHtmlStaysText()
    {
        var paragraph = Paragraph("<script>alert('x')</script>");

        Assert.Equal("<script>alert('x')</script>", Plain(paragraph.Inlines));
    }
}