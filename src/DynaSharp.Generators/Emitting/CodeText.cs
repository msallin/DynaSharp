using System.Globalization;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.Generators;

/// <summary>Spells values as the text of generated code.</summary>
internal static class CodeText
{
    /// <summary>A C# string literal: <c>a"b</c> gives <c>"a\"b"</c>.</summary>
    public static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    /// <summary>A C# string literal, or <c>null</c>.</summary>
    public static string OptionalLiteral(string? text) => text is null ? "null" : Literal(text);

    /// <summary>A whole number as text, the same on every build machine.</summary>
    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A UTF-8 literal: <c>pk</c> gives <c>"pk"u8</c>.</summary>
    public static string Utf8(string text) => Literal(text) + "u8";

    /// <summary>Text for a documentation comment, which an attribute name could break with a line feed and a template with <c>&lt;</c> or <c>&amp;</c>.</summary>
    public static string Xml(string text) => SymbolDisplay.FormatLiteral(text, quote: false).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
