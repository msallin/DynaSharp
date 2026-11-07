using System.Globalization;

namespace DynaSharp.Generators;

/// <summary>DynamoDB's rules for the name of a table or an index, which are the same: 3 to 255 of the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'.</summary>
internal static class ResourceNames
{
    private const int MinLength = 3;

    private const int MaxLength = 255;

    /// <summary>Says what DynamoDB would reject about the name of a table or an index.</summary>
    /// <param name="name">The name.</param>
    /// <param name="kind">What the name is of, <c>table</c> or <c>index</c>, for the reason.</param>
    /// <returns>The reason, or null for a valid name.</returns>
    public static string? Problem(string name, string kind)
    {
        if (name.Length == 0)
        {
            return "it is empty";
        }

        if (name.Length is < MinLength or > MaxLength)
        {
            return $"it has {name.Length.ToString(CultureInfo.InvariantCulture)} characters, and a {kind} name has 3 to 255";
        }

        foreach (var character in name)
        {
            if (!IsAllowed(character))
            {
                return $"'{character}' is not allowed, only the letters a-z and A-Z, the digits 0-9, '_', '-' and '.'";
            }
        }

        return null;
    }

    private static bool IsAllowed(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-' or '.';
}
