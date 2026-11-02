namespace DynaSharp.Generators;

/// <summary>A resolved converter with what its shape means for writing.</summary>
/// <param name="Model">The converters.</param>
/// <param name="Write">When the property is written.</param>
/// <param name="IsPublic">Whether every custom converter in it is public, so a public signature can name it.</param>
/// <param name="IsAddable">Whether the converter of the property is an <c>IAddableConverter</c>, so that a typed <c>Add</c> takes its handle.</param>
internal sealed record ResolvedConverter(ConverterModel Model, WriteCondition Write, bool IsPublic, bool IsAddable)
{
    /// <summary>Gives the same converter for a property that a key needs, which is written whatever the converter thinks of the value.</summary>
    /// <returns>The converter, without its say on what is left out.</returns>
    public ResolvedConverter AlwaysWritten() => this with { Model = Model with { OmissionForwarder = null } };

    /// <summary>Gives the same converter for a property that is a key, which is read for what it is, because an item has its key.</summary>
    /// <returns>The converter, without its say on what counts as missing.</returns>
    public ResolvedConverter AlwaysRead() => this with { Model = Model with { CountsAsMissing = false } };
}
