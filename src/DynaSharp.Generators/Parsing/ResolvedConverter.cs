namespace DynaSharp.Generators;

/// <summary>A resolved converter with what its shape means for writing.</summary>
/// <param name="Model">The converters.</param>
/// <param name="Write">When the property is written.</param>
/// <param name="IsPublic">Whether every custom converter in it is public, so a public signature can name it.</param>
internal sealed record ResolvedConverter(ConverterModel Model, WriteCondition Write, bool IsPublic)
{
    /// <summary>Gives the same converter for a property that a key needs, which is written whatever the converter thinks of the value.</summary>
    /// <returns>The converter, without its say on what is left out.</returns>
    public ResolvedConverter AlwaysWritten() => this with { Model = Model with { OmissionForwarder = null } };
}
