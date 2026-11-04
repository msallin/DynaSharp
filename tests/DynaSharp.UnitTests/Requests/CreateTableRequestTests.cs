using DynaSharp.Requests;

namespace DynaSharp.UnitTests.Requests;

public sealed class CreateTableRequestTests
{
    [Test]
    public async Task Constructor_OnlyRequiredMembers_LocalSecondaryIndexesIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.LocalSecondaryIndexes).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_GlobalSecondaryIndexesIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.GlobalSecondaryIndexes).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_ProvisionedThroughputIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.ProvisionedThroughput).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_StreamSpecificationIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.StreamSpecification).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_SSESpecificationIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.SSESpecification).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_OnDemandThroughputIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.OnDemandThroughput).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_TagsIsNull()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.Tags).IsNull();
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_BillingModeIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.BillingMode).IsEqualTo(BillingMode.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_TableClassIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.TableClass).IsEqualTo(TableClass.Unspecified);
    }

    [Test]
    public async Task Constructor_OnlyRequiredMembers_DeletionProtectionEnabledIsTheDefault()
    {
        // Arrange (nothing to arrange)

        // Act
        var value = new CreateTableRequest { TableName = "Users", AttributeDefinitions = [], KeySchema = [] };

        // Assert
        await Assert.That(value.DeletionProtectionEnabled).IsFalse();
    }
}
