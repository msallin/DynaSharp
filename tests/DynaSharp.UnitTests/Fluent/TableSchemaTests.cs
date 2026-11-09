using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Fluent;

// The check of the projection of an index against the discriminators of the entities that declare it, which their typed reads filter on.
public sealed class TableSchemaTests
{
    private static readonly DynamoDbIndexInfo Index = new("GSI1", "gpk", "gsk", false);

    [Test]
    [Arguments("no discriminator", null, null, "")]
    [Arguments("the partition key of the table", "pk", null, "")]
    [Arguments("the sort key of the table", "sk", null, "")]
    [Arguments("the partition key of the index", "gpk", null, "")]
    [Arguments("the sort key of the index", "gsk", null, "")]
    [Arguments("projected", "_type", null, "")]
    [Arguments("projected, twice", "_type", "_type", "")]
    [Arguments("projected, beside an entity without one", "_type", "none", "")]
    [Arguments("a second one not projected", "_type", "kind", "kind")]
    [Arguments("not projected", "label", null, "label")]
    public async Task Check_IncludeProjection_ReportsTheFirstDiscriminatorItLacks(string name, string? first, string? second, string lacks)
    {
        // Arrange (GSI1 includes _type only)
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(first));
        if (second is not null)
        {
            schema.Add("Second", Entity(second == "none" ? null : second));
        }

        var found = Described(new IndexProjection { ProjectionType = ProjectionType.Include, NonKeyAttributes = ["_type"] });

        // Act
        var drift = schema.Check("Shop", found);

        // Assert
        var lacking = drift.Select(entry => entry.Description.Contains($"which lacks {lacks}, the discriminator", StringComparison.Ordinal) ? lacks : entry.Description);
        await Assert.That(string.Join(",", lacking)).IsEqualTo(lacks).Because(name);
    }

    [Test]
    public async Task Check_KeysOnlyProjection_ReportsTheDiscriminator()
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity("_type"));

        // Act
        var drift = schema.Check("Shop", Described(new IndexProjection { ProjectionType = ProjectionType.KeysOnly }));

        // Assert
        await Assert.That((drift.Single().Kind, drift.Single().IndexName)).IsEqualTo((TableDriftKind.IndexDiffers, (string?)"GSI1"));
    }

    private static DynamoDbEntityInfo Entity(string? discriminatorAttributeName) =>
        discriminatorAttributeName is null
            ? new DynamoDbEntityInfo(null, "pk", "sk", [Index])
            : new DynamoDbEntityInfo("X", "pk", "sk", [Index], discriminatorAttributeName: discriminatorAttributeName);

    private static TableDescription Described(IndexProjection projection) => new()
    {
        KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
        GlobalSecondaryIndexes = [new GlobalSecondaryIndexDescription { IndexName = "GSI1", KeySchema = [new KeySchemaElement("gpk", KeyType.Hash), new KeySchemaElement("gsk", KeyType.Range)], Projection = projection }],
    };
}
