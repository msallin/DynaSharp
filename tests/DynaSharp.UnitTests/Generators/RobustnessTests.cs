using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Code that does not compile. An exception in a generator becomes CS8785 and removes all output of every type, so each of these must
/// come back quietly: the compiler already reports the problem.
/// </summary>
public sealed class RobustnessTests
{
    [Test]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(Missing))] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbConverter(Missing)] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbConverter] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbConverter(5)] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument, DynamoDbConverter(typeof(Missing))] public partial class A { public A Self { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public HashSet<Code> X { get; set; } = null!; } [DynamoDbConverter] public struct Code { }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public Missing X { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public List<Missing> X { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public Missing[] X { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public unsafe int* X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A : Missing { public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public A(Missing x) { } }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey(Missing)] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey(5)] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E", AttributeName = Missing)] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator(Missing), PartitionKey("E")] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator(5), PartitionKey("E")] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey(Missing, PartitionKey = "A")] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), GlobalIndexKey("GSI1", PartitionKey = Missing)] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey("LSI1")] public partial class E { }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("S"), LocalIndexKey] public partial class E { }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbAttribute(Missing)] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbAttribute] public int X { get; set; } }""")]
    [Arguments("""[DynamoDbEntity] public partial class E { [PartitionKey(AttributeName = Missing)] public string Id { get; set; } = ""; }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")] public partial class E { public Missing X { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("A#{X}")] public partial class E { public E(Missing x) { } public Missing X { get; } }""")]
    [Arguments("""[DynamoDbEntity, Discriminator("E"), PartitionKey("E")] public partial class E { [Version] public Missing X { get; set; } = null!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A<T> { public T X { get; set; } = default!; }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { public A { }""")]
    [Arguments("""[DynamoDbDocument] public partial class A { [DynamoDbConverter(typeof(C))] public int X { get; set; } } public readonly struct C : DynaSharp.Serialization.IDynamoDbConverter<int> { }""")]
    public async Task Run_CodeThatDoesNotCompile_NeverThrows(string source)
    {
        // Arrange
        var header = "using System.Collections.Generic;\nusing DynaSharp.Mapping;\nnamespace Shop;\n";
        var compilation = GeneratorRun.CreateCompilation(header + source, LanguageVersion.Latest);

        // Act
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGenerators(compilation).GetRunResult();

        // Assert
        await Assert.That((result.Results[0].Exception, result.Diagnostics.Any(diagnostic => diagnostic.Id == "CS8785"))).IsEqualTo(((Exception?)null, false));
    }

    [Test]
    public async Task Run_UnresolvedConverter_ReportsDyna008AndNoModel()
    {
        // Arrange
        const string Source = """[DynamoDbDocument] public partial class A { [[|DynamoDbConverter(typeof(Missing))|]] public int X { get; set; } }""";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA008 Error: Converter 'Missing' does not implement IDynamoDbConverter<int> or has no accessible definition"));
    }

    [Test]
    public async Task Run_PropertyOfAMissingType_AddsNothingToTheCompilerError()
    {
        // Arrange
        const string Source = "[DynamoDbDocument] public partial class A { public Missing X { get; set; } = null!; }";

        // Act
        var report = Analysis.Report(Source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Run_PropertyOfAMissingType_HasNoModel()
    {
        // Arrange: a step is tracked only on the way to an output, so the warning makes the result visible.
        const string Source = """
            namespace Shop
            {
                using DynaSharp.Mapping;

                [DynamoDbEntity, Discriminator("E"), PartitionKey("E"), SortKey("{N}")]
                public partial class E { public int N { get; set; } public Missing X { get; set; } = null!; }
            }
            """;

        // Act
        var result = Analysis.Results(Source, []).Single();

        // Assert
        await Assert.That((result.Type, result.Diagnostics.Single().Id)).IsEqualTo(((TypeModel?)null, "DYNA108"));
    }

    [Test]
    public async Task Run_ProblemInABaseFromMetadata_ReportsOnTheType()
    {
        // Arrange
        const string Source = """[DynamoDbDocument] public partial class [|A|] : Lib.Root { }""";
        var library = Analysis.Library("""
            namespace Lib
            {
                public class Root { public object Bad { get; set; } = new(); }
            }
            """);

        // Act
        var report = Analysis.Report(Source, LanguageVersion.Latest, library);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA003 Error: Property 'Bad' has unsupported type 'object'. Add a [DynamoDbConverter] or use a supported type."));
    }

    [Test]
    public async Task Run_ConverterProblemOnAPropertyFromMetadata_ReportsOnTheType()
    {
        // Arrange
        const string Source = """[DynamoDbDocument] public partial class [|A|] : Lib.Root { }""";
        var library = Analysis.Library("""
            namespace Lib
            {
                public class Root { [DynaSharp.Mapping.DynamoDbConverter(typeof(string))] public int Bad { get; set; } }
            }
            """);

        // Act
        var report = Analysis.Report(Source, LanguageVersion.Latest, library);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA008 Error: Converter 'string' does not implement IDynamoDbConverter<int> or has no accessible definition"));
    }

    [Test]
    public async Task Run_BadAttributeNameOnAPropertyFromMetadata_ReportsOnTheType()
    {
        // Arrange
        const string Source = """[DynamoDbDocument] public partial class [|A|] : Lib.Root { }""";
        var library = Analysis.Library("""
            namespace Lib
            {
                public class Root { [DynaSharp.Mapping.DynamoDbAttribute("")] public int Bad { get; set; } }
            }
            """);

        // Act
        var report = Analysis.Report(Source, LanguageVersion.Latest, library);

        // Assert
        await Assert.That(report).IsEqualTo(Analysis.Expect(Source, "DYNA010 Error: Attribute name '' is invalid: it is empty"));
    }

    [Test]
    public async Task Run_ProblemInABaseOfAnotherProject_HasNoLocationButDoesNotThrow()
    {
        // Arrange: in an IDE a project reference is a compilation, so the base type is source that is not in this compilation's trees.
        var other = Analysis.ProjectReference("""
            namespace Lib
            {
                public class Root { public object Bad { get; set; } = new(); }
            }
            """);
        var compilation = GeneratorRun
            .CreateCompilation("namespace Shop { [DynaSharp.Mapping.DynamoDbDocument] public partial class A : Lib.Root { } }", LanguageVersion.Latest)
            .AddReferences(other);

        // Act
        var result = GeneratorRun.CreateDriver(new DynaSharpGenerator(), LanguageVersion.Latest).RunGenerators(compilation).GetRunResult();

        // Assert
        await Assert.That((result.Diagnostics.Single().Id, result.Diagnostics.Single().Location)).IsEqualTo(("DYNA003", Location.None));
    }

    [Test]
    public async Task IsNamed_NoAttributeClass_IsFalse()
    {
        // Arrange, Act
        var named = AttributeReader.IsNamed(null, WellKnownNames.EntityAttribute);

        // Assert
        await Assert.That(named).IsFalse();
    }
}
