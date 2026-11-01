namespace DynaSharp.Generators;

/// <summary>What is wrong with the text of a key template.</summary>
/// <param name="Position">The index in the text, which is its length when the text ended too early.</param>
/// <param name="Message">What is wrong.</param>
/// <param name="IsLiteral">A character of a literal is not allowed; otherwise a syntax error.</param>
internal sealed record TemplateError(int Position, string Message, bool IsLiteral);
