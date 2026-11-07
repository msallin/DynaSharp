using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.Generators;

/// <summary>Turns the default value of a constructor parameter into code.</summary>
internal static class DefaultLiterals
{
    /// <summary>
    /// Gives the default as code, or null when the parameter has none. <c>decimal m = 1.5m</c> gives <c>1.5m</c>, and
    /// <c>Color c = Color.Red</c> gives <c>(global::Shop.Color)(1)</c>: the parentheses keep a negative value a cast. <c>byte b = 1</c> gives <c>(byte)(1)</c>.
    /// </summary>
    public static string? Of(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        if (parameter.ExplicitDefaultValue is not { } value)
        {
            return "default";
        }

        var text = SymbolDisplay.FormatPrimitive(value, quoteStrings: true, useHexadecimalNumbers: false)!;

        // A literal of a type narrower than int is an int, and `fields.X__set ? fields.X : 1` is then an int as well, which does not convert to a byte parameter.
        if (Types.Unwrap(parameter.Type).Inner.TypeKind == TypeKind.Enum || value is byte or sbyte or short or ushort)
        {
            return $"({Names.Of(parameter.Type)})({text})";
        }

        return value switch
        {
            decimal => text + "m",
            float => FloatingPoint(text, "float", "f"),
            double => FloatingPoint(text, "double", string.Empty),
            _ => text,
        };
    }

    private static string FloatingPoint(string text, string keyword, string suffix) => text switch
    {
        "NaN" => keyword + ".NaN",
        "Infinity" => keyword + ".PositiveInfinity",
        "-Infinity" => keyword + ".NegativeInfinity",
        _ => text + suffix,
    };
}
