namespace DynaSharp.Generators;

/// <summary>A type with one encoding and no formats: <c>Guid</c>, <c>DateOnly</c> and custom types.</summary>
/// <param name="typeName">The type as messages show it.</param>
/// <param name="encoding">The encoding.</param>
internal sealed class FixedInput(string typeName, KeyEncoding encoding) : KeyInputType(typeName, "no format")
{
    public override FormatBinding Bind(string? format, string property) => format is null ? Encoded(encoding) : DoesNotFit(format);
}
