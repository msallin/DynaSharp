using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DynaSharp.UnitTests.Generators;

/// <summary>What a generator run produced, and everything in it that a consumer with warnings as errors would trip over.</summary>
/// <param name="Result">The driver result, with the generated sources.</param>
/// <param name="Problems">Warnings and errors the generator reported, warnings and errors in the generated code, and errors anywhere else.</param>
internal sealed record GeneratorRunOutput(GeneratorDriverRunResult Result, ImmutableArray<string> Problems);
