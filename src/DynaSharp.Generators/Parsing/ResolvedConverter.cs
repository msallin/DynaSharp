namespace DynaSharp.Generators;

/// <summary>A resolved converter with what its shape means for writing.</summary>
/// <param name="Model">The converters.</param>
/// <param name="Write">When the property is written.</param>
/// <param name="IsPublic">Whether every custom converter in it is public, so a public signature can name it.</param>
internal sealed record ResolvedConverter(ConverterModel Model, WriteCondition Write, bool IsPublic);
