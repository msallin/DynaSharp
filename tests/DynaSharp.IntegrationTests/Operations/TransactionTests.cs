using System.Net;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>TransactGetItems and TransactWriteItems against the emulators: every action kind, cancellation reasons and idempotency.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class TransactionTests(Backend backend)
{
    [Test]
    public async Task TransactWriteItems_EveryActionKind_AppliesAllActions()
    {
        // Arrange: a put, an update, a delete and a condition check, the check on a second table
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var other = await sandbox.CreateTableAsync();
        var counter = Items.Item("counter", AttributeValue.FromNumber(1));
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = counter });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("doomed", "bye") });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = other, Item = Items.Item("guard", "present") });

        // Act
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("created", "hello") } },
                new TransactWriteItem
                {
                    Update = new TransactUpdate
                    {
                        TableName = table,
                        Key = Items.Key("counter"),
                        UpdateExpression = "ADD v :one",
                        ExpressionAttributeValues = Items.Values((":one", AttributeValue.FromNumber(1))),
                    },
                },
                new TransactWriteItem { Delete = new TransactDelete { TableName = table, Key = Items.Key("doomed") } },
                new TransactWriteItem { ConditionCheck = new TransactConditionCheck { TableName = other, Key = Items.Key("guard"), ConditionExpression = "attribute_exists(pk)" } },
            ],
        });
        var created = await Get(sandbox, table, "created");
        var updated = await Get(sandbox, table, "counter");
        var deleted = await Get(sandbox, table, "doomed");

        // Assert
        await Assert.That((created!["v"].AsString(), updated!["v"].AsNumber(), deleted)).IsEqualTo(("hello", "2", null));
    }

    [Test]
    public async Task TransactWriteItems_ConditionsHold_AppliesPutDeleteAndUpdateConditions()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "old") });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("b", "old") });

        // Act
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new TransactPut
                    {
                        TableName = table,
                        Item = Items.Item("fresh", "new"),
                        ConditionExpression = "attribute_not_exists(pk)",
                    },
                },
                new TransactWriteItem
                {
                    Update = new TransactUpdate
                    {
                        TableName = table,
                        Key = Items.Key("a"),
                        UpdateExpression = "SET v = :new",
                        ConditionExpression = "v = :old",
                        ExpressionAttributeValues = Items.Values((":new", "updated"), (":old", "old")),
                    },
                },
                new TransactWriteItem
                {
                    Delete = new TransactDelete
                    {
                        TableName = table,
                        Key = Items.Key("b"),
                        ConditionExpression = "v = :old",
                        ExpressionAttributeValues = Items.Values((":old", "old")),
                    },
                },
            ],
        });

        // Assert
        await Assert.That(((await Get(sandbox, table, "fresh"))!["v"].AsString(), (await Get(sandbox, table, "a"))!["v"].AsString(), await Get(sandbox, table, "b")))
            .IsEqualTo(("new", "updated", null));
    }

    [Test]
    public async Task TransactWriteItems_OneConditionFails_ThrowsTransactionCanceledWithReasonsInActionOrder()
    {
        // Arrange: action 1 passes, action 2 fails on an existing item, action 3 fails a check in another table
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var other = await sandbox.CreateTableAsync();
        var stored = Items.Item("taken", "stored");
        stored["n"] = AttributeValue.FromNumber("1.5");
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = stored });

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("never-written", "x") } },
                new TransactWriteItem
                {
                    Put = new TransactPut
                    {
                        TableName = table,
                        Item = Items.Item("taken", "replacement"),
                        ConditionExpression = "attribute_not_exists(pk)",
                        ReturnValuesOnConditionCheckFailure = ReturnValuesOnConditionCheckFailure.AllOld,
                    },
                },
                new TransactWriteItem { ConditionCheck = new TransactConditionCheck { TableName = other, Key = Items.Key("absent"), ConditionExpression = "attribute_exists(pk)" } },
            ],
        }));

        // Assert
        await Assert.That(string.Join(",", failure.CancellationReasons.Select(static reason => reason.Code))).IsEqualTo("None,ConditionalCheckFailed,ConditionalCheckFailed");
        await Assert.That(failure.CancellationReasons[0].Code).IsEqualTo("None");
        await Assert.That(failure.CancellationReasons[1].Item).IsEquivalentTo(stored);
        await Assert.That(failure.CancellationReasons[2].Item).IsNull();
        await Assert.That((failure.ErrorCode, failure.StatusCode)).IsEqualTo(("TransactionCanceledException", HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task TransactWriteItems_Canceled_AppliesNoAction()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("taken", "stored") });
        await Items.FailureOfAsync<TransactionCanceledException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("never-written", "x") } },
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("taken", "y"), ConditionExpression = "attribute_not_exists(pk)" } },
            ],
        }));

        // Act
        var written = await Get(sandbox, table, "never-written");

        // Assert
        await Assert.That(written).IsNull();
    }

    [Test]
    public async Task TransactWriteItems_ConditionFailsWithoutReturnValues_ReasonsCarryNoItem()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("taken", "stored") });

        // Act
        var failure = await Items.FailureOfAsync<TransactionCanceledException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("taken", "y"), ConditionExpression = "attribute_not_exists(pk)" } }],
        }));

        // Assert
        await Assert.That((failure.CancellationReasons.Single().Code, failure.CancellationReasons.Single().Item)).IsEqualTo(("ConditionalCheckFailed", null));
    }

    [Test]
    public async Task TransactWriteItems_SameClientRequestTokenTwice_AppliesTheTransactionOnce()
    {
        // Arrange: the first call writes v=first; a plain put then overwrites it. Repeating the token must not write again.
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var token = Guid.NewGuid().ToString("N");
        var transaction = new TransactWriteItemsRequest
        {
            ClientRequestToken = token,
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("a", "first") } }],
        };
        await sandbox.Client.TransactWriteItemsAsync(transaction);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "overwritten") });

        // Act
        await sandbox.Client.TransactWriteItemsAsync(transaction);
        var stored = await Get(sandbox, table, "a");

        // Assert
        await Assert.That(stored!["v"].AsString()).IsEqualTo("overwritten");
    }

    [Test]
    public async Task TransactWriteItems_SameTokenWithOtherActions_ThrowsIdempotentParameterMismatch()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var token = Guid.NewGuid().ToString("N");
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            ClientRequestToken = token,
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("a", "first") } }],
        });

        // Act
        var failure = await Items.FailureOfAsync<IdempotentParameterMismatchException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            ClientRequestToken = token,
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("b", "different") } }],
        }));

        // Assert
        await Assert.That((failure.ErrorCode, failure.StatusCode)).IsEqualTo(("IdempotentParameterMismatchException", HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task TransactWriteItems_WithoutToken_TheClientSuppliesOneAndTheCallSucceeds()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("a", "x") } }],
        });
        await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("b", "y") } }],
        });

        // Assert: two requests with different contents and no token of the caller do not collide
        await Assert.That(((await Get(sandbox, table, "a"))!["v"].AsString(), (await Get(sandbox, table, "b"))!["v"].AsString())).IsEqualTo(("x", "y"));
    }

    [Test]
    public async Task TransactWriteItems_TwoActionsOnOneItem_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("a", "x") } },
                new TransactWriteItem { Delete = new TransactDelete { TableName = table, Key = Items.Key("a") } },
            ],
        }));

        // Assert
        await Assert.That(failure.Message).Contains("multiple operations on one item", StringComparison.Ordinal);
    }

    [Test]
    public async Task TransactWriteItems_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = Tables.Missing, Item = Items.Item("a", "x") } }],
        }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task TransactWriteItems_Total_ReportsTwoUnitsPerWrite()
    {
        // Arrange: a transactional write costs twice a plain write
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
        {
            TransactItems = [new TransactWriteItem { Put = new TransactPut { TableName = table, Item = Items.Item("a", "x") } }],
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That((response.ConsumedCapacity.Single().TableName, response.ConsumedCapacity.Single().CapacityUnits)).IsEqualTo((table, 2.0));
    }

    [Test]
    public async Task TransactGetItems_ExistingAndMissingItems_ReturnsOneResponsePerActionInOrder()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        var other = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "first table") });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = other, Item = Items.Item("c", "second table") });

        // Act
        var response = await sandbox.Client.TransactGetItemsAsync(new TransactGetItemsRequest
        {
            TransactItems =
            [
                new TransactGetItem { Get = new TransactGet { TableName = other, Key = Items.Key("c") } },
                new TransactGetItem { Get = new TransactGet { TableName = table, Key = Items.Key("missing") } },
                new TransactGetItem { Get = new TransactGet { TableName = table, Key = Items.Key("a") } },
            ],
        });

        // Assert
        await Assert.That(response.Responses.Count).IsEqualTo(3);
        await Assert.That(response.Responses[0].Item!["v"].AsString()).IsEqualTo("second table");
        await Assert.That(response.Responses[1].Item).IsNull();
        await Assert.That(response.Responses[2].Item!["v"].AsString()).IsEqualTo("first table");
    }

    [Test]
    public async Task TransactGetItems_ProjectionExpression_ReturnsOnlyTheProjectedAttributes()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.TransactGetProjectionWithoutKeys);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "value") });

        // Act
        var response = await sandbox.Client.TransactGetItemsAsync(new TransactGetItemsRequest
        {
            TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = table, Key = Items.Key("a"), ProjectionExpression = "v" } }],
        });

        // Assert
        await Assert.That(response.Responses.Single().Item!.Keys).IsEquivalentTo(["v"]);
    }

    [Test]
    public async Task TransactGetItems_Total_ReportsTwoUnitsPerRead()
    {
        // Arrange: a transactional read of an item up to 4 KB costs 2 read units
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "x") });

        // Act
        var response = await sandbox.Client.TransactGetItemsAsync(new TransactGetItemsRequest
        {
            TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = table, Key = Items.Key("a") } }],
            ReturnConsumedCapacity = ReturnConsumedCapacity.Total,
        });

        // Assert
        await Assert.That(response.ConsumedCapacity.Single().CapacityUnits).IsEqualTo(2.0);
    }

    [Test]
    public async Task TransactGetItems_MissingTable_ThrowsResourceNotFoundException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Client.TransactGetItemsAsync(new TransactGetItemsRequest
        {
            TransactItems = [new TransactGetItem { Get = new TransactGet { TableName = Tables.Missing, Key = Items.Key("a") } }],
        }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task TransactWriteItems_ConcurrentIncrements_LoseNoUpdate()
    {
        // Arrange: 30 transactions add 1 to one counter at once. DynamoDB may refuse one with TransactionConflict; every call that
        // returns normally must be in the final count, so no update is lost either way.
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("counter", AttributeValue.FromNumber(0)) });

        async Task<bool> IncrementAsync()
        {
            try
            {
                await sandbox.Client.TransactWriteItemsAsync(new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        new TransactWriteItem
                        {
                            Update = new TransactUpdate
                            {
                                TableName = table,
                                Key = Items.Key("counter"),
                                UpdateExpression = "ADD v :one",
                                ExpressionAttributeValues = Items.Values((":one", AttributeValue.FromNumber(1))),
                            },
                        },
                    ],
                });
                return true;
            }
            catch (TransactionCanceledException exception) when (exception.CancellationReasons.All(static reason => reason.Code is "None" or "TransactionConflict"))
            {
                return false;
            }
        }

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => IncrementAsync()));
        var counter = await Get(sandbox, table, "counter");

        // Assert
        await Assert.That(counter!["v"].AsNumber<int>()).IsEqualTo(results.Count(static applied => applied));
    }

    private static async Task<IReadOnlyDictionary<string, AttributeValue>?> Get(Sandbox sandbox, string table, string pk) =>
        (await sandbox.Client.GetItemAsync(new GetItemRequest { TableName = table, Key = Items.Key(pk), ConsistentRead = true })).Item;
}
