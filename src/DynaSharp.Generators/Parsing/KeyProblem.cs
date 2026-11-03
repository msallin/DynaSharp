using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>A diagnostic of a template, whose location the caller knows.</summary>
/// <param name="Descriptor">The diagnostic.</param>
/// <param name="Arguments">The message arguments.</param>
/// <param name="KeepsModel">Whether the placeholder took the default encoding of its type instead, so the error leaves the model whole.</param>
internal sealed record KeyProblem(DiagnosticDescriptor Descriptor, string[] Arguments, bool KeepsModel = false);
