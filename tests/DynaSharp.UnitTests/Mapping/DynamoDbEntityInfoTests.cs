using DynaSharp.Mapping;

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
}
