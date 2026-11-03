namespace DynaSharp.Generators;

/// <summary>A template bound to properties.</summary>
/// <param name="Template">
/// The template. Placeholders that failed are missing, which is safe because their problem is an error that drops the model. One whose format does not fit has the default encoding of its type instead.
/// </param>
/// <param name="Problems">What is wrong with it.</param>
/// <param name="Fallbacks">The properties whose placeholder took the default encoding, which no check across templates may take for what the format meant.</param>
internal sealed record KeyBinding(KeyTemplate Template, IReadOnlyList<KeyProblem> Problems, IReadOnlyCollection<string> Fallbacks);
