using System.Globalization;
using System.Text;

namespace DynaSharp.Generators;

/// <summary>The rules for DynamoDB attribute names.</summary>
internal static class AttributeNames
{
    public const int MaxLength = 65535;

    public const int MaxKeyLength = 255;

    /// <summary>Says what is wrong with a name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="reserved">The name of the discriminator attribute, or null.</param>
    /// <param name="maxBytes">The limit in bytes of UTF-8.</param>
    /// <returns>The reason, or null for a valid name.</returns>
    public static string? Problem(string name, string? reserved, int maxBytes)
    {
        if (name.Length == 0)
        {
            return "it is empty";
        }

        if (!IsWellFormed(name))
        {
            return "it is not well-formed UTF-16";
        }

        if (Encoding.UTF8.GetByteCount(name) > maxBytes)
        {
            return "it is longer than " + maxBytes.ToString(CultureInfo.InvariantCulture) + " bytes of UTF-8";
        }

        return name == reserved ? "it is reserved for the discriminator" : null;
    }

    private static bool IsWellFormed(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsSurrogate(text[index]))
            {
                if (!char.IsSurrogatePair(text, index))
                {
                    return false;
                }

                index++;
            }
        }

        return true;
    }
}
