namespace ClimberWeb.Components.Ui;

/// <summary>
/// Maps a Tailwind utility to the conflict family it belongs to. Families that cover a smaller
/// slice of the same property are children of the broader one (<c>px-4</c> under <c>p-4</c>), so
/// the resolver can let a later broad class remove the narrow ones it subsumes. Unknown
/// utilities return null and never conflict, which keeps the fallback append-only.
/// </summary>
internal static class TailwindConflict
{
    private static readonly string[] DisplayFamily = ["display"];
    private static readonly string[] PositionFamily = ["position"];
    private static readonly string[] VisibilityFamily = ["visibility"];
    private static readonly string[] WidthFamily = ["size", "width"];
    private static readonly string[] HeightFamily = ["size", "height"];
    private static readonly string[] SizeFamily = ["size"];
    private static readonly string[] MinWidthFamily = ["min-width"];
    private static readonly string[] MaxWidthFamily = ["max-width"];
    private static readonly string[] MinHeightFamily = ["min-height"];
    private static readonly string[] MaxHeightFamily = ["max-height"];
    private static readonly string[] PaddingFamily = ["padding"];
    private static readonly string[] MarginFamily = ["margin"];
    private static readonly string[] GapFamily = ["gap"];
    private static readonly string[] RadiusFamily = ["radius"];
    private static readonly string[] BorderWidthFamily = ["border-width"];
    private static readonly string[] BorderColorFamily = ["border-color"];
    private static readonly string[] BorderStyleFamily = ["border-style"];
    private static readonly string[] RingWidthFamily = ["ring-width"];
    private static readonly string[] RingColorFamily = ["ring-color"];
    private static readonly string[] FontSizeFamily = ["font-size"];
    private static readonly string[] TextColorFamily = ["text-color"];
    private static readonly string[] FontFamilyFamily = ["font-family"];
    private static readonly string[] FontWeightFamily = ["font-weight"];
    private static readonly string[] BackgroundFamily = ["background-color"];
    private static readonly string[] OverflowFamily = ["overflow"];
    private static readonly string[] AlignItemsFamily = ["align-items"];
    private static readonly string[] JustifyFamily = ["justify-content"];
    private static readonly string[] AlignSelfFamily = ["align-self"];
    private static readonly string[] FlexFamily = ["flex"];
    private static readonly string[] FlexDirectionFamily = ["flex-direction"];
    private static readonly string[] FlexWrapFamily = ["flex-wrap"];
    private static readonly string[] FlexGrowFamily = ["flex-grow"];
    private static readonly string[] FlexShrinkFamily = ["flex-shrink"];
    private static readonly string[] FlexBasisFamily = ["flex-basis"];
    private static readonly string[] GridColumnsFamily = ["grid-template-columns"];
    private static readonly string[] GridRowsFamily = ["grid-template-rows"];
    private static readonly string[] GridAutoColumnsFamily = ["grid-auto-columns"];
    private static readonly string[] GridAutoRowsFamily = ["grid-auto-rows"];
    private static readonly string[] GridColumnFamily = ["grid-column"];
    private static readonly string[] GridRowFamily = ["grid-row"];
    private static readonly string[] WhitespaceFamily = ["white-space"];
    private static readonly string[] OverflowWrapFamily = ["overflow-wrap"];
    private static readonly string[] TextOverflowFamily = ["text-overflow"];
    private static readonly string[] TextAlignFamily = ["text-align"];
    private static readonly string[] TextWrapFamily = ["text-wrap"];
    private static readonly string[] TextTransformFamily = ["text-transform"];
    private static readonly string[] TextDecorationFamily = ["text-decoration-line"];
    private static readonly string[] FontStyleFamily = ["font-style"];
    private static readonly string[] LetterSpacingFamily = ["letter-spacing"];
    private static readonly string[] LineHeightFamily = ["line-height"];
    private static readonly string[] TransitionFamily = ["transition-property"];
    private static readonly string[] DurationFamily = ["transition-duration"];
    private static readonly string[] EaseFamily = ["transition-timing-function"];
    private static readonly string[] DelayFamily = ["transition-delay"];
    private static readonly string[] ShadowFamily = ["box-shadow"];
    private static readonly string[] OpacityFamily = ["opacity"];
    private static readonly string[] ZIndexFamily = ["z-index"];
    private static readonly string[] CursorFamily = ["cursor"];
    private static readonly string[] PointerEventsFamily = ["pointer-events"];
    private static readonly string[] UserSelectFamily = ["user-select"];
    private static readonly string[] ResizeFamily = ["resize"];
    private static readonly string[] AppearanceFamily = ["appearance"];
    private static readonly string[] ObjectFitFamily = ["object-fit"];
    private static readonly string[] ObjectPositionFamily = ["object-position"];
    private static readonly string[] VerticalAlignFamily = ["vertical-align"];
    private static readonly string[] FontVariantFamily = ["font-variant-numeric"];
    private static readonly string[] AnimationFamily = ["animation"];
    private static readonly string[] TransformFamily = ["transform"];
    private static readonly string[] ListStyleFamily = ["list-style-type"];
    private static readonly string[] InsetFamily = ["inset"];

