namespace DynaSharp.Generators;

/// <summary>The discriminator of an entity.</summary>
/// <param name="Value">The value, or null when the entity opted out.</param>
/// <param name="AttributeName">The attribute that holds it.</param>
internal sealed record DiscriminatorInfo(string? Value, string AttributeName);
