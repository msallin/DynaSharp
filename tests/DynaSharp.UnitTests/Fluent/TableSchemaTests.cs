using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Fluent;

// The projection of an index: what the request asks for, from the Include lists and the discriminators of the entities that declare it, and the check of a table that exists against it.
public sealed class TableSchemaTests
{
    private static readonly DynamoDbIndexInfo KeysOnly = Index([]);

    [Test]
    [Arguments("projected", "_type", null, "")]
    [Arguments("projected, twice", "_type", "_type", "")]
    [Arguments("projected, beside an entity without one", "_type", "none", "")]
    [Arguments("a second one not projected", "_type", "kind", "kind")]
    [Arguments("not projected", "label", null, "label")]
    public async Task Check_IncludeProjection_ReportsTheFirstDiscriminatorItLacks(string name, string? first, string? second, string lacks)
    {
        // Arrange (GSI1 includes _type only)
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(KeysOnly, first));
        if (second is not null)
        {
            schema.Add("Second", Entity(KeysOnly, second == "none" ? null : second));
        }

        var found = Described(Included("_type"));

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
        schema.Add("First", Entity(Index(null), "_type"));

        // Act
        var drift = schema.Check("Shop", Described(new IndexProjection { ProjectionType = ProjectionType.KeysOnly }));

