using ClimberWeb.Components.Ui;

namespace ClimberWeb.Tests;

/// <summary>
/// <see cref="UiClass.Join"/> resolves conflicting Tailwind utilities the way the registry's
/// <c>cn()</c> does: a caller's class wins, without an <c>!</c> escape. These tests pin the
/// families the ported components actually compose and the cases that bit the UI before.
/// </summary>
public class UiClassTests
{
    // ---- the shape of the helper --------------------------------------------

    [Fact]
    public void KeepsOrderAndCollapsesWhitespace()
    {
        Assert.Equal("a b c", UiClass.Join("a  b", " ", null, "c"));
    }

    [Fact]
    public void IgnoresEmptyParts()
    {
        Assert.Equal("px-2", UiClass.Join(null, "", "   ", "px-2", null));
    }

    [Fact]
    public void DropsAnExactDuplicate()
    {
        Assert.Equal("method active", UiClass.Join("method active", "active"));
    }

    [Fact]
    public void LeavesUnknownClassesAloneAndInOrder()
    {
        Assert.Equal("method active st-ok", UiClass.Join("method", "active", "st-ok"));
    }

    // ---- a caller beats the port --------------------------------------------

    [Fact]
    public void ADisplayUtilityBeatsThePortsBaseDisplay()
    {
        // The method badge bug: Badge's base `inline-flex` used to win over `hidden`.
        Assert.Equal("w-fit items-center hidden", UiClass.Join("inline-flex w-fit items-center", "hidden"));
    }

    [Fact]
    public void APaddingUtilityBeatsThePortsBasePadding()
    {
        Assert.Equal("px-4", UiClass.Join("px-6", "px-4"));
    }

    [Fact]
    public void AWidthBeatsThePortsBaseWidth()
    {
        Assert.Equal("w-fit min-w-0 max-w-[68ch]", UiClass.Join("w-fit max-w-full min-w-0", "max-w-[68ch]"));
    }

    [Fact]
    public void ABorderColourBeatsThePortsTransparentBorder()
    {
        Assert.Equal(
            "rounded-3xl border border-border",
            UiClass.Join("rounded-3xl border border-transparent", "border-border"));
    }

    [Fact]
    public void ATextColourBeatsThePortsMutedColour()
    {
        Assert.Equal(
            "text-sm text-destructive",
            UiClass.Join("text-sm text-muted-foreground", "text-destructive"));
    }

    [Fact]
    public void AFontWeightBeatsThePortsWeight()
    {
        Assert.Equal(
            "font-mono text-[0.68rem] font-medium",
            UiClass.Join("font-semibold", "font-mono text-[0.68rem] font-medium"));
    }

    [Fact]
    public void TheLastClassInAFamilyWins()
    {
        Assert.Equal("p-1", UiClass.Join("p-5", "p-2", "p-1"));
    }

    // ---- the utility tree ----------------------------------------------------

    [Fact]
    public void ABroaderUtilityRemovesTheNarrowerOnesItCovers()
    {
        Assert.Equal("p-0", UiClass.Join("px-4 py-2", "p-0"));
    }

    [Fact]
    public void ANarrowerUtilityKeepsItsBroaderAncestor()
    {
        // `p-4 px-2` is meaningful: the x padding overrides half of the shorthand.
        Assert.Equal("p-4 px-2", UiClass.Join("p-4", "px-2"));
    }

    [Fact]
    public void MarginsAndGapsUseTheSameTree()
    {
        Assert.Equal("m-0", UiClass.Join("mx-auto my-2", "m-0"));
        Assert.Equal("gap-0", UiClass.Join("gap-6", "gap-0"));
        Assert.Equal("gap-4 gap-x-2", UiClass.Join("gap-4", "gap-x-2"));
    }

    [Fact]
    public void RadiusCornersAreNarrowerThanTheWholeRadius()
    {
        Assert.Equal("rounded-xl", UiClass.Join("rounded-t-lg rounded-b-lg", "rounded-xl"));
        Assert.Equal("rounded-xl rounded-t-none", UiClass.Join("rounded-xl", "rounded-t-none"));
    }

    [Fact]
    public void SizeCoversWidthAndHeight()
    {
        Assert.Equal("size-8", UiClass.Join("w-4 h-4", "size-8"));
        Assert.Equal("size-8", UiClass.Join("size-4", "size-8"));
    }

    [Fact]
    public void BorderWidthAndBorderColourAreDifferentFamilies()
    {
        Assert.Equal("border border-b border-border", UiClass.Join("border border-b", "border-border"));
        Assert.Equal("border-border border-2", UiClass.Join("border-border border", "border-2"));
    }

    // ---- variants stay separate ---------------------------------------------

