using DynaSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DynaSharp.UnitTests.Generators;

/// <summary>
/// Where <c>[GeneratedCode]</c> goes, which is what coverage tools exclude by. A partial declaration is one type with the code of its user, so the attribute sits on every generated member and
/// on no partial declaration: there it would exclude hand-written members too, and clash with an attribute of the user's own.
/// </summary>
public sealed class GeneratedCodeAttributeTests
{
    private const string Attribute = """[global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]""";

    private const string WithEnumAndVersion = """
        using DynaSharp.Mapping;

        public enum Status { Open, Closed }

        [DynamoDbDocument]
        public partial record Address(string Street);

        [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}"), GlobalIndexKey("ByStatus", PartitionKey = "S#{Status}")]
        public partial class E
        {
            public required string Id { get; init; }

            public required Status Status { get; init; }

            public Address? ShipTo { get; init; }

            [Version]
            public long Revision { get; init; }

            public int Total() => 1;
        }
        """;

    // Each shape is read by three tests, which look at nothing but the syntax of what is generated.
    private static readonly Dictionary<string, SharedResult<GeneratorDriverRunResult>> Runs = Shapes().ToDictionary(
        static shape => shape,
        static shape => new SharedResult<GeneratorDriverRunResult>(() => GeneratorRun.Generate(SourceOf(shape), new DynaSharpGenerator(), LanguageVersion.Latest)));

    public static IEnumerable<string> Shapes() => [.. EmitterShapes.Names(), nameof(WithEnumAndVersion)];

    [Test]
    [MethodDataSource(nameof(Shapes))]
    public async Task Run_EveryGeneratedMember_CarriesTheAttribute(string shape)
    {
        // Arrange
        var result = Runs[shape].Value;

        // Act
        var bare = Generated(result).SelectMany(MembersWithoutTheAttribute).ToList();

        // Assert
        await Assert.That(bare).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Shapes))]
    public async Task Run_PartialDeclaration_LeavesTheAttributeOut(string shape)
    {
        // Arrange
        var result = Runs[shape].Value;

        // Act
        var marked = Generated(result).SelectMany(UserTypes).Where(HasTheAttribute).Select(static type => type.Identifier.Text).ToList();

        // Assert
        await Assert.That(marked).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Shapes))]
    public async Task Run_PartialDeclaration_GetsNoFieldInitializer(string shape)
    {
        // Arrange (an initializer runs in the static constructor of the type, which no attribute on the field excludes from coverage; a constant runs nothing)
        var result = Runs[shape].Value;

        // Act
        var initialized = Generated(result)
            .SelectMany(UserTypes)
            .SelectMany(static type => type.Members.OfType<FieldDeclarationSyntax>())
            .Where(static field => !field.Modifiers.Any(SyntaxKind.ConstKeyword) && field.Declaration.Variables.Any(static variable => variable.Initializer is not null))
            .ToList();

        // Assert
        await Assert.That(initialized).IsEmpty();
    }

    [Test]
    public async Task Run_Text_NamesTheGeneratorAndItsVersion()
    {
        // Arrange, Act
        var result = Runs[nameof(WithEnumAndVersion)].Value;

        // Assert
        await Assert.That(result.Results[0].GeneratedSources[0].SourceText.ToString()).Contains(Attribute);
    }

    [Test]
    [Arguments(LanguageVersion.CSharp11)]
    [Arguments(LanguageVersion.Latest)]
    public async Task Run_AttributeOfTheUserOnTheType_DoesNotClash(LanguageVersion language)
    {
        // Arrange (the attribute allows one use, so one on the partial declaration would be CS0579 here)
        const string Source = """
            using DynaSharp.Mapping;

            [System.CodeDom.Compiler.GeneratedCode("mine", "1")]
            [DynamoDbEntity, Discriminator("E"), PartitionKey("E#{Id}")]
            public partial class E
            {
                public required string Id { get; init; }
            }
            """;

        // Act
        var output = GeneratorRun.Run(Source, new DynaSharpGenerator(), language);

        // Assert
        await Assert.That(output.Problems).IsEmpty();
    }

    private static string SourceOf(string shape) => shape == nameof(WithEnumAndVersion) ? WithEnumAndVersion : EmitterShapes.All[shape];

    private static IEnumerable<CompilationUnitSyntax> Generated(GeneratorDriverRunResult result) =>
        result.GeneratedTrees.Select(static tree => tree.GetCompilationUnitRoot());

    private static IEnumerable<TypeDeclarationSyntax> UserTypes(CompilationUnitSyntax file) =>
        file.DescendantNodes(static node => node is not TypeDeclarationSyntax || IsPartial(node))
            .OfType<TypeDeclarationSyntax>()
            .Where(static type => IsPartial(type));

    private static bool IsPartial(SyntaxNode node) => node is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword);

    // What a partial declaration of the user holds, and the converters of enums at the end of the file. Members of a generated class fall under the class.
    private static IEnumerable<string> MembersWithoutTheAttribute(CompilationUnitSyntax file)
    {
        var fileTypes = file.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(static type => type.Modifiers.Any(SyntaxKind.FileKeyword));
        var members = UserTypes(file).SelectMany(static type => type.Members).Where(static member => !IsPartial(member)).Concat(fileTypes);
        var generatedTypes = members.OfType<TypeDeclarationSyntax>().SelectMany(static type => type.Members.OfType<TypeDeclarationSyntax>());
        return members.Concat(generatedTypes).Where(static member => !HasTheAttribute(member)).Select(static member => member.ToString().Split('\n')[0]);
    }

    private static bool HasTheAttribute(MemberDeclarationSyntax member) =>
        member.AttributeLists.SelectMany(static list => list.Attributes).Any(static attribute => attribute.Name.ToString().EndsWith("GeneratedCode", StringComparison.Ordinal));
}