    private static readonly HashSet<string> Displays = new(StringComparer.Ordinal)
    {
        "block", "inline-block", "inline", "flex", "inline-flex", "table", "inline-table",
        "table-caption", "table-cell", "table-column", "table-column-group", "table-footer-group",
        "table-header-group", "table-row-group", "table-row", "flow-root", "grid", "inline-grid",
        "contents", "list-item", "hidden",
    };

    private static readonly HashSet<string> Positions = new(StringComparer.Ordinal)
    {
        "static", "fixed", "absolute", "relative", "sticky",
    };

    private static readonly HashSet<string> FontSizes = new(StringComparer.Ordinal)
    {
        "xs", "sm", "base", "lg", "xl", "2xl", "3xl", "4xl", "5xl", "6xl", "7xl", "8xl", "9xl",
    };

    private static readonly HashSet<string> TextAligns = new(StringComparer.Ordinal)
    {
        "left", "center", "right", "justify", "start", "end",
    };

    private static readonly HashSet<string> TextWraps = new(StringComparer.Ordinal)
    {
        "wrap", "nowrap", "balance", "pretty",
    };

    private static readonly HashSet<string> FontFamilies = new(StringComparer.Ordinal)
    {
        "sans", "serif", "mono",
    };

    private static readonly HashSet<string> FontWeights = new(StringComparer.Ordinal)
    {
        "thin", "extralight", "light", "normal", "medium", "semibold", "bold", "extrabold",
        "black",
    };

    private static readonly HashSet<string> BorderStyles = new(StringComparer.Ordinal)
    {
        "solid", "dashed", "dotted", "double", "hidden", "none",
    };

    private static readonly HashSet<string> TextTransforms = new(StringComparer.Ordinal)
    {
        "uppercase", "lowercase", "capitalize", "normal-case",
    };

    private static readonly HashSet<string> Decorations = new(StringComparer.Ordinal)
    {
        "underline", "overline", "line-through", "no-underline",
    };

    private static readonly HashSet<string> BreakWords = new(StringComparer.Ordinal)
    {
        "break-normal", "break-words", "break-all", "break-keep", "wrap-break-word",
    };

    private static readonly HashSet<string> ListStyles = new(StringComparer.Ordinal)
    {
        "list-none", "list-disc", "list-decimal",
    };

    private static readonly HashSet<string> Overflows = new(StringComparer.Ordinal)
    {
        "auto", "hidden", "clip", "visible", "scroll",
    };

