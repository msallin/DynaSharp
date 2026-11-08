using DynaSharp.Generators;

namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>The fix of DYNA036, applied in a real workspace to what the generator reports there, and run through the generator again.</summary>
public sealed class EpochSecondsCodeFixTests
{
    private const string Entity = """[DynamoDbEntity, Discriminator(OptOut = true), PartitionKey("S#{Id}")]""";

    private static readonly CodeFixRun Run = new(new EpochSecondsCodeFix(), "DYNA036");

    [Test]
    [Arguments("    [TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n", "    [TimeToLive] [DynamoDbConverter(typeof(EpochSecondsConverter))] public System.DateTimeOffset ExpiresAt { get; set; }\n")]
    [Arguments("    [TimeToLive]\n    public System.DateTimeOffset? ExpiresAt { get; set; }\n", "    [TimeToLive]\n    [DynamoDbConverter(typeof(EpochSecondsConverter))]\n    public System.DateTimeOffset? ExpiresAt { get; set; }\n")]
    [Arguments("    [TimeToLive, System.Obsolete] public System.DateTimeOffset ExpiresAt { get; set; }\n", "    [TimeToLive, DynamoDbConverter(typeof(EpochSecondsConverter)), System.Obsolete] public System.DateTimeOffset ExpiresAt { get; set; }\n")]
    public async Task Fix_DateTimeOffset_AddsTheConverterAfterTimeToLive(string property, string expected)
    {
        // Arrange
        var files = Session(property);

        // Act
        var fixedFiles = await Run.FixAsync(EpochSecondsCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).IsEqualTo(files[0].Item2.Replace(property, expected, StringComparison.Ordinal));
    }

    [Test]
    public async Task Fix_ParameterOfARecord_AddsTheConverterInItsList()
    {
        // Arrange
        var files = Order(Entity + "\npublic partial record Session(string Id, [property: TimeToLive] System.DateTimeOffset ExpiresAt);\n");

        // Act
        var fixedFiles = await Run.FixAsync(EpochSecondsCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("Session(string Id, [property: TimeToLive, DynamoDbConverter(typeof(EpochSecondsConverter))] System.DateTimeOffset ExpiresAt);");
    }

    [Test]
    public async Task Fix_FileWithoutTheUsings_NamesTheTypesInFull()
    {
        // Arrange
        (string, string)[] files = [("Order.cs", "namespace Shop;\n\n[DynaSharp.Mapping.DynamoDbEntity, DynaSharp.Mapping.Discriminator(OptOut = true), DynaSharp.Mapping.PartitionKey(\"S#{Id}\")]\npublic partial class Session\n{\n    public required string Id { get; set; }\n    [DynaSharp.Mapping.TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n}\n")];

        // Act
        var fixedFiles = await Run.FixAsync(EpochSecondsCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("[DynaSharp.Mapping.TimeToLive] [global::DynaSharp.Mapping.DynamoDbConverter(typeof(global::DynaSharp.Serialization.EpochSecondsConverter))] public");
    }

    [Test]
    public async Task Fix_AnotherTypeTakesTheName_NamesTheConverterAttributeInFull()
    {
        // Arrange
        var files = Session("    [TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n").Select(file => (file.Item1, file.Item2 + "\npublic sealed class DynamoDbConverterAttribute(System.Type type) : System.Attribute\n{\n    public System.Type Type { get; } = type;\n}\n")).ToArray();

        // Act
        var fixedFiles = await Run.FixAsync(EpochSecondsCodeFix.Key, files);

        // Assert
        await Assert.That(fixedFiles["Order.cs"]).Contains("[TimeToLive] [global::DynaSharp.Mapping.DynamoDbConverter(typeof(EpochSecondsConverter))] public");
    }

    [Test]
    public async Task Fix_Result_HasNoDiagnosticLeft()
    {
        // Arrange
        var files = Session("    [TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n");

        // Act
        var fixedFiles = await Run.FixAsync(EpochSecondsCodeFix.Key, files);

        // Assert
        await Assert.That(await Run.LeftAsync(fixedFiles, files)).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_DateTimeOffset_OffersTheConverter()
    {
        // Arrange
        var files = Session("    [TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n");

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEquivalentTo(["Add [DynamoDbConverter(typeof(EpochSecondsConverter))]"]);
    }

    [Test]
    [Arguments("    [TimeToLive] public string ExpiresAt { get; set; } = \"\";\n")]
    [Arguments("    [TimeToLive, DynamoDbConverter(typeof(DateTimeOffsetConverter))] public System.DateTimeOffset ExpiresAt { get; set; }\n")]
    [Arguments("    [TimeToLive, DynamoDbIgnore] public System.DateTimeOffset ExpiresAt { get; set; }\n")]
    [Arguments("    [TimeToLive] public long Expiry { get; set; }\n    [TimeToLive] public System.DateTimeOffset ExpiresAt { get; set; }\n")]
    public async Task RegisterCodeFixes_NoDateTimeOffsetToConvert_OffersNothing(string property)
    {
        // Arrange
        var files = Session(property);

        // Act
        var titles = await Run.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task RegisterCodeFixes_TimeToLiveOfACompiledBase_OffersNothing()
    {
        // Arrange
        var library = Analysis.Library("namespace Lib;\n\npublic class Base\n{\n    [DynaSharp.Mapping.TimeToLive] public virtual System.DateTimeOffset ExpiresAt { get; set; }\n}\n");
        var files = Order(Entity + "\npublic partial class Session : Lib.Base\n{\n    public required string Id { get; set; }\n\n    public override System.DateTimeOffset ExpiresAt { get; set; }\n}\n");

        // Act
        var titles = await new CodeFixRun(new EpochSecondsCodeFix(), "DYNA036") { Libraries = [library] }.TitlesAsync(files);

        // Assert
        await Assert.That(titles).IsEmpty();
    }

    [Test]
    public async Task FixableDiagnosticIds_IsTheRule()
    {
        // Arrange
        var fix = new EpochSecondsCodeFix();

        // Act
        var ids = fix.FixableDiagnosticIds;

        // Assert
        await Assert.That(ids).IsEquivalentTo(["DYNA036"]);
    }

    // A file that sees the converters, as one with a time to live does.
    private static (string, string)[] Order(string declarations) => [("Order.cs", "using DynaSharp.Mapping;\nusing DynaSharp.Serialization;\n\nnamespace Shop;\n\n" + declarations)];

    private static (string, string)[] Session(string members) => Order(Entity + "\npublic partial class Session\n{\n    public required string Id { get; set; }\n" + members + "}\n");
}
