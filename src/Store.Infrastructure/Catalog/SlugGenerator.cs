using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Store.Infrastructure.Catalog;

/// <summary>
/// Turns a display name into a clean, stable URL slug.
/// </summary>
/// <remarks>
/// Written to fix three concrete defects in the legacy store's slugs:
/// <list type="number">
///   <item>
///     <b>Doubled separators.</b> <c>"Ready / Canned Food"</c> became
///     <c>ready---canned-food</c>, because each of <c>' '</c>, <c>'/'</c>, <c>' '</c> was replaced
///     with a hyphen independently. Here, any run of separator characters collapses to a single hyphen.
///   </item>
///   <item>
///     <b>Random numeric suffixes.</b> Legacy appended a random number to every slug
///     (<c>frozen-320</c>) to dodge collisions it never actually checked for. Uniqueness is a
///     database concern, enforced by a unique index; collisions are resolved here with a readable
///     <c>-2</c>, <c>-3</c> suffix, and only when a collision genuinely exists.
///   </item>
///   <item>
///     <b>Accents and non-ASCII.</b> Normalised rather than dropped, so <c>"Crème"</c> becomes
///     <c>creme</c> instead of <c>crme</c>.
///   </item>
/// </list>
/// </remarks>
public static partial class SlugGenerator
{
    private const int MaxLength = 200;

    /// <summary>Anything that is not a lowercase letter, digit or hyphen.</summary>
    [GeneratedRegex(@"[^a-z0-9\-]+", RegexOptions.Compiled)]
    private static partial Regex NonSlugChars();

    /// <summary>A run of two or more hyphens — the legacy <c>---</c> bug.</summary>
    [GeneratedRegex(@"-{2,}", RegexOptions.Compiled)]
    private static partial Regex RepeatedHyphens();

    /// <summary>Generates a slug. Returns <c>"item"</c> if the input reduces to nothing.</summary>
    public static string Generate(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "item";
        }

        // Decompose accented characters into base letter + combining mark, then drop the marks.
        // "Crème brûlée" -> "creme brulee" rather than "crme brle".
        var normalised = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalised.Length);

        foreach (var c in normalised)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        var slug = builder.ToString().Normalize(NormalizationForm.FormC);

        // Ampersand is worth spelling out — "Salt & Pepper" reads better as "salt-and-pepper"
        // than "salt-pepper".
        slug = slug.Replace("&", " and ");

        // Every remaining run of punctuation or whitespace becomes a single hyphen. Doing this in
        // one pass over a character class is what prevents the doubled-separator bug.
        slug = NonSlugChars().Replace(slug, "-");
        slug = RepeatedHyphens().Replace(slug, "-");
        slug = slug.Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');

            // Avoid cutting mid-word when a word boundary is close to the limit.
            var lastHyphen = slug.LastIndexOf('-');

            if (lastHyphen > MaxLength - 30)
            {
                slug = slug[..lastHyphen];
            }
        }

        return slug.Length == 0 ? "item" : slug;
    }

    /// <summary>
    /// Generates a slug guaranteed unique against <paramref name="existsAsync"/>, appending
    /// <c>-2</c>, <c>-3</c>… only when a real collision occurs.
    /// </summary>
    /// <param name="desired">Caller-supplied slug, or null to derive one from <paramref name="fallbackName"/>.</param>
    /// <param name="fallbackName">Display name used when no slug is supplied.</param>
    /// <param name="existsAsync">Checks whether a slug is already taken, excluding the row being edited.</param>
    public static async Task<string> GenerateUniqueAsync(
        string? desired,
        string fallbackName,
        Func<string, CancellationToken, Task<bool>> existsAsync,
        CancellationToken ct = default)
    {
        var baseSlug = Generate(string.IsNullOrWhiteSpace(desired) ? fallbackName : desired);

        if (!await existsAsync(baseSlug, ct))
        {
            return baseSlug;
        }

        // Bounded: a name colliding 100 times means something is wrong upstream, and an unbounded
        // loop here would be a denial-of-service vector on bulk import.
        for (var suffix = 2; suffix <= 100; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";

            if (!await existsAsync(candidate, ct))
            {
                return candidate;
            }
        }

        // Last resort only — and still readable, unlike the legacy store's unconditional suffix.
        return $"{baseSlug}-{DateTime.UtcNow:yyyyMMddHHmmss}";
    }
}
