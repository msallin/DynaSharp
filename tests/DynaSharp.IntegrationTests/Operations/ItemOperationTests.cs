using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>GetItem, PutItem, UpdateItem and DeleteItem against the emulators: what they read, write, return and leave behind.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ItemOperationTests(Backend backend)
{
    [Test]
    public async Task GetItem_MissingItem_ReturnsNullItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("nobody") });

        // Assert
        await Assert.That(response.Item).IsNull();
    }

    [Test]
    public async Task GetItem_ProjectionWithReservedWord_ReturnsOnlyTheProjectedAttributes()
    {
        // Arrange: "name" is a DynamoDB reserved word and needs a placeholder
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var item = Items.Item("user", "x");
        item["name"] = "Alice";
        item["age"] = AttributeValue.FromNumber(30);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = item });

        // Act
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest
        {
            TableName = table,
            Key = Items.Key("user"),
            ProjectionExpression = "#n, age",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#n"] = "name" },
        });

        // Assert
        await Assert.That(response.Item!.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["age", "name"]);
    }

    [Test]
    public async Task GetItem_NumberAndBinaryKeys_FindTheItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var numberTable = await sandbox.CreateTableAsync(new TableSpec(PartitionKeyType: KeyAttributeType.Number));
        var binaryTable = await sandbox.CreateTableAsync(new TableSpec(PartitionKeyType: KeyAttributeType.Binary));
        var numberKey = new Dictionary<string, AttributeValue> { ["pk"] = AttributeValue.FromNumber("-12.5") };
        var binaryKey = new Dictionary<string, AttributeValue> { ["pk"] = AttributeValue.FromBinary([0, 1, 254, 255]) };
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = numberTable, Item = new Dictionary<string, AttributeValue>(numberKey) { ["v"] = "number key" } });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = binaryTable, Item = new Dictionary<string, AttributeValue>(binaryKey) { ["v"] = "binary key" } });

        // Act
        var byNumber = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = numberTable, Key = numberKey, ConsistentRead = true });
        var byBinary = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = binaryTable, Key = binaryKey, ConsistentRead = true });

        // Assert
        await Assert.That((byNumber.Item!["v"].AsString(), byBinary.Item!["v"].AsString())).IsEqualTo(("number key", "binary key"));
    }

    [Test]
    public async Task PutItem_ExistingKey_ReplacesTheWholeItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["old"] = "gone" } });

        // Act
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["new"] = "here" } });
        var response = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That(response.Item!.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["new", "pk"]);
    }

    [Test]
    public async Task PutItem_ReturnValuesAllOld_ReturnsThePreviousItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first") });

        // Act
        var response = await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "second"), ReturnValues = ReturnValue.AllOld });

        // Assert
        await Assert.That(response.Attributes!["v"].AsString()).IsEqualTo("first");
    }

    [Test]
    public async Task PutItem_NewKeyWithReturnValuesAllOld_ReturnsNoAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first"), ReturnValues = ReturnValue.AllOld });

        // Assert
        await Assert.That(response.Attributes).IsNull();
    }

    [Test]
    public async Task PutItem_WithoutReturnValues_ReturnsNoAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first") });

        // Act
        var response = await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "second") });

        // Assert
        await Assert.That(response.Attributes).IsNull();
    }

    [Test]
    public async Task UpdateItem_SetAndRemove_ChangesOnlyTheNamedAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["keep"] = "kept", ["change"] = "before", ["drop"] = "dropped" },
        });

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET #c = :after REMOVE #d",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#c"] = "change", ["#d"] = "drop" },
            ExpressionAttributeValues = Items.Values((":after", "after")),
            ReturnValues = ReturnValue.AllNew,
        });

        // Assert
        await Assert.That(response.Attributes!.Select(static pair => (pair.Key, pair.Value.AsString())).Order()).IsEquivalentTo(
            [("change", "after"), ("keep", "kept"), ("pk", "a")]);
    }

    [Test]
    public async Task UpdateItem_AddToNumberWithUpdatedNew_ReturnsOnlyTheUpdatedAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "counter", ["hits"] = AttributeValue.FromNumber(41), ["other"] = "untouched" },
        });

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("counter"),
            UpdateExpression = "ADD hits :one",
            ExpressionAttributeValues = Items.Values((":one", AttributeValue.FromNumber(1))),
            ReturnValues = ReturnValue.UpdatedNew,
        });

        // Assert
        await Assert.That((response.Attributes!.Count, response.Attributes["hits"].AsNumber())).IsEqualTo((1, "42"));
    }

    [Test]
    public async Task UpdateItem_AddToMissingAttributeWithUpdatedNew_StartsFromZero()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act: the item does not exist either, so UpdateItem creates it
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("new-counter"),
            UpdateExpression = "ADD hits :five",
            ExpressionAttributeValues = Items.Values((":five", AttributeValue.FromNumber(5))),
            ReturnValues = ReturnValue.UpdatedNew,
        });
        var stored = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("new-counter"), ConsistentRead = true });

        // Assert
        await Assert.That((response.Attributes!["hits"].AsNumber(), stored.Item!["hits"].AsNumber())).IsEqualTo(("5", "5"));
    }

    [Test]
    public async Task UpdateItem_UpdatedOld_ReturnsThePreviousValueOfTheUpdatedAttribute()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["status"] = "pending", ["other"] = "untouched" },
        });

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET #s = :done",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
            ExpressionAttributeValues = Items.Values((":done", "done")),
            ReturnValues = ReturnValue.UpdatedOld,
        });

        // Assert
        await Assert.That((response.Attributes!.Count, response.Attributes["status"].AsString())).IsEqualTo((1, "pending"));
    }

    [Test]
    public async Task UpdateItem_ListAppendAndIfNotExists_AppendsAndInitializesOnce()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["log"] = AttributeValue.FromList(["first"]) },
        });
        var update = new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET #log = list_append(#log, :entry), created = if_not_exists(created, :now)",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#log"] = "log" },
            ExpressionAttributeValues = Items.Values((":entry", AttributeValue.FromList(["second"])), (":now", "today")),
            ReturnValues = ReturnValue.AllNew,
        };

        // Act: the second update appends again but keeps the first value of "created"
        await sandbox.Client.UpdateItemAsync(update);
        var response = await sandbox.Client.UpdateItemAsync(update with { ExpressionAttributeValues = Items.Values((":entry", AttributeValue.FromList(["third"])), (":now", "later")) });

        // Assert
        await Assert.That(response.Attributes!["log"]).IsEqualTo(AttributeValue.FromList(["first", "second", "third"]));
        await Assert.That(response.Attributes["created"].AsString()).IsEqualTo("today");
    }

    [Test]
    public async Task UpdateItem_AddAndDeleteSetMembers_ChangesTheSet()
    {
        // Arrange: ADD and DELETE on the same attribute in one expression overlap, so they are two updates
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest
        {
            TableName = table,
            Item = new Dictionary<string, AttributeValue> { ["pk"] = "a", ["tags"] = AttributeValue.FromStringSet(["red", "green"]) },
        });

        // Act
        await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "ADD tags :blue",
            ExpressionAttributeValues = Items.Values((":blue", AttributeValue.FromStringSet(["blue"]))),
        });
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "DELETE tags :green",
            ExpressionAttributeValues = Items.Values((":green", AttributeValue.FromStringSet(["green"]))),
            ReturnValues = ReturnValue.AllNew,
        });

        // Assert
        await Assert.That(response.Attributes!["tags"]).IsEqualTo(AttributeValue.FromStringSet(["red", "blue"]));
    }

    [Test]
    public async Task UpdateItem_AddAndDeleteOnTheSameAttributeInOneExpression_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "ADD tags :blue DELETE tags :green",
            ExpressionAttributeValues = Items.Values((":blue", AttributeValue.FromStringSet(["blue"])), (":green", AttributeValue.FromStringSet(["green"]))),
        }));

        // Assert
        await Assert.That(failure.Message).Contains("overlap", StringComparison.Ordinal);
    }

    [Test]
    public async Task UpdateItem_NestedPath_ChangesOnlyTheNestedValue()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var address = AttributeValue.FromMap([KeyValuePair.Create<string, AttributeValue>("city", "Bern"), KeyValuePair.Create<string, AttributeValue>("zip", "3000")]);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", address) });

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET v.city = :city",
            ExpressionAttributeValues = Items.Values((":city", "Zurich")),
            ReturnValues = ReturnValue.AllNew,
        });

        // Assert
        await Assert.That(response.Attributes!["v"].AsMap().Select(static pair => (pair.Key, pair.Value.AsString())).Order()).IsEquivalentTo(
            [("city", "Zurich"), ("zip", "3000")]);
    }

    [Test]
    public async Task DeleteItem_ExistingItem_RemovesItAndReturnsItWithAllOld()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "value") });

        // Act
        var deleted = await sandbox.Client.DeleteItemAsync(new DeleteItemRequest { TableName = table, Key = Items.Key("a"), ReturnValues = ReturnValue.AllOld });
        var after = await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key("a"), ConsistentRead = true });

        // Assert
        await Assert.That((deleted.Attributes!["v"].AsString(), after.Item)).IsEqualTo(("value", null));
    }

    [Test]
    public async Task DeleteItem_MissingItem_SucceedsAndReturnsNoAttributes()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.DeleteItemAsync(new DeleteItemRequest { TableName = table, Key = Items.Key("never-existed"), ReturnValues = ReturnValue.AllOld });

        // Assert
        await Assert.That(response.Attributes).IsNull();
    }

    [Test]
    public async Task UpdateItem_ItemCollectionMetricsRequestedOnTableWithoutLocalIndex_ReturnsNoMetrics()
    {
        // Arrange: item collection metrics exist only for tables with a local secondary index
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET x = :x",
            ExpressionAttributeValues = Items.Values((":x", "1")),
            ReturnItemCollectionMetrics = ReturnItemCollectionMetrics.Size,
        });

        // Assert
        await Assert.That(response.ItemCollectionMetrics).IsNull();
    }
}
