using DynaSharp.Mapping;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Mapping.KeyEntities;

namespace DynaSharp.UnitTests.Fluent;

public sealed class TableTests
{
    [Test]
    public async Task Table_Names_AreKept()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var table = client.Table("Shop", "pk", "sk");

        // Assert
        await Assert.That((table.Name, table.PartitionKeyName, table.SortKeyName)).IsEqualTo(("Shop", "pk", (string?)"sk"));
    }

    [Test]
    [Arguments("", "pk", null)]
    [Arguments("Shop", "", null)]
    [Arguments("Shop", "pk", "")]
    public async Task Table_EmptyName_Throws(string name, string partitionKey, string? sortKey)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(client.Table(name, partitionKey, sortKey)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Client_OfATableAndItsViews_IsTheClientItWasOpenedFrom()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var table = client.Table("Shop", "pk", "sk");

        // Act
        var clients = (table.Client, table.Of<Customer>().Client, client.Table<Customer>("Shop").Client);

        // Assert
        await Assert.That(clients.Item1 == client && clients.Item2 == client && clients.Item3 == client).IsTrue();
    }

    [Test]
    public async Task TableOfEntity_KeyNames_ComeFromTheEntity()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var table = client.Table<Customer>("Shop");

        // Assert
        await Assert.That((table.Name, table.PartitionKeyName, table.SortKeyName)).IsEqualTo(("Shop", "pk", (string?)"sk"));
    }

    [Test]
    public async Task TableOfEntity_EmptyName_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(client.Table<Customer>(string.Empty)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    public async Task Of_Entity_HasTheKeyNamesOfTheEntity()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "PK", "SK");

        // Act
        var customers = shop.Of<Customer>();

        // Assert
        await Assert.That((customers.Name, customers.PartitionKeyName, customers.SortKeyName)).IsEqualTo(("Shop", "pk", (string?)"sk"));
    }

    [Test]
    public async Task Of_SameTypeTwice_ReturnsTheSameTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var first = shop.Of<Customer>();
        var second = shop.Of<Customer>();

        // Assert
        await Assert.That(ReferenceEquals(first, second)).IsTrue();
    }

    [Test]
    public async Task Of_SecondCall_AllocatesNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");
        for (var i = 0; i < 1000; i++)
        {
            shop.Of<Customer>();
        }

        // Act
        var allocated = long.MaxValue;
        for (var i = 0; i < 16; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            shop.Of<Customer>();
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        // Assert
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Of_ManyThreads_AllGetOneTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var tables = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(shop.Of<Customer>)));

        // Assert
        await Assert.That(tables.Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task Index_Names_AreKept()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var index = shop.Index("StatusIndex", "status", "placedAt");

        // Assert
        await Assert.That((index.Table, index.Name, index.PartitionKeyName, index.SortKeyName)).IsEqualTo((shop, "StatusIndex", "status", (string?)"placedAt"));
    }

    [Test]
    [Arguments("", "status")]
    [Arguments("StatusIndex", "")]
    public async Task Index_EmptyName_Throws(string name, string partitionKey)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Index(name, partitionKey)));

        // Assert
        await Assert.That(failure).IsNotNull();
    }

    [Test]
    [Arguments("ByEmail", "email", null)]
    [Arguments("ByName", "fullName", "pk")]
    public async Task Index_EntityDeclaresIt_TakesTheKeyNamesFromTheEntity(string name, string partitionKey, string? sortKey)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var index = customers.Index(name);

        // Assert
        await Assert.That((index.Name, index.PartitionKeyName, index.SortKeyName)).IsEqualTo((name, partitionKey, sortKey));
    }

    [Test]
    public async Task Index_GeneratedConstant_FindsTheIndexWithItsKeys()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var index = client.Table<KeyedOrder>("Shop").Index(KeyedOrder.Indexes.Gsi2);

        // Assert
        await Assert.That((index.Name, index.PartitionKeyName, index.SortKeyName)).IsEqualTo(("GSI2", "gsi2pk", (string?)"gsi2sk"));
    }

    [Test]
    public async Task Index_EntityDoesNotDeclareIt_ThrowsNamingTheIndex()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
#pragma warning disable DYNA207 // Which is what this test is about.
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(customers.Index("Nope")));
#pragma warning restore DYNA207

        // Assert
        await Assert.That(failure.Message).Contains("Nope");
    }

    [Test]
    public async Task Index_OfAnEntityOnATableWithoutASchema_TakesTheKeyNamesFromTheEntity()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var index = shop.Index<KeyedOrder>(KeyedOrder.Indexes.Gsi2);

        // Assert
        await Assert.That((index.Table, index.Name, index.PartitionKeyName, index.SortKeyName)).IsEqualTo((shop, "GSI2", "gsi2pk", (string?)"gsi2sk"));
    }

    [Test]
    public async Task Index_OfAnEntityOnATableWithoutASchema_ReadsItemsWithoutAFilter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.Table("Shop", "pk", "sk").Index<KeyedOrder>(KeyedOrder.Indexes.Gsi2).Query("a", SortKeyCondition.Ge("b")).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","IndexName":"GSI2","KeyConditionExpression":"#n0 = :v0 AND #n1 >= :v1","ExpressionAttributeNames":{"#n0":"gsi2pk","#n1":"gsi2sk"},"ExpressionAttributeValues":{":v0":{"S":"a"},":v1":{"S":"b"}}}""");
    }

    [Test]
    public async Task Index_OfAnEntityThatHasOnlyAPartitionKey_HasNoSortKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var index = client.Table("Shop", "pk", "sk").Index<Customer>("ByEmail");

        // Assert
        await Assert.That((index.PartitionKeyName, index.SortKeyName)).IsEqualTo(("email", (string?)null));
    }

    [Test]
    public async Task Index_OfAnEntityThatDoesNotDeclareIt_ThrowsAsTheTypedFormDoes()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
