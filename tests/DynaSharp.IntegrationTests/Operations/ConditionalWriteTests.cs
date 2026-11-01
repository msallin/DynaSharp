using System.Net;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>Conditional writes: when the condition holds, when it fails, and the item that comes back with the failure.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ConditionalWriteTests(Backend backend)
{
    [Test]
    public async Task PutItem_AttributeNotExistsOnNewKey_Succeeds()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first"), ConditionExpression = "attribute_not_exists(pk)" });
        var stored = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That(stored.Item!["v"].AsString()).IsEqualTo("first");
    }

    [Test]
    public async Task PutItem_AttributeNotExistsOnExistingKey_ThrowsConditionalCheckFailedWithTheStoredItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first") });

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = Items.Item("a", "second"),
            ConditionExpression = "attribute_not_exists(pk)",
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.StatusCode, failure.Item!["v"].AsString())).IsEqualTo(("ConditionalCheckFailedException", HttpStatusCode.BadRequest, "first"));
    }

    [Test]
    public async Task PutItem_ConditionFailsWithoutReturnValuesOnConditionCheckFailure_CarriesNoItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first") });

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = Items.Item("a", "second"),
            ConditionExpression = "attribute_not_exists(pk)",
        }));

        // Assert
        await Assert.That(failure.Item).IsNull();
    }

    [Test]
    public async Task PutItem_ConditionFails_LeavesTheStoredItemUnchanged()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first") });
        await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = Items.Item("a", "second"),
            ConditionExpression = "attribute_not_exists(pk)",
        }));

        // Act
        var stored = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That(stored.Item!["v"].AsString()).IsEqualTo("first");
    }

    [Test]
    public async Task UpdateItem_VersionMismatch_ThrowsConditionalCheckFailedWithTheStoredItem()
    {
        // Arrange: optimistic locking on a version attribute
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("a", "data");
        item["version"] = AttributeValue.FromNumber(3);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET v = :new, version = :next",
            ConditionExpression = "version = :expected",
            ExpressionAttributeValues = Items.Values((":new", "changed"), (":next", AttributeValue.FromNumber(3)), (":expected", AttributeValue.FromNumber(2))),
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));

        // Assert
        await Assert.That((failure.Item!["version"].AsNumber(), failure.Item["v"].AsString())).IsEqualTo(("3", "data"));
    }

    [Test]
    public async Task UpdateItem_VersionMatches_AppliesTheUpdate()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("a", "data");
        item["version"] = AttributeValue.FromNumber(3);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET v = :new, version = :next",
            ConditionExpression = "version = :expected",
            ExpressionAttributeValues = Items.Values((":new", "changed"), (":next", AttributeValue.FromNumber(4)), (":expected", AttributeValue.FromNumber(3))),
            ReturnValues = ReturnValue.AllNew,
        });

        // Assert
        await Assert.That((response.Attributes!["v"].AsString(), response.Attributes["version"].AsNumber())).IsEqualTo(("changed", "4"));
    }

    [Test]
    public async Task UpdateItem_ConditionOnMissingItem_ThrowsConditionalCheckFailedWithoutItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("missing"),
            UpdateExpression = "SET v = :v",
            ConditionExpression = "attribute_exists(pk)",
            ExpressionAttributeValues = Items.Values((":v", "x")),
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));

        // Assert
        await Assert.That(failure.Item).IsNull();
    }

    [Test]
    public async Task DeleteItem_ConditionFails_ThrowsConditionalCheckFailedWithTheStoredItemAndKeepsIt()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("a", "data");
        item["locked"] = AttributeValue.FromBool(true);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(() => sandbox.Client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            ConditionExpression = "locked = :no",
            ExpressionAttributeValues = Items.Values((":no", AttributeValue.FromBool(false))),
            ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
        }));
        var stored = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That((failure.Item!["locked"].AsBool(), stored.Item is not null)).IsEqualTo((true, true));
    }

    [Test]
    public async Task DeleteItem_ConditionHolds_RemovesTheItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("a", "data");
        item["locked"] = AttributeValue.FromBool(false);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        await sandbox.Client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            ConditionExpression = "locked = :no",
            ExpressionAttributeValues = Items.Values((":no", AttributeValue.FromBool(false))),
        });
        var stored = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That(stored.Item).IsNull();
    }

    [Test]
    public async Task PutItem_TwoWritersWithAttributeNotExists_ExactlyOneWins()
    {
        // Arrange: the "claim a key" pattern; both requests race for the same key
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        async Task<bool> TryClaimAsync(string owner)
        {
            try
            {
                await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("lock", owner), ConditionExpression = "attribute_not_exists(pk)" });
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        // Act
        var results = await Task.WhenAll(TryClaimAsync("first"), TryClaimAsync("second"));

        // Assert
        await Assert.That(results.Count(static won => won)).IsEqualTo(1);
    }
}
