using DynaSharp.Generators;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fixes of DYNA029, applied in a real workspace to the diagnostics the generator reports there, and run through the generator again.</summary>
public sealed class DefaultConverterCodeFixTests
{
    private const string Converters = """

        namespace Shop
        {
            public readonly struct IntConverter : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) => writer.WriteNull();

                public static int Read(ref DynamoDbReader reader, AttributeType type) => default;
            }

            public readonly struct OtherIntConverter : IDynamoDbConverter<int>
            {
                public static void Write(DynamoDbWriter writer, int value) => writer.WriteNull();

                public static int Read(ref DynamoDbReader reader, AttributeType type) => default;
            }

            public readonly struct OptionalIntConverter : IDynamoDbConverter<int?>
            {
                public static void Write(DynamoDbWriter writer, int? value) => writer.WriteNull();

                public static int? Read(ref DynamoDbReader reader, AttributeType type) => default;
            }
        }
        """;

    private const string Second = "[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.IntConverter))]\n[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.OtherIntConverter))]\n";

    private static readonly CodeFixRun Run = new(new DefaultConverterCodeFix(), "DYNA029");

    [Test]
    public async Task Fix_SecondDefaultForAType_RemovesItsLine()
    {
        // Arrange
        var files = Files(Second);

        // Act
        var fixedFiles = await Run.FixAsync(DefaultConverterCodeFix.RemoveKey, files);

        // Assert
        await Assert.That(fixedFiles["Defaults.cs"]).IsEqualTo(Files("[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.IntConverter))]\n")[0].Item2);
    }

    [Test]
    public async Task Fix_NullableType_DropsTheQuestionMark()
    {
        // Arrange
        var files = Files("[assembly: DynamoDbDefaultConverter(typeof(int?), typeof(Shop.OptionalIntConverter))]\n");

        // Act
        var fixedFiles = await Run.FixAsync(DefaultConverterCodeFix.UnwrapKey, files);

        // Assert
        await Assert.That(fixedFiles["Defaults.cs"]).StartsWith("using DynaSharp;\nusing DynaSharp.Mapping;\nusing DynaSharp.Serialization;\n\n[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.OptionalIntConverter))]\n");
    }

    [Test]
    public async Task Fix_Remove_LeavesNoError()
    {
        // Arrange
        var files = Files(Second);

        // Act
        var fixedFiles = await Run.FixAsync(DefaultConverterCodeFix.RemoveKey, files);
        var left = await Run.LeftAsync(fixedFiles, files);

        // Assert
        await Assert.That(left).IsEmpty();
    }

    [Test]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.IntConverter))]\n[assembly: DynamoDbDefaultConverter(typeof(int), typeof(Shop.OtherIntConverter))]\n", "Remove the second default for int")]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(int?), typeof(Shop.OptionalIntConverter))]\n", "Drop the ? of int?")]
    public async Task RegisterCodeFixes_Default_OffersTheFixOfItsProblem(string defaults, string expected)
    {
        // Arrange
        var files = Files(defaults);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo([expected]);
    }

    [Test]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(System.Collections.Generic.List<>), typeof(Shop.IntConverter))]\n")]
    [Arguments("[assembly: DynamoDbDefaultConverter(typeof(System.Nullable<int>), typeof(Shop.OptionalIntConverter))]\n")]
    public async Task RegisterCodeFixes_OpenGenericOrNullableInFull_OffersNothing(string defaults)
    {
        // Arrange
        var files = Files(defaults);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_DefaultInAGeneratedFile_OffersNothing()
    {
        // Arrange
        (string, string)[] files = [("Defaults.g.cs", Files(Second)[0].Item2)];

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixAll_Document_DropsEachQuestionMark()
    {
        // Arrange
        var files = Files("[assembly: DynamoDbDefaultConverter(typeof(int?), typeof(Shop.OptionalIntConverter))]\n[assembly: DynamoDbDefaultConverter(typeof(long?), typeof(Shop.OptionalIntConverter))]\n");

        // Act
        var fixedFiles = await Run.FixAllAsync(DefaultConverterCodeFix.UnwrapKey, FixAllScope.Document, files);

        // Assert
        await Assert.That(fixedFiles["Consumer0/Defaults.cs"]).Contains("typeof(int), typeof(Shop.OptionalIntConverter))]\n[assembly: DynamoDbDefaultConverter(typeof(long), ");
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new DefaultConverterCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA029"]);
    }

    private static (string, string)[] Files(string defaults) => [("Defaults.cs", "using DynaSharp;\nusing DynaSharp.Mapping;\nusing DynaSharp.Serialization;\n\n" + defaults + Converters)];
}
