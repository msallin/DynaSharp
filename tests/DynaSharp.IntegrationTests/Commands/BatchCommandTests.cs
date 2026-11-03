using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>BatchGet and BatchWrite of the command API against the emulators, with what only a service can show: the limits it enforces and the keys it leaves.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class BatchCommandTests(Backend backend)
{
    [Test]
    public async Task BatchWrite_MoreThanOneRequestHolds_WritesEverything()
    {
        // Arrange (DynamoDB rejects a request of more than 25 writes, so this works only if they are cut)
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await Puts(sandbox.Shop, 60).SendAsync();
        var stored = await sandbox.Shop.BatchGet().Keys(Keys(60)).Consistent().SendAsync();

        // Assert
        await Assert.That((result.Written, result.Requests, stored.Items.Count)).IsEqualTo((60, 3, 60));
    }

    [Test]
    public async Task BatchWrite_PutsAndDeletesOnTwoTablesAndTwoTypes_AppliesThemAll()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var other = await sandbox.CreateTableAsync();
        var profiles = sandbox.Shop.Of<Profile>();
        await sandbox.Shop.Put(new Item { ["pk"] = "old", ["sk"] = "s" }).SendAsync();

        // Act
        var result = await sandbox.Shop.BatchWrite()
            .Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 })
            .Delete("old", "s")
            .Put(profiles, new Profile("alice", "Alice"))
            .Put(other, new Item { ["pk"] = "b", ["sk"] = "s" })
            .ReturnCapacity()
            .SendAsync();

        // Assert
        var profile = await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync();
        await Assert.That((await sandbox.ReadAsync("a"), await sandbox.ReadAsync("old"), profile.Item, (await other.Get("b", "s").Consistent().SendAsync()).Found, result.ConsumedCapacity.Count))
            .IsEqualTo((new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }, null, new Profile("alice", "Alice"), true, 2));
    }

    [Test]
    public async Task BatchWrite_DeleteByGeneratedKey_DeletesTheItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.Put(new Profile("alice", "Alice")).SendAsync();

        // Act
        await profiles.BatchWrite().Delete(Profile.Keys.Of("alice")).SendAsync();

        // Assert
        await Assert.That((await profiles.Get(Profile.Keys.Of("alice")).Consistent().SendAsync()).Found).IsFalse();
    }

    [Test]
    public async Task BatchWrite_PutAllThenDeleteAll_WritesAndRemovesWhatTheSequencesHold()
    {
        // Arrange (more than one request holds)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        var names = Enumerable.Range(0, 30).Select(static i => "user-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        // Act
        var written = await profiles.BatchWrite().PutAll(names.Select(static name => new Profile(name, name))).SendAsync();
        var deleted = await profiles.BatchWrite().DeleteAll(names.Skip(1).Select(Profile.Keys.Of)).SendAsync();
        var left = await profiles.BatchGet().Keys(names.Select(Profile.Keys.Of)).Consistent().SendAsync();

        // Assert
        await Assert.That((written.Written, deleted.Written, left.Items.Single().FullName)).IsEqualTo((30, 29, "user-0"));
    }

    [Test]
    public async Task BatchWrite_OneKeyTwiceInARequest_FailsWithTheErrorOfDynamoDB()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () =>
            await sandbox.Shop.BatchWrite().Put(new Item { ["pk"] = "a", ["sk"] = "s" }).Delete("a", "s").SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("duplicates", StringComparison.Ordinal);
    }

    [Test]
    public async Task BatchWrite_OneKeyInTwoRequests_WritesBothInOrder()
    {
        // Arrange (write 0 and write 25 have the same key, and the last one wins)
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await PutsWithARepeat(sandbox.Shop).SendAsync();
        var stored = await sandbox.ReadAsync("item-0");

        // Assert
        await Assert.That(stored!.GetNumber<int>("v")).IsEqualTo(25);
    }

    [Test]
    public async Task BatchWrite_MissingTable_Throws()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var missing = sandbox.Client.Table(Tables.Missing, "pk");

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(async () => await missing.BatchWrite().Delete("a").SendAsync());

        // Assert
        await Assert.That(failure.OperationName).IsEqualTo("BatchWriteItem");
    }

    [Test]
    public async Task BatchGet_MoreThanOneRequestHolds_ReadsEverything()
    {
        // Arrange (DynamoDB rejects a request of more than 100 keys)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await Puts(sandbox.Shop, 250).SendAsync();

        // Act
        var result = await sandbox.Shop.BatchGet().Keys(Keys(250)).Consistent().ReturnCapacity().SendAsync();

        // Assert
        await Assert.That((result.Items.Count, result.Requests, result.ConsumedCapacity.Count, result.Items.Select(static item => item.GetNumber<int>("v")).Distinct().Count())).IsEqualTo((250, 3, 3, 250));
    }

    [Test]
    public async Task BatchGet_MissingKeys_LeavesThemOut()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).SendAsync();

        // Act
        var result = await sandbox.Shop.BatchGet().Key("a", "s").Key("missing", "s").Consistent().SendAsync();

        // Assert
        await Assert.That(result.Items.Single().GetString("pk")).IsEqualTo("a");
    }

    [Test]
    public async Task BatchGet_Entities_ReadsThem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var profiles = sandbox.Shop.Of<Profile>();
        await profiles.BatchWrite().Put(new Profile("alice", "Alice", 1)).Put(new Profile("bob", "Bob", 2)).SendAsync();

        // Act
        var result = await profiles.BatchGet().Key(Profile.Keys.Of("alice")).Keys([Profile.Keys.Of("bob")]).Consistent().SendAsync();

        // Assert
        await Assert.That(result.Items.OrderBy(static profile => profile.Id, StringComparer.Ordinal)).IsEquivalentTo([new Profile("alice", "Alice", 1), new Profile("bob", "Bob", 2)], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task BatchGet_Projection_ReturnsThoseAttributes()
    {
        // Arrange (status is a reserved word)
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["status"] = "open", ["other"] = "dropped" }).SendAsync();

        // Act
        var result = await sandbox.Shop.BatchGet().Key("a", "s").Consistent().Project("pk", "status").SendAsync();

        // Assert
        await Assert.That(result.Items.Single().Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(["pk", "status"]);
    }

    [Test]
    public async Task BatchGet_OneKeyTwiceInARequest_FailsWithTheErrorOfDynamoDB()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () => await sandbox.Shop.BatchGet().Key("a", "s").Key("a", "s").SendAsync());

        // Assert
        await Assert.That(failure.Message).Contains("duplicates", StringComparison.Ordinal);
    }

    [Test]
    public async Task BatchGet_InvalidProjection_ExplainsItsPlaceholders()
    {
        // Arrange (a projection that overlaps itself is what DynamoDB refuses)
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () => await sandbox.Shop.BatchGet().Key("a", "s").Project("a", "a.b").SendAsync());

        // Assert
        await Assert.That(failure.ExpressionNames!.Values).Contains("a");
    }

    [Test]
    [NotInParallel] // Moves 35 MB through the emulator; running beside the other tests slows every one of them down.
    public async Task BatchGet_ResponseOver16MB_ReadsWhatDynamoDBLeavesInTheNextRound()
    {
        // Arrange (45 items of 390 KB are 17.5 MB, and a response holds 16 MB, so DynamoDB leaves keys unprocessed. Real DynamoDB leaves more when a partition
        // runs out of throughput, so there are at least two rounds, and on a new table more)
        Gaps.SkipIfMissing(backend, Gap.BatchResponseLimit);
        const int Count = 45;
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var big = new string('x', 390 * 1024);
        await BigPuts(sandbox.Shop, Count, big).SendAsync();

        // Act
        var result = await sandbox.Shop.BatchGet().Keys(Keys(Count)).Consistent().SendAsync();

        // Assert
        await Assert.That((result.Items.Count, result.Requests >= 2, result.Items.Select(static item => item.GetString("pk")).Distinct().Count())).IsEqualTo((Count, true, Count));
    }

    private static BatchWriteCommand<Item> Puts(Table<Item> table, int count)
    {
        var command = table.BatchWrite();
        for (var i = 0; i < count; i++)
        {
            command = command.Put(new Item { ["pk"] = "item-" + i, ["sk"] = "s", ["v"] = i });
        }

        return command;
    }

    private static BatchWriteCommand<Item> PutsWithARepeat(Table<Item> table)
    {
        var command = table.BatchWrite();
        for (var i = 0; i < 25; i++)
        {
            command = command.Put(new Item { ["pk"] = "item-" + i, ["sk"] = "s", ["v"] = i });
        }

        return command.Put(new Item { ["pk"] = "item-0", ["sk"] = "s", ["v"] = 25 });
    }

    private static BatchWriteCommand<Item> BigPuts(Table<Item> table, int count, string big)
    {
        var command = table.BatchWrite();
        for (var i = 0; i < count; i++)
        {
            command = command.Put(new Item { ["pk"] = "item-" + i, ["sk"] = "s", ["big"] = big });
        }

        return command;
    }

    private static IEnumerable<ItemKey> Keys(int count) => Enumerable.Range(0, count).Select(static i => new ItemKey("item-" + i));

    /// <summary>A key of a table without a schema, as an application writes one by hand.</summary>
    /// <param name="partitionKey">The partition key.</param>
    private readonly struct ItemKey(string partitionKey) : IKey<Item>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString(partitionKey);
            writer.WriteAttributeName("sk"u8);
            writer.WriteString("s"u8);
        }
    }
}
