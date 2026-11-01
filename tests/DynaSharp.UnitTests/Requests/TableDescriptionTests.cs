using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class TableDescriptionTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_KeySchemaIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.KeySchema).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_AttributeDefinitionsIsEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.AttributeDefinitions).IsEmpty();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_TableStatusIsUnknown()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.TableStatus).IsEqualTo(TableStatus.Unknown);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_OptionalMembersAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That((description.TableArn, description.CreationDateTime)).IsEqualTo(((string?)null, (DateTimeOffset?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_IndexListsAreEmpty()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.LocalSecondaryIndexes.Count + description.GlobalSecondaryIndexes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamMembersAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That((description.StreamSpecification, description.LatestStreamArn, description.LatestStreamLabel)).IsEqualTo(((StreamSpecification?)null, (string?)null, (string?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_BillingModeSummaryIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.BillingModeSummary).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_DeletionProtectionIsOff()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That(description.DeletionProtectionEnabled).IsFalse();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_CapacityIsNullAndTableClassUnspecified()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = new TableDescription { TableName = "Orders" };

        // Assert
        await Assert.That((description.ProvisionedThroughput, description.TableClass)).IsEqualTo(((ProvisionedThroughputDescription?)null, TableClass.Unspecified));
    }

    [Test]
    public async Task Constructor_AllMembers_KeepsKeySchemaInOrder()
    {
        // Arrange
        KeySchemaElement[] keySchema = [new("pk", KeyType.Hash), new("sk", KeyType.Range)];

        // Act
        var description = new TableDescription { TableName = "Orders", KeySchema = keySchema };

        // Assert
        await Assert.That(description.KeySchema).IsEquivalentTo(keySchema);
    }
}