    private static readonly string[] SpacingAxes = ["x", "y", "s", "e", "t", "r", "b", "l"];
    private static readonly string[] BorderAxes = ["x", "y", "s", "e", "t", "r", "b", "l"];
    private static readonly string[] RadiusCorners = ["tl", "tr", "bl", "br", "t", "r", "b", "l", "s", "e"];
    private static readonly string[] InsetAxes = ["x", "y"];
    private static readonly (string Side, string Axis)[] InsetSides =
    [
        ("top", "t"), ("right", "r"), ("bottom", "b"), ("left", "l"), ("start", "s"), ("end", "e"),
    ];
    private static readonly string[] TransformAxes = ["x", "y", "z"];

    /// <summary>
    /// Sibling coverage: a two-axis utility also sets the single-side utilities named here,
    /// though their paths are not prefixes of one another (<c>px</c> vs <c>pl</c>). A later
    /// broad axis removes the side classes it covers; the reverse keeps both, as twMerge does.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> AxisCoverage = new(StringComparer.Ordinal)
    {
        ["padding/x"] = new(StringComparer.Ordinal) { "l", "r" },
        ["padding/y"] = new(StringComparer.Ordinal) { "t", "b" },
        ["margin/x"] = new(StringComparer.Ordinal) { "l", "r" },
        ["margin/y"] = new(StringComparer.Ordinal) { "t", "b" },
        ["inset/x"] = new(StringComparer.Ordinal) { "l", "r" },
        ["inset/y"] = new(StringComparer.Ordinal) { "t", "b" },
        ["border-width/x"] = new(StringComparer.Ordinal) { "l", "r" },
        ["border-width/y"] = new(StringComparer.Ordinal) { "t", "b" },
        ["border-color/x"] = new(StringComparer.Ordinal) { "l", "r" },
        ["border-color/y"] = new(StringComparer.Ordinal) { "t", "b" },
        ["radius/t"] = new(StringComparer.Ordinal) { "tl", "tr" },
        ["radius/r"] = new(StringComparer.Ordinal) { "tr", "br" },
        ["radius/b"] = new(StringComparer.Ordinal) { "bl", "br" },
        ["radius/l"] = new(StringComparer.Ordinal) { "tl", "bl" },
    };

    /// <summary>
    /// True when a later <paramref name="incoming"/> utility makes an earlier
    /// <paramref name="existing"/> one redundant: the incoming path is the existing path's
    /// ancestor, or the incoming axis covers the existing side.
    /// </summary>
    public static bool Covers(string[] incoming, string[] existing) =>
        (incoming.Length <= existing.Length
            && incoming.AsSpan().SequenceEqual(existing.AsSpan(0, incoming.Length)))
        || (incoming.Length == 2
            && existing.Length == 2
            && string.Equals(incoming[0], existing[0], StringComparison.Ordinal)
            && AxisCoverage.TryGetValue($"{incoming[0]}/{incoming[1]}", out var covered)
            && covered.Contains(existing[1]));

