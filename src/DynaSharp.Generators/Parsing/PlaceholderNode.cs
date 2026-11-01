namespace DynaSharp.Generators;

/// <summary>A placeholder.</summary>
/// <param name="Property">The property name.</param>
/// <param name="Format">The format, or null for the default of the type.</param>
internal sealed record PlaceholderNode(string Property, string? Format) : TemplateNode;
