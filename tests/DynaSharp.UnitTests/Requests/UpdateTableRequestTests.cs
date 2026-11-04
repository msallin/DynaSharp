using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class UpdateTableRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_ChangeListsAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateTableRequest { TableName = "Orders" };

        // Assert
        await Assert.That((value.AttributeDefinitions, value.GlobalSecondaryIndexUpdates)).IsEqualTo(((IReadOnlyList<AttributeDefinition>?)null, (IReadOnlyList<GlobalSecondaryIndexUpdate>?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SettingsSendNothing()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateTableRequest { TableName = "Orders" };

        // Assert
        await Assert.That((value.BillingMode, value.TableClass, value.ProvisionedThroughput, value.StreamSpecification)).IsEqualTo((BillingMode.Unspecified, TableClass.Unspecified, (ProvisionedThroughput?)null, (StreamSpecification?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_EncryptionAndOnDemandThroughputAreNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateTableRequest { TableName = "Orders" };

        // Assert
        await Assert.That((value.SSESpecification, value.OnDemandThroughput)).IsEqualTo(((SSESpecification?)null, (OnDemandThroughput?)null));
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_DeletionProtectionIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new UpdateTableRequest { TableName = "Orders" };

        // Assert
        await Assert.That(value.DeletionProtectionEnabled).IsNull();
    }
}
