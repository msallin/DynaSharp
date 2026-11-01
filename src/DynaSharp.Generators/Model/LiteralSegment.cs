namespace DynaSharp.Generators;

/// <summary>Literal text, with <c>{{</c> and <c>}}</c> already resolved to single braces.</summary>
/// <param name="Text">The printable ASCII text.</param>
internal sealed record LiteralSegment(string Text) : TemplateSegment;
