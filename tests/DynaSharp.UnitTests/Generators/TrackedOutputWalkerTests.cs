using System.Collections.Immutable;
using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>The reflection walk that guards the generator tracked outputs, checked against outputs whose faults are known.</summary>
public sealed class TrackedOutputWalkerTests
{
    [Test]
    public async Task Violations_NameNumberAndEnum_ReportsNothing()
    {
        // Arrange
        var output = new Clean("Order", 3, LanguageVersion.CSharp11);

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Violations_Null_ReportsNothing()
    {
        // Arrange, Act
        var violations = TrackedOutputWalker.Violations(null);

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Violations_SymbolInsideATuple_NamesThePathToIt()
    {
        // Arrange
        var output = (new WithSymbol("x", ObjectSymbol()), LanguageVersion.Latest);

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).StartsWith("output.Item1.<Symbol>k__BackingField: ");
    }

    [Test]
    public async Task Violations_Compilation_IsReported()
    {
        // Arrange
        var compilation = GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest);

        // Act
        var violations = TrackedOutputWalker.Violations(compilation);

        // Assert
        await Assert.That(violations.Single()).EndsWith("is a Compilation");
    }

    [Test]
    public async Task Violations_SyntaxTreeLocation_IsReported()
    {
        // Arrange
        var tree = CSharpSyntaxTree.ParseText("class A { }");
        var output = new WithLocation(Location.Create(tree, new Microsoft.CodeAnalysis.Text.TextSpan(0, 5)));

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).EndsWith("is bound to a syntax tree");
    }

    [Test]
    public async Task Violations_LocationInAnExternalFile_IsAllowed()
    {
        // Arrange
        var output = new WithLocation(Location.Create("A.cs", default, default));

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Violations_PlainArray_IsReported()
    {
        // Arrange
        var output = new WithArray(["a"]);

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).EndsWith("String[] compares by reference, wrap it in EquatableArray<T>");
    }

    [Test]
    public async Task Violations_ImmutableArray_IsReported()
    {
        // Arrange
        var output = new WithImmutableArray(["a"]);

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).EndsWith("ImmutableArray`1 compares by reference, wrap it in EquatableArray<T>");
    }

    [Test]
    public async Task Violations_List_IsReported()
    {
        // Arrange
        var output = new WithList(["a"]);

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).EndsWith("List`1 compares by reference, wrap it in EquatableArray<T>");
    }

    [Test]
    public async Task Violations_EquatableArrayOfStrings_ReportsNothing()
    {
        // Arrange
        var output = new WithEquatableArray(new EquatableArray<string>(["a", "b"]));

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Violations_SymbolInsideAnEquatableArray_NamesTheElement()
    {
        // Arrange
        var output = new WithEquatableSymbols(new EquatableArray<ISymbol>([ObjectSymbol()]));

        // Act
        var violations = TrackedOutputWalker.Violations(output);

        // Assert
        await Assert.That(violations.Single()).StartsWith("output.<Symbols>k__BackingField[0]: ");
    }

    [Test]
    public async Task Violations_ObjectGraphWithACycle_Terminates()
    {
        // Arrange
        var cycle = new Cycle();
        cycle.Next = cycle;

        // Act
        var violations = TrackedOutputWalker.Violations(cycle);

        // Assert
        await Assert.That(violations).IsEmpty();
    }

    private static INamedTypeSymbol ObjectSymbol() =>
        GeneratorRun.CreateCompilation(string.Empty, LanguageVersion.Latest).GetSpecialType(SpecialType.System_Object);

    private sealed record Clean(string Name, int Count, LanguageVersion Language);

    private sealed record WithSymbol(string Name, ISymbol Symbol);

    private sealed record WithArray(string[] Names);

    private sealed record WithImmutableArray(ImmutableArray<string> Names);

    private sealed record WithEquatableArray(EquatableArray<string> Names);

    private sealed record WithList(List<string> Names);

    private sealed record WithEquatableSymbols(EquatableArray<ISymbol> Symbols);

    private sealed record WithLocation(Location Where);

    private sealed record Cycle
    {
        public Cycle? Next { get; set; }
    }
}
