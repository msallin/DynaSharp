using DynaSharp.Mapping;
using DynaSharp.Requests;
using DynaSharp.UnitTests.Fluent;

namespace DynaSharp.UnitTests.Mapping;

public sealed class DynamoDbEntityInfoTests
{
    [Test]
    public async Task Constructor_RequiredValues_KeepsThem()
    {
        // Arrange
        IReadOnlyList<DynamoDbIndexInfo> indexes = [new DynamoDbIndexInfo("GSI1", "gsi1pk", "gsi1sk", false, false, ScalarAttributeType.String, ScalarAttributeType.Unknown, "Gsi1Pk")];

        // Act
        var info = new DynamoDbEntityInfo("ORDER", "kind", "pk", ScalarAttributeType.Number, "sk", ScalarAttributeType.Binary, indexes, "revision");

        // Assert
        await Assert.That((info.Discriminator, info.DiscriminatorAttributeName, info.PartitionKeyName, info.PartitionKeyType, info.SortKeyName, info.SortKeyType, info.Indexes, info.VersionAttributeName))
            .IsEqualTo(((string?)"ORDER", "kind", "pk", ScalarAttributeType.Number, (string?)"sk", ScalarAttributeType.Binary, indexes, (string?)"revision"));
    }

    [Test]
    public async Task Constructor_NoOptionalValues_HasNoneOfThem()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "_type", "pk", ScalarAttributeType.Unknown, null, ScalarAttributeType.Unknown, [], null);

        // Assert
        await Assert.That((info.VersionMissing, info.KeyTemplates.Count, info.TimeToLiveAttributeName, info.TableName)).IsEqualTo((VersionMissing.Reject, 0, (string?)null, (string?)null));
    }

    [Test]
    public async Task Constructor_OptionalValues_KeepsThem()
    {
        // Arrange, Act
        var info = new DynamoDbEntityInfo(null, "_type", "pk", ScalarAttributeType.Unknown, null, ScalarAttributeType.Unknown, [], "revision", VersionMissing.Allow, timeToLiveAttributeName: "expiresAt", tableName: "orders");

        // Assert
        await Assert.That((info.VersionMissing, info.TimeToLiveAttributeName, info.TableName)).IsEqualTo((VersionMissing.Allow, (string?)"expiresAt", (string?)"orders"));
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
    public async Task Constructor_KeyTemplates_KeepsThem()
    {
        // Arrange
        var template = new DynamoDbKeyTemplate("gsi1pk", "S#{State}", false, [new DynamoDbKeyInput("State", "state")], static values => "S#" + values[0]);

        // Act
        var info = new DynamoDbEntityInfo(null, "_type", "pk", ScalarAttributeType.Unknown, null, ScalarAttributeType.Unknown, [], null, keyTemplates: [template]);

        // Assert
        var kept = info.KeyTemplates.Single();
        var input = kept.Inputs.Single();
        await Assert.That((kept.AttributeName, kept.Text, kept.IsTableKey, input.PropertyName, input.AttributeName, input.ComputedFromAttributes, input.ToAttributeValue, input.IsCutOff, kept.Format!(["a"])))
            .IsEqualTo(("gsi1pk", "S#{State}", false, "State", (string?)"state", (IReadOnlyList<string>?)null, (Func<object, AttributeValue>?)null, false, "S#a"));
    }

    private static DynamoDbEntityInfo GeneratedInfo<T>()
        where T : IDynamoDbEntity<T> => T.Info;
}
