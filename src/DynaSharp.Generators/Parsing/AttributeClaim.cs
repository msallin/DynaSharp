namespace DynaSharp.Generators;

/// <summary>Who owns an attribute name.</summary>
/// <param name="Label">The owner as messages show it.</param>
/// <param name="Template">The template of a key, or null for a property.</param>
internal sealed record AttributeClaim(string Label, string? Template);
