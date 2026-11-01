namespace DynaSharp.Generators;

/// <summary>A type around the mapped one, re-declared as <c>partial</c> in the generated file.</summary>
/// <param name="Keyword">One of <c>record</c>, <c>record struct</c>, <c>class</c>, <c>struct</c> and <c>interface</c>.</param>
/// <param name="Identifier">The name as code.</param>
internal sealed record ContainingType(string Keyword, string Identifier);
