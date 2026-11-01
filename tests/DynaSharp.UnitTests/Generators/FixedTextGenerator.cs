using Microsoft.CodeAnalysis;

namespace DynaSharp.UnitTests.Generators;

/// <summary>A generator that adds one fixed file, to test the harness without depending on what the real generator emits.</summary>
internal sealed class FixedTextGenerator(string hintName, string text) : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterPostInitializationOutput(output => output.AddSource(hintName, text));
}