        // Assert
        await Assert.That((drift.Single().Kind, drift.Single().IndexName)).IsEqualTo((TableDriftKind.IndexDiffers, (string?)"GSI1"));
    }

    [Test]
    [Arguments(null, "_type", "ALL")]
    [Arguments("", null, "KEYS_ONLY")]
    [Arguments("", "_type", "INCLUDE (_type)")]
    [Arguments("total,pk,gsk,total", "_type", "INCLUDE (total, _type)")]
    [Arguments("", "gpk", "KEYS_ONLY")]
    public async Task ToRequest_Include_ProjectsWhatItNamesAndTheDiscriminatorWithoutTheKeys(string? include, string? discriminator, string expected)
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(Index(include?.Split(',', StringSplitOptions.RemoveEmptyEntries)), discriminator));

        // Act
        var request = schema.ToRequest("Shop", null, null);

        // Assert
        await Assert.That(Describe(request.GlobalSecondaryIndexes!.Single().Projection)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("total", "status", "INCLUDE (total, status, _type)")]
    [Arguments("total", null, "ALL")]
    [Arguments(null, "total", "ALL")]
    public async Task ToRequest_TwoEntitiesOfOneIndex_ProjectWhatEitherNames(string? first, string? second, string expected)
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(Index(first is null ? null : [first]), "_type"));
        schema.Add("Second", Entity(Index(second is null ? null : [second]), "_type"));

        // Act
        var request = schema.ToRequest("Shop", null, null);

        // Assert
        await Assert.That(Describe(request.GlobalSecondaryIndexes!.Single().Projection)).IsEqualTo(expected);
    }

    [Test]
    public async Task ToRequest_LocalIndexWithInclude_ProjectsIt()
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(new DynamoDbIndexInfo("LSI1", "pk", "lsk", false, true, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null, ["total"]), "_type"));

        // Act
        var request = schema.ToRequest("Shop", null, null);

        // Assert
        await Assert.That(Describe(request.LocalSecondaryIndexes!.Single().Projection)).IsEqualTo("INCLUDE (total, _type)");
    }

    [Test]
    [Arguments("ALL where it names some", "ALL", "", "so every write copies the other attributes into it as well")]
    [Arguments("more than it names", "INCLUDE", "total,_type,note", "so every write copies note into it as well")]
    [Arguments("less than it names", "INCLUDE", "_type", "so it lacks total.")]
    [Arguments("INCLUDE without attributes", "INCLUDE", "", "with a projection of INCLUDE (), which lacks _type")]
    public async Task Check_ProjectionOfTheTable_ReportsWhatDiffersFromInclude(string name, string type, string attributes, string differs)
    {
        // Arrange (the entity includes total, and the discriminator joins it)
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(Index(["total"]), "_type"));

        // Act
        var drift = schema.Check("Shop", Described(Projection(type, attributes)));

        // Assert
        await Assert.That(drift.Single().Description).Contains(differs).Because(name);
    }

    [Test]
    [Arguments("the same, in another order", "INCLUDE", "_type,total")]
    [Arguments("an unknown projection", "", "")]
    public async Task Check_ProjectionOfTheTableThatFitsInclude_ReportsNothing(string name, string type, string attributes)
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(Index(["total"]), "_type"));

        // Act
        var drift = schema.Check("Shop", Described(Projection(type, attributes)));

        // Assert
        await Assert.That(drift).IsEmpty().Because(name);
    }

    [Test]
    public async Task Check_KeysOnlyOverKeysOnly_ReportsNothing()
    {
        // Arrange (no discriminator, so the keys alone)
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(KeysOnly, null));

        // Act
        var drift = schema.Check("Shop", Described(new IndexProjection { ProjectionType = ProjectionType.KeysOnly }));

        // Assert
        await Assert.That(drift).IsEmpty();
    }

    [Test]
    public async Task Check_AllDeclaredOverInclude_SaysItLacksTheOtherAttributes()
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(Index(null), "_type"));

        // Act
        var drift = schema.Check("Shop", Described(Included("_type")));

        // Assert
        await Assert.That(drift.Single().Description).IsEqualTo(
            "The table Shop has the index GSI1 with a projection of INCLUDE (_type), and the entities declare ALL, so it lacks the other attributes. A typed read of it fails for a required property it lacks. "
            + "An index keeps the projection it was created with: delete it and create it again with ALL.");
    }

    [Test]
    public async Task Check_LocalIndexThatDiffers_SaysOnlyANewTableChangesIt()
    {
        // Arrange
        var schema = new TableSchema("Shop", "pk", "sk");
        schema.Add("First", Entity(new DynamoDbIndexInfo("LSI1", "pk", "lsk", false, true, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null, ["total"]), "_type"));
        var found = new TableDescription
        {
            KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
            LocalSecondaryIndexes = [new LocalSecondaryIndexDescription { IndexName = "LSI1", KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("lsk", KeyType.Range)], Projection = new IndexProjection { ProjectionType = ProjectionType.All } }],
        };

        // Act
        var drift = schema.Check("Shop", found);

        // Assert
        await Assert.That(drift.Single().Description).EndsWith("A local index keeps the projection it was created with: only a new table with the index changes it with INCLUDE (total, _type).");
    }

    private static DynamoDbIndexInfo Index(IReadOnlyList<string>? include) => new DynamoDbIndexInfo("GSI1", "gpk", "gsk", false, false, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, null, include);

    private static DynamoDbEntityInfo Entity(DynamoDbIndexInfo index, string? discriminatorAttributeName) =>
        discriminatorAttributeName is null
            ? new DynamoDbEntityInfo(null, "_type", "pk", ScalarAttributeType.Unknown, "sk", ScalarAttributeType.Unknown, [index], null)
            : new DynamoDbEntityInfo("X", discriminatorAttributeName, "pk", ScalarAttributeType.Unknown, "sk", ScalarAttributeType.Unknown, [index], null);

    private static IndexProjection Projection(string type, string? attributes) => new()
    {
        ProjectionType = type switch { "ALL" => ProjectionType.All, "INCLUDE" => ProjectionType.Include, _ => ProjectionType.Unknown },
        NonKeyAttributes = string.IsNullOrEmpty(attributes) ? null : attributes.Split(','),
    };

    private static IndexProjection Included(params string[] attributes) => new() { ProjectionType = ProjectionType.Include, NonKeyAttributes = attributes };

    private static string Describe(IndexProjection projection) => projection.ProjectionType switch
    {
        ProjectionType.All => "ALL",
        ProjectionType.KeysOnly => "KEYS_ONLY",
        _ => $"INCLUDE ({string.Join(", ", projection.NonKeyAttributes ?? [])})",
    };

    private static TableDescription Described(IndexProjection projection) => new()
    {
        KeySchema = [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)],
        GlobalSecondaryIndexes = [new GlobalSecondaryIndexDescription { IndexName = "GSI1", KeySchema = [new KeySchemaElement("gpk", KeyType.Hash), new KeySchemaElement("gsk", KeyType.Range)], Projection = projection }],
    };
}
