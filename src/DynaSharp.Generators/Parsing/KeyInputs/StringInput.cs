namespace DynaSharp.Generators;

/// <summary><c>string</c>: delimiters are rejected by default, and <c>raw</c> takes them, in a value that ends the template.</summary>
/// <param name="typeName">The type as messages show it.</param>
internal sealed class StringInput(string typeName) : KeyInputType(typeName, "no format, or raw at the end of the template")
{
    private const string Raw = "raw";

    public override FormatBinding Bind(string? format, string property) => format switch
    {
        null => Encoded(new StringEncoding()),
        Raw => Encoded(new StringEncoding(IsRaw: true)),
        _ => DoesNotFit(format),
    };
}
