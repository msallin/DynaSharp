using System.Globalization;
using System.Text;

namespace DynaSharp.Generators;

/// <summary>
/// The rules of <c>JsonNamingPolicy.CamelCase</c> and <c>JsonNamingPolicy.SnakeCaseLower</c>, which a generator on netstandard cannot call. A test holds them against the real policies, so this is a copy of
/// their behaviour and no second opinion on what a word is.
/// </summary>
internal static class NameCasing
{
    private enum Previous
    {
        Nothing,
        Upper,
        LowerOrDigit,
        Space,
    }

    /// <summary>
    /// Lowers the leading capitals, all but the last of a run that a word follows.
    /// <code>
    /// OrderId  orderId
    /// ID       id
    /// URLPath  urlPath
    /// IsOK     isOK
    /// </code>
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The name in camel case.</returns>
    public static string ToCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (index == 1 && !char.IsUpper(chars[index]))
            {
                break;
            }

            var hasNext = index + 1 < chars.Length;
            if (index > 0 && hasNext && !char.IsUpper(chars[index + 1]))
            {
                if (chars[index + 1] == ' ')
                {
                    chars[index] = char.ToLowerInvariant(chars[index]);
                }

                break;
            }

            chars[index] = char.ToLowerInvariant(chars[index]);
        }

        return new string(chars);
    }

    /// <summary>
    /// Lowers everything, with a <c>_</c> where a word starts.
    /// <code>
    /// OrderId     order_id
    /// XMLReader   xml_reader
    /// SHA512Hash  sha512_hash
    /// Line2Total  line2_total
    /// </code>
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The name in snake case.</returns>
    public static string ToSnakeCase(string name)
    {
        const char Separator = '_';
        var text = new StringBuilder(name.Length + (name.Length / 5));
        var previous = Previous.Nothing;
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            var category = char.GetUnicodeCategory(current);
            if (category == UnicodeCategory.UppercaseLetter)
            {
                // A run of capitals stays one word, until the last one, which starts the word that a lower case letter goes on with.
                if (previous is Previous.LowerOrDigit or Previous.Space || (previous == Previous.Upper && index + 1 < name.Length && char.IsLower(name[index + 1])))
                {
                    text.Append(Separator);
                }

                text.Append(char.ToLowerInvariant(current));
                previous = Previous.Upper;
            }
            else if (category is UnicodeCategory.LowercaseLetter or UnicodeCategory.DecimalDigitNumber)
            {
                if (previous == Previous.Space)
                {
                    text.Append(Separator);
                }

                text.Append(current);
                previous = Previous.LowerOrDigit;
            }
            else if (category == UnicodeCategory.SpaceSeparator)
            {
                // Spaces at either end go, and those between words become one separator.
                previous = previous == Previous.Nothing ? Previous.Nothing : Previous.Space;
            }
            else
            {
                text.Append(current);
                previous = Previous.Nothing;
            }
        }

        return text.ToString();
    }
}