    /// <summary>The conflict family for <paramref name="utility"/>, or null when it is unknown.</summary>
    public static string[]? Path(string utility)
    {
        if (string.IsNullOrEmpty(utility))
        {
            return null;
        }

        // Negative utilities conflict with their positive counterparts.
        var value = utility[0] == '-' ? utility[1..] : utility;
        if (value.Length == 0)
        {
            return null;
        }

        if (Displays.Contains(value)) return DisplayFamily;
        if (Positions.Contains(value)) return PositionFamily;
        if (value is "visible" or "invisible" or "collapse") return VisibilityFamily;

        if (value.StartsWith("w-", StringComparison.Ordinal)) return WidthFamily;
        if (value.StartsWith("h-", StringComparison.Ordinal)) return HeightFamily;
        if (value.StartsWith("size-", StringComparison.Ordinal)) return SizeFamily;
        if (value.StartsWith("min-w-", StringComparison.Ordinal)) return MinWidthFamily;
        if (value.StartsWith("max-w-", StringComparison.Ordinal)) return MaxWidthFamily;
        if (value.StartsWith("min-h-", StringComparison.Ordinal)) return MinHeightFamily;
        if (value.StartsWith("max-h-", StringComparison.Ordinal)) return MaxHeightFamily;
        if (value.StartsWith("aspect-", StringComparison.Ordinal)) return ["aspect-ratio"];

        if (Spacing(value, "p", PaddingFamily) is { } padding) return padding;
        if (Spacing(value, "m", MarginFamily) is { } margin) return margin;
        if (Gap(value) is { } gap) return gap;
        if (value.StartsWith("space-x-", StringComparison.Ordinal)) return ["space", "x"];
        if (value.StartsWith("space-y-", StringComparison.Ordinal)) return ["space", "y"];

        if (Radius(value) is { } radius) return radius;
        if (Border(value) is { } border) return border;
        if (Ring(value) is { } ring) return ring;
        if (Outline(value) is { } outline) return outline;

        if (value == "truncate" || value is "text-ellipsis" or "text-clip") return TextOverflowFamily;
        if (value.StartsWith("text-", StringComparison.Ordinal)) return Text(value[5..]);
        if (value.StartsWith("font-", StringComparison.Ordinal)) return Font(value[5..]);
        if (value.StartsWith("tracking-", StringComparison.Ordinal)) return LetterSpacingFamily;
        if (value.StartsWith("leading-", StringComparison.Ordinal)) return LineHeightFamily;
        if (TextTransforms.Contains(value)) return TextTransformFamily;
        if (Decorations.Contains(value)) return TextDecorationFamily;
        if (value.StartsWith("decoration-", StringComparison.Ordinal)) return ["text-decoration-color"];
        if (value.StartsWith("underline-offset-", StringComparison.Ordinal)) return ["underline-offset"];
        if (value is "italic" or "not-italic") return FontStyleFamily;
        if (value == "tabular-nums" || value == "normal-nums") return FontVariantFamily;

        if (value.StartsWith("bg-", StringComparison.Ordinal)) return Background(value[3..]);
        if (value.StartsWith("from-", StringComparison.Ordinal)) return ["gradient-from"];
        if (value.StartsWith("via-", StringComparison.Ordinal)) return ["gradient-via"];
        if (value.StartsWith("to-", StringComparison.Ordinal)) return ["gradient-to"];

        if (Overflows.Contains(value)) return OverflowFamily;
        if (value.StartsWith("overflow-x-", StringComparison.Ordinal)) return ["overflow", "x"];
        if (value.StartsWith("overflow-y-", StringComparison.Ordinal)) return ["overflow", "y"];
        if (value.StartsWith("overflow-", StringComparison.Ordinal)) return OverflowFamily;

        if (value.StartsWith("items-", StringComparison.Ordinal)) return AlignItemsFamily;
        if (value.StartsWith("justify-", StringComparison.Ordinal)) return JustifyFamily;
        if (value.StartsWith("self-", StringComparison.Ordinal)) return AlignSelfFamily;
        if (value.StartsWith("content-", StringComparison.Ordinal)) return ["align-content"];
        if (value.StartsWith("place-", StringComparison.Ordinal)) return ["place-content"];
        if (value == "align-middle" || value.StartsWith("align-", StringComparison.Ordinal)) return VerticalAlignFamily;

        if (value.StartsWith("flex-", StringComparison.Ordinal)) return Flex(value[5..]);
        if (value is "grow" or "grow-0" || value.StartsWith("grow-", StringComparison.Ordinal)) return FlexGrowFamily;
        if (value is "shrink" or "shrink-0" || value.StartsWith("shrink-", StringComparison.Ordinal)) return FlexShrinkFamily;
        if (value.StartsWith("basis-", StringComparison.Ordinal)) return FlexBasisFamily;
        if (value.StartsWith("order-", StringComparison.Ordinal)) return ["order"];

        if (value.StartsWith("grid-cols-", StringComparison.Ordinal)) return GridColumnsFamily;
        if (value.StartsWith("grid-rows-", StringComparison.Ordinal)) return GridRowsFamily;
        if (value.StartsWith("grid-flow-", StringComparison.Ordinal)) return ["grid-auto-flow"];
        if (value.StartsWith("auto-cols-", StringComparison.Ordinal)) return GridAutoColumnsFamily;
        if (value.StartsWith("auto-rows-", StringComparison.Ordinal)) return GridAutoRowsFamily;
        if (value.StartsWith("col-", StringComparison.Ordinal)) return GridColumnFamily;
        if (value.StartsWith("row-", StringComparison.Ordinal)) return GridRowFamily;

        if (value.StartsWith("whitespace-", StringComparison.Ordinal)) return WhitespaceFamily;
        if (BreakWords.Contains(value)) return OverflowWrapFamily;

        if (value.StartsWith("transition", StringComparison.Ordinal)) return TransitionFamily;
        if (value.StartsWith("duration-", StringComparison.Ordinal)) return DurationFamily;
        if (value.StartsWith("ease-", StringComparison.Ordinal)) return EaseFamily;
        if (value.StartsWith("delay-", StringComparison.Ordinal)) return DelayFamily;
        if (value.StartsWith("animate-", StringComparison.Ordinal)) return AnimationFamily;

        if (value == "shadow") return ShadowFamily;
        if (value.StartsWith("shadow-", StringComparison.Ordinal)) return Shadow(value[7..]);
        if (value.StartsWith("opacity-", StringComparison.Ordinal)) return OpacityFamily;
        if (value.StartsWith("z-", StringComparison.Ordinal)) return ZIndexFamily;
        if (value.StartsWith("cursor-", StringComparison.Ordinal)) return CursorFamily;
        if (value.StartsWith("pointer-events-", StringComparison.Ordinal)) return PointerEventsFamily;
        if (value.StartsWith("select-", StringComparison.Ordinal)) return UserSelectFamily;
        if (value.StartsWith("resize-", StringComparison.Ordinal)) return ResizeFamily;
        if (value.StartsWith("appearance-", StringComparison.Ordinal)) return AppearanceFamily;
        if (value.StartsWith("object-", StringComparison.Ordinal)) return Object(value[7..]);
        if (ListStyles.Contains(value)) return ListStyleFamily;

        if (value.StartsWith("translate-", StringComparison.Ordinal)) return Translate(value[10..]);
        if (value.StartsWith("scale-", StringComparison.Ordinal)) return ["scale"];
        if (value.StartsWith("rotate-", StringComparison.Ordinal)) return ["rotate"];
        if (value.StartsWith("skew-", StringComparison.Ordinal)) return ["skew"];
        if (value == "transform" || value.StartsWith("transform-", StringComparison.Ordinal)) return TransformFamily;
        if (value.StartsWith("origin-", StringComparison.Ordinal)) return ["transform-origin"];

        if (Inset(value) is { } inset) return inset;
        if (value.StartsWith("scroll-m-", StringComparison.Ordinal)) return ["scroll-margin"];
        if (value.StartsWith("scroll-p-", StringComparison.Ordinal)) return ["scroll-padding"];

        return null;
    }

