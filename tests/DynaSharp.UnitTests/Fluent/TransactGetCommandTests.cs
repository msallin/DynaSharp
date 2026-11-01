using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using static DynaSharp.UnitTests.Http.RetryTestSupport;

namespace DynaSharp.UnitTests.Fluent;

public sealed class TransactGetCommandTests
{
    private const string TwoItems = """{"Responses":[{"Item":{"pk":{"S":"CUSTOMER#a"},"fullName":{"S":"Alice"}}},{"Item":{"pk":{"S":"x"},"n":{"N":"1"}}}]}""";

    [Test]
    public async Task SendAsync_GetsOfSeveralTables_WritesThemInOrder()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Customers");
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        await client.TransactGet().Get(customers, Customer.Keys.Of("a"), out _).Get(shop, "TENANT#1", "QUOTA", out _).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().BodyText).IsEqualTo(
            """{"TransactItems":[{"Get":{"TableName":"Customers","Key":{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"}}}},{"Get":{"TableName":"Shop","Key":{"pk":{"S":"TENANT#1"},"sk":{"S":"QUOTA"}}}}],"ReturnConsumedCapacity":"TOTAL"}""");
    }

    [Test]
    public async Task ToRequestJson_PartitionKeyOnly_WritesAKeyWithoutASortKey()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = client.TransactGet().Get(client.Table("Sessions", "pk"), "a", out _).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Get":{"TableName":"Sessions","Key":{"pk":{"S":"a"}}}}]}""");
    }

    [Test]
    public async Task Read_SlotOfAPartitionKeyOnly_ReadsTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"a"},"n":{"N":"1"}}}]}"""));
        using var client = TestClient.Create(handler);

        // Act
        var snapshot = await client.TransactGet().Get(client.Table("Sessions", "pk"), "a", out var slot).SendAsync();

        // Assert
        await Assert.That(snapshot.Read(slot).Item!.GetNumber<int>("n")).IsEqualTo(1);
    }

    [Test]
    public async Task Read_EverySlot_ReadsItsItemWithItsType()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(TwoItems));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        var shop = client.Table("Shop", "pk");

        // Act
        var snapshot = await client.TransactGet().Get(customers, Customer.Keys.Of("a"), out var customer).Get(shop, "x", default, out var item).SendAsync();

        // Assert
        await Assert.That((snapshot.Read(customer).Item, snapshot.Read(item).Item!.GetNumber<int>("n"))).IsEqualTo((new Customer("a", "Alice"), (int?)1));
    }

    [Test]
    public async Task Read_KeyWithoutAnItem_IsNotFound()
    {
        // Arrange (a missing item is {} on the wire, and the documentation also has null)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{},null,{"Other":1}]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var snapshot = await client.TransactGet().Get(shop, "a", default, out var empty).Get(shop, "b", default, out var nothing).Get(shop, "c", default, out var unknown).SendAsync();

        // Assert
        await Assert.That((snapshot.Read(empty).Found, snapshot.Read(nothing).Found, snapshot.Read(unknown).Found)).IsEqualTo((false, false, false));
    }

    [Test]
    public async Task Read_Twice_MakesTwoItems()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"a"}}}]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var snapshot = await client.TransactGet().Get(shop, "a", default, out var slot).SendAsync();

        // Act
        var first = snapshot.Read(slot).Item;
        var second = snapshot.Read(slot).Item;

        // Assert
        await Assert.That((first!.Equals(second), ReferenceEquals(first, second))).IsEqualTo((true, false));
    }

    [Test]
    public async Task Read_AfterTheClientIsGoneAndItsBuffersAreOverwritten_StillReadsTheItem()
    {
        // Arrange (a pool that hands the same array out again would show a use after return: the array of the response is filled by the next call)
        using var handler = new StubHandler();
        handler.Enqueue(StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"first"}}}]}"""));
        handler.Enqueue(StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"other"}}}]}"""));
        var pool = new ReusingArrayPool();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");
        var snapshot = await client.TransactGet().Get(shop, "a", default, out var slot).SendAsync();
        await client.TransactGet().Get(shop, "b", default, out _).SendAsync();
        pool.Scribble();
        client.Dispose();

        // Act
        var item = snapshot.Read(slot).Item;

        // Assert
        await Assert.That(item!.GetString("pk")).IsEqualTo("first");
    }

    [Test]
    public async Task Read_ItemOfAnotherType_ThrowsOnlyWhereItIsRead()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"CUSTOMER#a"},"_type":{"S":"ORDER"}}},{"Item":{"pk":{"S":"b"}}}]}"""));
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");
        var shop = client.Table("Shop", "pk");
        var snapshot = await client.TransactGet().Get(customers, Customer.Keys.Of("a"), out var customer).Get(shop, "b", default, out var item).SendAsync();

        // Act
        var failure = TestClient.Catch(() => snapshot.Read(customer));

        // Assert
        await Assert.That((failure is DynamoDbMappingException, snapshot.Read(item).Found)).IsEqualTo((true, true));
    }

    [Test]
    public async Task Read_DefaultSlot_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{}]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var snapshot = await client.TransactGet().Get(shop, "a", default, out _).SendAsync();

        // Act
        var failure = TestClient.Catch(() => snapshot.Read(default(TransactSlot<Item>)));

        // Assert
        await Assert.That((failure as ArgumentException)?.ParamName).IsEqualTo("slot");
    }

    [Test]
    public async Task Read_SlotOfALargerTransaction_Throws()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[{}]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        var snapshot = await client.TransactGet().Get(shop, "a", default, out _).SendAsync();
        client.TransactGet().Get(shop, "a", default, out _).Get(shop, "b", default, out var second).ToRequestJson();

        // Act
        var failure = TestClient.Catch(() => snapshot.Read(second));

        // Assert
        await Assert.That(failure.Message).Contains("another transaction");
    }

    [Test]
    public async Task SendAsync_Capacity_ReachesTheResult()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"New":1,"ConsumedCapacity":[{"TableName":"Shop","CapacityUnits":2.0}]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await client.TransactGet().Get(shop, "a", default, out _).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity.Single().CapacityUnits).IsEqualTo(2.0);
    }

    [Test]
    public async Task SendAsync_Await_SendsIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var result = await client.TransactGet().Get(shop, "a", default, out _);

        // Assert
        await Assert.That(result.ConsumedCapacity.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_NoGets_LeavesItToDynamoDB()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.DynamoDbError("ValidationException", "Member must have length greater than or equal to 1"));
        using var client = TestClient.Create(handler);

        // Act
        var failure = await TestClient.CaptureAsync<ValidationException>(async () => await client.TransactGet().SendAsync());

        // Assert
        await Assert.That((handler.Requests.Single().BodyText, failure.ErrorCode)).IsEqualTo(("""{"TransactItems":[]}""", "ValidationException"));
    }

    [Test]
    public async Task SendAsync_EntryThatIsNoEntry_FailsAsUnrecognized()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":[1]}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbException>(async () => await client.TransactGet().Get(shop, "a", default, out _).SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("UnrecognizedResponse");
    }

    [Test]
    public async Task SendAsync_Retried_SendsTheSameBody()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.Enqueue(Throttled());
        handler.Enqueue(StubResponse.Json("""{"Responses":[{}]}"""));
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await client.TransactGet().Get(shop, "a", default, out _).SendAsync();

        // Assert
        await Assert.That(handler.Requests.Select(static request => request.BodyText).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_MaxAttempts_LimitsTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        handler.DefaultResponse = Throttled();
        using var client = CreateClient(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        await TestClient.CaptureAsync<ProvisionedThroughputExceededException>(async () => await client.TransactGet().Get(shop, "a", default, out _).MaxAttempts(1).SendAsync());

        // Assert
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_Cancelled_SurfacesTheReasons()
    {
        // Arrange
        const string Canceled = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","Message":"Transaction cancelled","CancellationReasons":[{"Code":"TransactionConflict"}]}""";
        using var handler = new StubHandler(_ => new StubResponse { StatusCode = System.Net.HttpStatusCode.BadRequest, Body = Canceled });
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<TransactionCanceledException>(async () => await client.TransactGet().Get(shop, "a", default, out _).SendAsync());

        // Assert
        await Assert.That(failure.Failures.Single()).IsEqualTo(new TransactionActionFailure(0, TransactionFailure.TransactionConflict));
    }

    [Test]
    public async Task Get_100Gets_Fit()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var json = Gets(client.TransactGet(), shop, 100).ToRequestJson();

        // Assert
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("TransactItems").GetArrayLength()).IsEqualTo(100);
    }

    [Test]
    public async Task Get_101stGet_ThrowsAClearError()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => Gets(client.TransactGet(), shop, 101));

        // Assert
        await Assert.That((failure is InvalidOperationException, failure.Message.Contains("at most 100 actions", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    public async Task Get_NullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get((Table<Item>)null!, "a", default, out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Get_NullTableWithAKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get((Table<Customer>)null!, Customer.Keys.Of("a"), out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Get_NullKey_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get(customers, (ClassKey)null!, out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task Get_KeyOfAClass_WritesWhatItWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var customers = client.Table<Customer>("Shop");

        // Act
        var json = client.TransactGet().Get(customers, new ClassKey(), out _).ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TransactItems":[{"Get":{"TableName":"Shop","Key":{}}}]}""");
    }

    [Test]
    public async Task Get_EmptyKey_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get(shop, string.Empty, default, out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Get_EmptyPartitionKeyOnly_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get(shop, string.Empty, out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Get_NullTableWithAPartitionKeyOnly_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().Get((Table<Item>)null!, "a", out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
    }

    [Test]
    public async Task MaxAttempts_Zero_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failure = TestClient.Catch(() => client.TransactGet().MaxAttempts(0));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Get_OnAStaleCommand_ThrowsAndNamesTheTransaction()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() =>
        {
            var command = client.TransactGet();
            _ = command.Get(shop, "a", default, out _);
            _ = command.Get(shop, "b", default, out _);
        });

        // Assert
        await Assert.That(failure.Message).Contains("TransactGetItems on several tables was already sent, extended or rendered");
    }

    [Test]
    public async Task DebuggerText_WhileBuilding_ShowsTheGetsSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var text = client.TransactGet().Get(shop, "a", default, out _).DebuggerText;

        // Assert
        await Assert.That(text).IsEqualTo("""TransactGetItems on Shop: {"TransactItems":[{"Get":{"TableName":"Shop","Key":{"pk":{"S":"a"}}""");
    }

    private static TransactGetCommand Gets(TransactGetCommand command, Table<Item> shop, int count)
    {
        for (var i = 0; i < count; i++)
        {
            command = command.Get(shop, "key-" + i, default, out _);
        }

        return command;
    }

    private sealed class ClassKey : IKey<Customer>
    {
        public void WriteKey(DynamoDbWriter writer)
        {
        }
    }

    // A pool that keeps every array it gets back and hands it out again, so that memory comes back at once, as in a pool under load.
    private sealed class ReusingArrayPool : System.Buffers.ArrayPool<byte>
    {
        private readonly Lock _gate = new();
        private readonly List<byte[]> _idle = [];
        private readonly List<byte[]> _all = [];

        public override byte[] Rent(int minimumLength)
        {
            lock (_gate)
            {
                var index = _idle.FindIndex(array => array.Length >= minimumLength);
                if (index >= 0)
                {
                    var array = _idle[index];
                    _idle.RemoveAt(index);
                    return array;
                }

                var created = new byte[minimumLength];
                _all.Add(created);
                return created;
            }
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            lock (_gate)
            {
                _idle.Add(array);
            }
        }

        public void Scribble()
        {
            lock (_gate)
            {
                foreach (var array in _all)
                {
                    array.AsSpan().Fill((byte)'#');
                }
            }
        }
    }
}