#pragma warning disable DYNA207 // Which is what this test is about.
        var failure = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Index<Customer>("Nope")));
        var typed = await TestClient.CaptureAsync<ArgumentException>(() => Task.FromResult(shop.Of<Customer>().Index("Nope")));
#pragma warning restore DYNA207

        // Assert
        await Assert.That((failure.Message, failure.ParamName)).IsEqualTo((typed.Message, typed.ParamName));
    }

    [Test]
    [Arguments("")]
    public async Task Get_EmptyPartitionKey_ThrowsNamingTheParameter(string key)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get(key, "b");
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Get_DefaultPartitionKey_ThrowsNamingTheParameter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get(default);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Update_EmptySortKey_ThrowsNamingTheParameter()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Update("a", string.Empty);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("sortKey");
    }

    [Test]
    public async Task Delete_EmptyBinaryKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Delete(Array.Empty<byte>());
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("partitionKey");
    }

    [Test]
    public async Task Get_SortKeyOnATableWithoutOne_ThrowsNamingTheTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get("a", "b");
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.Message).Contains("Shop");
    }

    [Test]
    public async Task Get_EmptyKey_LeavesThePoolAsItWas()
    {
        // Arrange
        using var handler = new StubHandler();
        var pool = new DynaSharp.UnitTests.Serialization.CountingArrayPool<byte>();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ArgumentException>(() =>
        {
            _ = shop.Get(string.Empty);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(pool.RentedLengths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Get_NullGeneratedKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            _ = customers.Get((ClassKey)null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("key");
    }

    [Test]
    public async Task Get_KeyThatIsAClass_Works()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = customers.Get(new ClassKey()).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}}}""");
    }

    [Test]
    public async Task Get_NullEntity_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentNullException>(() =>
        {
            _ = customers.Get((Customer)null!);
            return Task.CompletedTask;
        });

        // Assert
        await Assert.That(failure.ParamName).IsEqualTo("entity");
    }

    [Test]
    public async Task Extensions_NullTable_Throw()
    {
        // Arrange
        Table<Item> items = null!;
        Table<Customer> customers = null!;

        // Act
        var failures = new[]
        {
            await TestClient.CaptureAsync<ArgumentNullException>(() =>
            {
                _ = items.Put();
                return Task.CompletedTask;
            }),
            await TestClient.CaptureAsync<ArgumentNullException>(() =>
            {
                _ = customers.Get(new Customer("a", "b"));
                return Task.CompletedTask;
            }),
            await TestClient.CaptureAsync<ArgumentNullException>(() => Task.FromResult(customers.Index("ByName"))),
        };

        // Assert
        await Assert.That(failures.Select(failure => failure.ParamName).Distinct().Single()).IsEqualTo("table");
    }

    [Test]
    public async Task BeginScan_EntityWithADiscriminator_FiltersOnIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var model = client.Table<Customer>("Shop").Model;

        // Act
        var state = model.BeginScan();
        var version = state.Version;
        var text = System.Text.Encoding.UTF8.GetString(state.Finish());
        state.Release(version);

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Shop","FilterExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"_type"},"ExpressionAttributeValues":{":v0":{"S":"CUSTOMER"}}}""");
    }

    [Test]
    public async Task BeginScan_AttributeOfAnotherName_FiltersOnThatOne()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var model = client.Table<Tenant>("Shop").Model;

        // Act
        var state = model.BeginScan();
        var version = state.Version;
        var text = System.Text.Encoding.UTF8.GetString(state.Finish());
        state.Release(version);

        // Assert
        await Assert.That(text).Contains("\"ExpressionAttributeNames\":{\"#n0\":\"kind\"}");
    }

    [Test]
    public async Task BeginScan_ItemTable_AddsNothing()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var model = client.Table("Shop", "pk").Model;

        // Act
        var state = model.BeginScan();
        var version = state.Version;
        var text = System.Text.Encoding.UTF8.GetString(state.Finish());
        state.Release(version);

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Shop"}""");
    }

    [Test]
    public async Task IndexOfAnEntity_Read_KeepsTheDiscriminator()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var model = client.Table<Customer>("Shop").Index("ByEmail").Model;

        // Assert
        await Assert.That((model.Discriminator, model.DiscriminatorAttributeName, model.PartitionKeyName)).IsEqualTo(((string?)"CUSTOMER", (string?)"_type", "email"));
    }

    [Test]
    public async Task TableWithoutDiscriminator_Read_HasNone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var model = client.Table<Session>("Sessions").Model;

        // Assert
        await Assert.That(model.Discriminator).IsNull();
    }

    private sealed class ClassKey : IKey<Customer>
    {
        public void WriteKey(DynaSharp.Serialization.DynamoDbWriter writer)
        {
            writer.WriteAttributeName("pk"u8);
            writer.WriteString("a"u8);
        }
    }
}