    private static string[]? Spacing(string value, string prefix, string[] family)
    {
        if (value == prefix)
        {
            return family;
        }

        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length < prefix.Length + 2)
        {
            return null;
        }

        // `p-4` is the whole side; `px-4`, `pt-2` and friends are the axis variants.
        if (value[prefix.Length] == '-')
        {
            return family;
        }

        return value[prefix.Length + 1] == '-' && SpacingAxes.Contains(value[prefix.Length].ToString())
            ? [.. family, value[prefix.Length].ToString()]
            : null;
    }

    private static string[]? Gap(string value)
    {
        if (value == "gap") return GapFamily;
        if (!value.StartsWith("gap-", StringComparison.Ordinal)) return null;

        var rest = value[4..];
        if (rest is "x" or "y" || rest.StartsWith("x-", StringComparison.Ordinal) || rest.StartsWith("y-", StringComparison.Ordinal))
        {
            return [.. GapFamily, rest[..1]];
        }

        return GapFamily;
    }

    private static string[]? Radius(string value)
    {
        if (value == "rounded") return RadiusFamily;
        if (!value.StartsWith("rounded-", StringComparison.Ordinal)) return null;

        var rest = value[8..];
        foreach (var corner in RadiusCorners)
        {
            if (rest == corner || rest.StartsWith(corner + "-", StringComparison.Ordinal))
            {
                return [.. RadiusFamily, corner];
            }
        }

        return RadiusFamily;
    }

    private static string[]? Border(string value)
    {
        if (value == "border") return BorderWidthFamily;
        if (value == "border-collapse" || value == "border-separate") return ["border-collapse"];
        if (value.StartsWith("border-spacing", StringComparison.Ordinal)) return ["border-spacing"];
        if (!value.StartsWith("border-", StringComparison.Ordinal)) return null;

        var rest = value[7..];
        if (BorderStyles.Contains(rest)) return BorderStyleFamily;
        if (IsWidth(rest)) return BorderWidthFamily;

        foreach (var axis in BorderAxes)
        {
            if (rest == axis) return [.. BorderWidthFamily, axis];
            if (!rest.StartsWith(axis + "-", StringComparison.Ordinal)) continue;

            var remainder = rest[(axis.Length + 1)..];
            return IsWidth(remainder)
                ? [.. BorderWidthFamily, axis]
                : [.. BorderColorFamily, axis];
        }

        return BorderColorFamily;
    }

    private static string[]? Ring(string value)
    {
        if (value == "ring") return RingWidthFamily;
        if (value == "ring-inset") return ["ring-inset"];
        if (value == "ring-offset") return ["ring-offset-width"];
        if (!value.StartsWith("ring-", StringComparison.Ordinal)) return null;

        var rest = value[5..];
        if (rest.StartsWith("offset-", StringComparison.Ordinal))
        {
            return IsWidth(rest[7..]) ? ["ring-offset-width"] : ["ring-offset-color"];
        }

        return IsWidth(rest) ? RingWidthFamily : RingColorFamily;
    }

    private static string[]? Outline(string value)
    {
        if (value == "outline") return ["outline-width"];
        if (!value.StartsWith("outline-", StringComparison.Ordinal)) return null;
        if (value is "outline-none" or "outline-hidden") return ["outline-style"];

        var rest = value[8..];
        if (rest.StartsWith("offset-", StringComparison.Ordinal)) return ["outline-offset"];
        if (BorderStyles.Contains(rest)) return ["outline-style"];

        return IsWidth(rest) ? ["outline-width"] : ["outline-color"];
    }

    private static string[]? Text(string rest)
    {
        if (FontSizes.Contains(rest)) return FontSizeFamily;
        if (TextAligns.Contains(rest)) return TextAlignFamily;
        if (TextWraps.Contains(rest)) return TextWrapFamily;

        // An arbitrary value that reads as a length is a size; `text-[length:â€¦]` says so
        // explicitly. Everything else is a colour, matching Tailwind's own ambiguity rule.
        if (rest.StartsWith('[') && rest.EndsWith(']'))
        {
            var inner = rest[1..^1];
            return inner.StartsWith("length:", StringComparison.Ordinal) || LooksLikeLength(inner)
                ? FontSizeFamily
                : TextColorFamily;
        }

        return TextColorFamily;
    }

    private static string[]? Font(string rest)
    {
        if (FontFamilies.Contains(rest)) return FontFamilyFamily;

        // `font-` is Tailwind's ambiguous slot: a named weight, or a bare number in an
        // arbitrary value, sets weight. Everything else (including `font-['Inter']`) is a
        // family, so an unrecognised name cannot silently collide with a real weight.
        if (FontWeights.Contains(rest)) return FontWeightFamily;
        if (rest.StartsWith('[') && rest.EndsWith(']') && LooksLikeNumber(rest[1..^1])) return FontWeightFamily;
        return FontFamilyFamily;
    }

    private static string[]? Background(string rest)
    {
        if (rest is "none" || rest.StartsWith("gradient-", StringComparison.Ordinal)) return ["background-image"];
        if (rest is "cover" or "contain" or "auto") return ["background-size"];
        if (rest is "center" or "top" or "bottom" or "left" or "right"
            or "top-left" or "top-right" or "bottom-left" or "bottom-right") return ["background-position"];
        if (rest.StartsWith("repeat", StringComparison.Ordinal)) return ["background-repeat"];
        if (rest is "fixed" or "local" or "scroll") return ["background-attachment"];
        if (rest.StartsWith("clip-", StringComparison.Ordinal)) return ["background-clip"];
        if (rest.StartsWith("origin-", StringComparison.Ordinal)) return ["background-origin"];
        return BackgroundFamily;
    }

    private static string[]? Flex(string rest)
    {
        if (rest is "row" or "row-reverse" or "col" or "col-reverse") return FlexDirectionFamily;
        if (rest is "wrap" or "wrap-reverse" or "nowrap") return FlexWrapFamily;
        if (rest is "grow" or "grow-0" || rest.StartsWith("grow-", StringComparison.Ordinal)) return FlexGrowFamily;
        if (rest is "shrink" or "shrink-0" || rest.StartsWith("shrink-", StringComparison.Ordinal)) return FlexShrinkFamily;
        if (rest.StartsWith("basis-", StringComparison.Ordinal)) return FlexBasisFamily;
        return FlexFamily;
    }

    private static string[]? Shadow(string rest)
    {
        if (rest is "xs" or "sm" or "md" or "lg" or "xl" or "2xl" or "none" || rest.StartsWith('['))
        {
            return ShadowFamily;
        }

        return ["box-shadow-color"];
    }

    private static string[]? Object(string rest)
    {
        if (rest is "contain" or "cover" or "fill" or "none" or "scale-down") return ObjectFitFamily;
        return ObjectPositionFamily;
    }

    private static string[]? Translate(string rest)
    {
        foreach (var axis in TransformAxes)
        {
            if (rest == axis || rest.StartsWith(axis + "-", StringComparison.Ordinal))
            {
                return ["translate", axis];
            }
        }

        return ["translate"];
    }

    private static string[]? Inset(string value)
    {
        if (value == "inset") return InsetFamily;
        if (value.StartsWith("inset-", StringComparison.Ordinal))
        {
            var rest = value[6..];
            foreach (var axis in InsetAxes)
            {
                if (rest == axis || rest.StartsWith(axis + "-", StringComparison.Ordinal))
                {
                    return [.. InsetFamily, axis];
                }
            }

            return InsetFamily;
        }

        foreach (var (side, axis) in InsetSides)
        {
            if (value == side || value.StartsWith(side + "-", StringComparison.Ordinal))
            {
                return [.. InsetFamily, axis];
            }
        }

        return null;
    }

    /// <summary>Numeric widths, or an arbitrary value carrying a length unit.</summary>
    private static bool IsWidth(string value) =>
        value is "0" or "px"
        || (value.Length > 0 && value.All(char.IsAsciiDigit))
        || (value.StartsWith('[') && value.EndsWith(']') && LooksLikeLength(value[1..^1]));

    private static bool LooksLikeLength(string value) =>
        value.Contains("px", StringComparison.Ordinal)
        || value.Contains("rem", StringComparison.Ordinal)
        || value.Contains("em", StringComparison.Ordinal)
        || value.Contains("ch", StringComparison.Ordinal)
        || value.Contains("pt", StringComparison.Ordinal)
        || value.Contains("vw", StringComparison.Ordinal)
        || value.Contains("vh", StringComparison.Ordinal)
        || value.Contains('%');

    /// <summary>A bare numeric arbitrary value, the shape a font weight takes.</summary>
    private static bool LooksLikeNumber(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiDigit(c) || c == '.');
}
