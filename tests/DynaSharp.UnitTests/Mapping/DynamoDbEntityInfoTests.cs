using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Mapping;

public sealed class DynamoDbEntityInfoTests
{
    [Test]
    public async Task Constructor_Defaults_UseTheDiscriminatorAttributeOfTheLibrary()
    {
        // Arrange
        IReadOnlyList<DynamoDbIndexInfo> indexes = [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false)];

        // Act
        var info = new DynamoDbEntityInfo("ORDER", "pk", "sk", indexes);

        // Assert
        await Assert.That((info.Discriminator, info.DiscriminatorAttributeName, info.PartitionKeyName, info.SortKeyName, info.Indexes, info.VersionAttributeName)).IsEqualTo(((string?)"ORDER", "_type", "pk", (string?)"sk", indexes, (string?)null));
    }

    [Test]
    public async Task Constructor_AllValues_KeepsThem()
    {
        // Arrange
        const string version = "revision";

        // Act
        var info = new DynamoDbEntityInfo(null, "pk", null, [], version, "kind");

        // Assert
        await Assert.That((info.Discriminator, info.DiscriminatorAttributeName, info.SortKeyName, info.VersionAttributeName)).IsEqualTo(((string?)null, "kind", (string?)null, (string?)version));
    }

    [Test]
    public async Task TableName_NotSet_IsNull()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "pk", null, []);

        // Assert
        await Assert.That(info.TableName).IsNull();
    }

    [Test]
    public async Task TableName_Set_KeepsIt()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "pk", null, []) { TableName = "orders" };

        // Assert
        await Assert.That(info.TableName).IsEqualTo("orders");
    }

    [Test]
    public async Task Constructor_NoKeyTypes_SaysNothingAboutThem()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "pk", "sk", [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false)]);

        // Assert
        var index = info.Indexes.Single();
        await Assert.That((info.PartitionKeyType, info.SortKeyType, index.PartitionKeyType, index.SortKeyType, index.IsLocal))
            .IsEqualTo((ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, ScalarAttributeType.Unknown, false));
    }

    [Test]
    public async Task Constructor_KeyTypes_KeepsThem()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "pk", "sk", [], partitionKeyType: ScalarAttributeType.Number, sortKeyType: ScalarAttributeType.Binary);

        // Assert
        await Assert.That((info.PartitionKeyType, info.SortKeyType)).IsEqualTo((ScalarAttributeType.Number, ScalarAttributeType.Binary));
    }

    [Test]
    public async Task Constructor_TimeToLive_KeepsItAndDefaultsToNone()
    {
        // Arrange, Act
        var (with, without) = (new DynamoDbEntityInfo(null, "pk", null, [], timeToLiveAttributeName: "expiresAt"), new DynamoDbEntityInfo(null, "pk", null, []));

        // Assert
        await Assert.That((with.TimeToLiveAttributeName, without.TimeToLiveAttributeName)).IsEqualTo(((string?)"expiresAt", (string?)null));
    }

    [Test]
    public async Task Info_GeneratedEntity_NamesTheTypesOfItsKeysAndItsLocalIndexes()
    {
        // Arrange, Act
        var info = GeneratedInfo<SchemaReading>();

        // Assert
        var index = info.Indexes.Single();
        await Assert.That((info.PartitionKeyType, info.SortKeyType, index.IsLocal, index.PartitionKeyName, index.PartitionKeyType, index.SortKeyType))
            .IsEqualTo((ScalarAttributeType.Number, ScalarAttributeType.Binary, true, "Sensor", ScalarAttributeType.Number, ScalarAttributeType.Number));
    }

    [Test]
    public async Task Info_GeneratedEntityWithoutASortKey_SaysNothingAboutOne()
    {
        // Arrange, Act
        var info = GeneratedInfo<SchemaFlat>();

        // Assert
        await Assert.That((info.PartitionKeyType, info.SortKeyName, info.SortKeyType)).IsEqualTo((ScalarAttributeType.String, (string?)null, ScalarAttributeType.Unknown));
    }

    [Test]
    public async Task Info_GeneratedIndexWithoutASortKey_SaysNothingAboutOne()
    {
        // Arrange, Act
        var index = GeneratedInfo<SchemaOrder>().Indexes.Single(static candidate => candidate.IndexName == "ByTracking");

        // Assert
        await Assert.That((index.IsLocal, index.Sparse, index.PartitionKeyType, index.SortKeyType)).IsEqualTo((false, true, ScalarAttributeType.String, ScalarAttributeType.Unknown));
    }

    [Test]
    public async Task Constructor_NoKeyTemplates_HasAnEmptyList()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "pk", null, []);

        // Assert
        await Assert.That(info.KeyTemplates).IsEmpty();
    }

    [Test]
    public async Task Constructor_KeyTemplates_KeepsThem()
    {
        // Arrange
        var template = new DynamoDbKeyTemplate("gsi1pk", "S#{State}", false, [new DynamoDbKeyInput("State", "state")], static values => "S#" + values[0]);

        // Act
        var info = new DynamoDbEntityInfo(null, "pk", null, [], keyTemplates: [template]);

        // Assert
        var kept = info.KeyTemplates.Single();
        await Assert.That((kept.AttributeName, kept.Text, kept.IsTableKey, kept.Inputs.Single(), kept.Format!(["a"])))
            .IsEqualTo(("gsi1pk", "S#{State}", false, new DynamoDbKeyInput("State", "state"), "S#a"));
    }

    private static DynamoDbEntityInfo GeneratedInfo<T>()
        where T : IDynamoDbEntity<T> => T.Info;
}
