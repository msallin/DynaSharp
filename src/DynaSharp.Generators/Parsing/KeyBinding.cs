namespace DynaSharp.Generators;

/// <summary>A template bound to properties.</summary>
/// <param name="Template">The template. Placeholders that failed are missing, which is safe because their problem is an error.</param>
/// <param name="Problems">What is wrong with it.</param>
internal sealed record KeyBinding(KeyTemplate Template, IReadOnlyList<KeyProblem> Problems);
