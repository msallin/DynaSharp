using Microsoft.CodeAnalysis;

namespace DynaSharp.Generators;

/// <summary>A diagnostic of a template, whose location the caller knows.</summary>
/// <param name="Descriptor">The diagnostic.</param>
/// <param name="Arguments">The message arguments.</param>
internal sealed record KeyProblem(DiagnosticDescriptor Descriptor, string[] Arguments);
