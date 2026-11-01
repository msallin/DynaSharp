namespace DynaSharp.Generators;

/// <summary>A type that cannot be in a key, with any format: floats have no exact text, and the others have not been needed.</summary>
/// <param name="typeName">The type as messages show it.</param>
internal sealed class RejectedInput(string typeName) : KeyInputType(typeName)
{
    public override FormatBinding Bind(string? format, string property) =>
        new(null, new KeyProblem(Descriptors.UnsupportedKeyInput, [property, TypeName]));
}
