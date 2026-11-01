namespace DynaSharp.Generators;

/// <summary>Literal text with doubled braces resolved. Adjacent literals are one node.</summary>
/// <param name="Text">The text.</param>
internal sealed record LiteralNode(string Text) : TemplateNode;
