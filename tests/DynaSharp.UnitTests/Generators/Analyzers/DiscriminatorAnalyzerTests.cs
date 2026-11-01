namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>DYNA026: two entities of one compilation that cannot be told apart in a table they might share.</summary>
public sealed class DiscriminatorAnalyzerTests
{
    private const string Body = " public partial class {0} { [PartitionKey] public required string Id { get; init; } }\n";

    [Test]
    public async Task Analyze_TwoEntitiesWithOneDiscriminator_ReportsTheSecond()
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty, Entity("[DynamoDbEntity, Discriminator(\"INV\")]", "First") + Entity("[DynamoDbEntity, [|Discriminator(\"INV\")|]]", "Second"));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Second", "INV", "_type", "First")));
    }

    [Test]
    public async Task Analyze_ThreeEntitiesWithOneDiscriminator_ReportsAllButTheFirst()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            string.Empty,
            Entity("[DynamoDbEntity, Discriminator(\"INV\")]", "First") + Entity("[DynamoDbEntity, [|Discriminator(\"INV\")|]]", "Second") + Entity("[DynamoDbEntity, [|Discriminator(\"INV\")|]]", "Third"));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Second", "INV", "_type", "First"), Message("Third", "INV", "_type", "First")));
    }

    [Test]
    public async Task Analyze_OneValueInOneRenamedAttribute_Reports()
    {
        // Arrange
        var source = AnalyzerRun.Member(
            string.Empty,
            Entity("[DynamoDbEntity, Discriminator(\"INV\", AttributeName = \"kind\")]", "First") + Entity("[DynamoDbEntity, [|Discriminator(\"INV\", AttributeName = \"kind\")|]]", "Second"));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEqualTo(AnalyzerRun.Expect(source, Message("Second", "INV", "kind", "First")));
    }

    [Test]
    [Arguments("""[DynamoDbEntity, Discriminator("A")]""", """[DynamoDbEntity, Discriminator("B")]""")]
    [Arguments("""[DynamoDbEntity, Discriminator("A")]""", """[DynamoDbEntity, Discriminator("A", AttributeName = "kind")]""")]
    [Arguments("""[DynamoDbEntity, Discriminator(OptOut = true)]""", """[DynamoDbEntity, Discriminator(OptOut = true)]""")]
    public async Task Analyze_EntitiesThatCanBeToldApart_ReportsNothing(string first, string second)
    {
        // Arrange
        var source = AnalyzerRun.Member(string.Empty, Entity(first, "First") + Entity(second, "Second"));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_AttributeOnATypeThatIsNoEntity_ReportsNothing()
    {
        // Arrange (DYNA012 says what is wrong with that)
        var source = AnalyzerRun.Member(string.Empty, Entity("[DynamoDbEntity, Discriminator(\"A\")]", "First") + "[Discriminator(\"A\")] public partial class Second { }\n");

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    [Test]
    public async Task Analyze_EmptyValue_ReportsNothing()
    {
        // Arrange (DYNA022 says what is wrong with that)
        var source = AnalyzerRun.Member(string.Empty, Entity("[DynamoDbEntity, Discriminator(\"\")]", "First") + Entity("[DynamoDbEntity, Discriminator(\"\")]", "Second"));

        // Act
        var report = await AnalyzerRun.ReportAsync(source);

        // Assert
        await Assert.That(report).IsEmpty();
    }

    private static string Entity(string attributes, string name) => attributes + Body.Replace("{0}", name, StringComparison.Ordinal);

    private static string Message(string entity, string value, string attribute, string other) =>
        $"DYNA026 Warning: Entity '{entity}' has the discriminator '{value}' in the attribute '{attribute}', as '{other}' has, so on one table their items cannot be told apart. Give one of them another value, unless they never share a table.";
}
