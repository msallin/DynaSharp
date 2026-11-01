namespace DynaSharp.Generators;

/// <summary>A format resolved against a type: an encoding, or what is wrong.</summary>
/// <param name="Encoding">The encoding, or null.</param>
/// <param name="Problem">The problem, when there is no encoding.</param>
internal sealed record FormatBinding(KeyEncoding? Encoding, KeyProblem? Problem);
