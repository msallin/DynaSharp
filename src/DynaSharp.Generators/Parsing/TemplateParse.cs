namespace DynaSharp.Generators;

/// <summary>The result of parsing a key template.</summary>
/// <param name="Nodes">The nodes up to the first error.</param>
/// <param name="Error">The first error, or null.</param>
internal sealed record TemplateParse(IReadOnlyList<TemplateNode> Nodes, TemplateError? Error);
