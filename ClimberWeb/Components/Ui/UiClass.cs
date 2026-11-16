using System.Collections.Concurrent;

namespace ClimberWeb.Components.Ui;

/// <summary>
/// Joins conditional class fragments with the registry's <c>cn()</c> semantics: a later class
/// wins over an earlier class in the same utility family, so a caller's <c>px-4</c> beats a
/// port's <c>px-6</c> without an <c>!</c> escape. Classes are otherwise preserved in order,
/// duplicates collapse, and variants are compared whole, so <c>hover:bg-accent</c> and
/// <c>bg-primary</c> both survive.
/// </summary>
/// <remarks>
/// This is a small ordered resolver, not a Tailwind compiler: it knows the families the ported
/// components actually compose (spacing, sizing, display, colour, radius, type, flex/grid,
/// positioning, motion). A utility it does not recognise is never a conflict, so the fallback is
/// the old append behaviour rather than a silently dropped class. A later *broader* utility also
/// removes the narrower ones it covers (<c>p-0</c> over <c>px-4 py-2</c>), while a later narrower
/// one keeps its broader ancestor, exactly as <c>twMerge</c> does.
/// </remarks>
public static class UiClass
{
    private static readonly ConcurrentDictionary<string, ClassToken> Tokens = new(StringComparer.Ordinal);

    public static string Join(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var resolved = new List<ClassToken>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            foreach (var raw in part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = Tokens.GetOrAdd(raw, ClassToken.Parse);

                if (resolved.Exists(existing => string.Equals(existing.Class, token.Class, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (token.Group is { } group)
                {
                    for (var index = resolved.Count - 1; index >= 0; index--)
                    {
                        if (resolved[index].Variants == token.Variants
                            && resolved[index].Group is { } existing
                            && IsPrefix(group, existing))
                        {
                            resolved.RemoveAt(index);
                        }
                    }
                }

                resolved.Add(token);
            }
        }

        return string.Join(" ", resolved.Select(token => token.Class));
    }

    /// <summary>True when <paramref name="ancestor"/> equals or contains <paramref name="descendant"/>.</summary>
    private static bool IsPrefix(string[] ancestor, string[] descendant) =>
        ancestor.Length <= descendant.Length
        && ancestor.AsSpan().SequenceEqual(descendant.AsSpan(0, ancestor.Length));

    /// <summary>One class, split into its variant prefix, its conflict family and its source text.</summary>
    private readonly record struct ClassToken(string Class, string Variants, string[]? Group)
    {
        public static ClassToken Parse(string raw)
        {
            var variants = string.Empty;
            var utility = raw;

            var colon = LastTopLevelColon(raw);
            if (colon >= 0)
            {
                variants = raw[..colon];
                utility = raw[(colon + 1)..];
            }

            // Tailwind v4 spells the important modifier as a suffix. The escapes are gone from
            // the components, but an important class still belongs to its own family.
            if (utility.EndsWith('!'))
            {
                utility = utility[..^1];
            }

            return new ClassToken(raw, variants, TailwindConflict.Path(utility));
        }

        /// <summary>
        /// The last colon that separates variants, ignoring colons inside arbitrary variants
        /// such as <c>in-[a:hover,button:hover]</c> and <c>[&_svg:not([class*='size-'])]</c>.
        /// </summary>
        private static int LastTopLevelColon(string value)
        {
            var brackets = 0;
            var parentheses = 0;
            var index = -1;

            for (var position = 0; position < value.Length; position++)
            {
                switch (value[position])
                {
                    case '[':
                        brackets++;
                        break;
                    case ']':
                        if (brackets > 0) brackets--;
                        break;
                    case '(':
                        if (brackets > 0) parentheses++;
                        break;
                    case ')':
                        if (parentheses > 0) parentheses--;
                        break;
                    case ':':
                        if (brackets == 0 && parentheses == 0) index = position;
                        break;
                }
            }

            return index;
        }
    }
}