    [Fact]
    public void ADifferentVariantNeverConflicts()
    {
        Assert.Equal("bg-primary hover:bg-accent/90", UiClass.Join("bg-primary", "hover:bg-accent/90"));
        Assert.Equal("px-6 sm:px-5", UiClass.Join("px-6", "sm:px-5"));
        Assert.Equal("bg-muted/30 dark:bg-muted", UiClass.Join("bg-muted/30", "dark:bg-muted"));
    }

    [Fact]
    public void ArbitraryVariantsWithColonsParseAsOnePrefix()
    {
        Assert.Equal(
            "text-muted-foreground in-[a:hover,button:hover]:text-foreground",
            UiClass.Join("text-muted-foreground", "in-[a:hover,button:hover]:text-foreground"));

        Assert.Equal(
            "size-4 [&_svg:not([class*='size-'])]:size-4",
            UiClass.Join("size-4", "[&_svg:not([class*='size-'])]:size-4"));
    }

    [Fact]
    public void ADataVariantMatchesOnlyItsOwnVariant()
    {
        Assert.Equal(
            "text-muted-foreground data-[status=error]:text-destructive",
            UiClass.Join("text-muted-foreground", "data-[status=error]:text-destructive"));
    }

    // ---- the ambiguities that need a rule -----------------------------------

    [Fact]
    public void TextSizeColourAndAlignmentAreThreeFamilies()
    {
        Assert.Equal(
            "text-sm text-muted-foreground text-left",
            UiClass.Join("text-sm", "text-muted-foreground", "text-left"));
        Assert.Equal("text-muted-foreground text-xs", UiClass.Join("text-muted-foreground text-sm", "text-xs"));
    }

    [Fact]
    public void AnArbitraryTextValueIsASizeWhenItReadsAsALength()
    {
        Assert.Equal("text-[0.68rem]", UiClass.Join("text-sm", "text-[0.68rem]"));
        Assert.Equal("text-[#fafafa]", UiClass.Join("text-muted-foreground", "text-[#fafafa]"));
        Assert.Equal("text-[length:0.7rem]", UiClass.Join("text-sm", "text-[length:0.7rem]"));
    }

    [Fact]
    public void FontFamilyAndFontWeightAreDifferentFamilies()
    {
        Assert.Equal("font-mono text-sm font-medium", UiClass.Join("font-mono", "text-sm", "font-medium"));
    }

    [Fact]
    public void RingWidthAndRingColourAreDifferentFamilies()
    {
        Assert.Equal(
            "focus-visible:ring-3 focus-visible:ring-ring",
            UiClass.Join("focus-visible:ring-3", "focus-visible:ring-ring"));
        Assert.Equal("ring-2", UiClass.Join("ring-1", "ring-2"));
    }

    [Fact]
    public void AnImportantSuffixStillBelongsToItsFamily()
    {
        Assert.Equal("p-4!", UiClass.Join("p-2", "p-4!"));
    }

    // ---- the compositions this pass removed the escapes from -----------------

    [Fact]
    public void CardAndPagePanelComposeWithoutEscapes()
    {
        Assert.Equal(
            "bg-card text-card-foreground flex flex-col rounded-xl border shadow-sm gap-0 overflow-hidden py-0",
            UiClass.Join(
                "bg-card text-card-foreground flex flex-col gap-6 rounded-xl border py-6 shadow-sm",
                "gap-0 overflow-hidden py-0"));
    }

    [Fact]
    public void PagePanelHeaderBeatsTheCardHeaderPadding()
    {
        Assert.Equal(
            "@container/card-header grid auto-rows-min grid-rows-[auto_auto] items-start gap-1.5 has-data-[slot=card-action]:grid-cols-[1fr_auto] border-b px-4 py-2.5 sm:px-5",
            UiClass.Join(
                "@container/card-header grid auto-rows-min grid-rows-[auto_auto] items-start gap-1.5 px-6 has-data-[slot=card-action]:grid-cols-[1fr_auto]",
                "border-b px-4 py-2.5 sm:px-5"));
    }

    [Fact]
    public void ThePromptBubbleOverridesMeasureBorderAndFont()
    {
        var css = UiClass.Join(
            "w-fit max-w-full min-w-0 overflow-hidden rounded-3xl border border-transparent px-3 py-2.5 text-sm leading-relaxed wrap-break-word",
            "bg-muted",
            "max-w-[68ch] border-border font-sans");

        Assert.DoesNotContain("max-w-full", css);
        Assert.DoesNotContain("border-transparent", css);
        Assert.Contains("max-w-[68ch]", css);
        Assert.Contains("border-border", css);
        Assert.Contains("font-sans", css);
    }
}