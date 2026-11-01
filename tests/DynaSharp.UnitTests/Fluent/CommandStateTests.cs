using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

// What keeps a pooled state safe: a handle that is used up stays used up, whatever happened to the state since, and a state goes back once.
public sealed class CommandStateTests
{
    [Test]
    public async Task Extend_HandleThatWasExtended_ThrowsNamingOperationAndTable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var message = MessageOfStaleExtend(shop);

        // Assert
        await Assert.That(message).IsEqualTo("This UpdateItem on Shop was already sent or extended. A command is built in one chain and sent once; to send it again, build it again.");
    }

    [Test]
    public async Task SendAsync_SentTwice_ThrowsAndSendsOnce()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var (message, first) = SendTwice(shop);
        await first;

        // Assert
        await Assert.That((message.StartsWith("This PutItem on Shop was already sent", StringComparison.Ordinal), handler.Requests.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task ToRequestJson_SecondTime_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var message = RenderTwice(shop);

        // Assert
        await Assert.That(message).Contains("DeleteItem on Shop");
    }

    [Test]
    public async Task Extend_StaleHandleOfAStateThatHasANewOwner_ThrowsAndLeavesTheNewCommandAlone()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var (message, json) = StaleHandleAfterReuse(shop);

        // Assert
        await Assert.That((message.StartsWith("This UpdateItem on Shop", StringComparison.Ordinal), json)).IsEqualTo((true, """{"TableName":"Shop","Key":{"pk":{"S":"second"}}}"""));
    }

    [Test]
    public async Task If_StaleHandleThroughAnInterpolation_ThrowsBeforeItWrites()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var (message, json) = StaleInterpolation(shop);

        // Assert
        await Assert.That((message.StartsWith("This UpdateItem on Shop", StringComparison.Ordinal), json)).IsEqualTo((true, """{"TableName":"Shop","Key":{"pk":{"S":"second"}}}"""));
    }

    [Test]
    public async Task SendAsync_DefaultCommand_ThrowsSayingSo()
    {
        // Arrange
        var message = string.Empty;

        // Act
        try
        {
            await default(GetCommand<Item>).SendAsync();
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
        }

        // Assert
        await Assert.That(message).StartsWith("This GetItem command is a default value.");
    }

    [Test]
    public async Task Release_Twice_ThrowsAndLeavesOneStateInThePool()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var state = client.Table("Shop", "pk").Model.Begin();
        var version = state.Version;
        state.Release(version);

        // Act
        var message = string.Empty;
        try
        {
            state.Release(version);
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
        }

        var first = client.Core.RentCommand();
        var second = client.Core.RentCommand();

        // Assert
        await Assert.That((message.Length > 0, ReferenceEquals(first, second))).IsEqualTo((true, false));
    }

    [Test]
    public async Task Release_RacingThreads_OneWins()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var model = client.Table("Shop", "pk").Model;
        var rounds = 2000;
        var failures = 0;
        using var start = new Barrier(2);

        // Act
        for (var round = 0; round < rounds; round++)
        {
            var state = model.Begin();
            var version = state.Version;
            var racers = new[] { RaceAsync(state, version, start), RaceAsync(state, version, start) };
            var results = await Task.WhenAll(racers);
            failures += results.Count(won => !won) == 1 ? 0 : 1;
        }

        // Assert
        await Assert.That(failures).IsEqualTo(0);
    }

    [Test]
    public async Task Release_NextCommand_StartsClean()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");
        shop.Update("first").Set("a", 1).If(Attr("b").Eq(2)).ReturnNew().ReturnOldOnFailure().ReturnCapacity().ReturnMetrics().MaxAttempts(2).ToRequestJson();

        // Act
        var json = shop.Get("second").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"second"}}}""");
    }

    [Test]
    public async Task Release_SameThreadAfterALargeBody_GivesTheLargeArrayBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");

        // Act
        shop.Put(new Item { ["pk"] = "a", ["large"] = new string('x', 100_000) }).ToRequestJson();
        var heldAfterwards = pool.Outstanding;

        // Assert
        await Assert.That(heldAfterwards).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_Client_GivesTheArraysOfIdleStatesBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        var client = TestClient.Create(handler, pool: pool);
        client.Table("Shop", "pk").Update("a").Set("b", 1).If(Attr("c").Eq(2)).ToRequestJson();
        var held = pool.Outstanding;

        // Act
        client.Dispose();

        // Assert
        await Assert.That((held > 0, pool.Outstanding)).IsEqualTo((true, 0));
    }

    [Test]
    public async Task Return_PoolIsFull_GivesTheArraysBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var model = client.Table("Shop", "pk").Model;
        var states = Enumerable.Range(0, 100).Select(_ => model.Begin()).ToList();

        // Act
        foreach (var state in states)
        {
            state.Release(state.Version);
        }

        // Assert (the pool of the client keeps 64 states, each of which holds its body array)
        await Assert.That(pool.Outstanding).IsEqualTo(64);
    }

    [Test]
    public async Task SendAsync_ClientDisposed_GivesTheStateBackAndThrows()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");
        client.Dispose();

        // Act
        await TestClient.CaptureAsync<ObjectDisposedException>(async () => await shop.Put(new Item { ["pk"] = "a" }).SendAsync());

        // Assert
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_InvalidOption_FailsInTheCallAndGivesTheStateBack()
    {
        // Arrange
        var pool = new CountingArrayPool<byte>();
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler, pool: pool);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(async () => await shop.Get("a").ReturnCapacity((ReturnConsumedCapacity)99).SendAsync());
        var next = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(async () => await shop.Get("a").ReturnCapacity((ReturnConsumedCapacity)99).SendAsync());

        // Assert (a second command takes the same state, so nothing was lost)
        await Assert.That((failure is not null, next is not null, pool.RentedLengths.Count)).IsEqualTo((true, true, 1));
    }

    [Test]
    public async Task ToRequestJson_InvalidOption_ThrowsAndGivesTheStateBack()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<ArgumentOutOfRangeException>(() => Task.FromResult(shop.Get("a").ReturnCapacity((ReturnConsumedCapacity)99).ToRequestJson()));
        var json = shop.Get("b").ToRequestJson();

        // Assert
        await Assert.That((failure is not null, json)).IsEqualTo((true, """{"TableName":"Shop","Key":{"pk":{"S":"b"}}}"""));
    }

    [Test]
    public async Task Abandon_Commands_TheNextOneIsClean()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        for (var i = 0; i < 1000; i++)
        {
            _ = shop.Update("abandoned").Set("a", i).If(Attr("b").Eq(i));
        }

        var json = shop.Get("next").ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"next"}}}""");
    }

    [Test]
    public async Task Build_ConverterThatThrows_LeavesTheNextCommandClean()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<InvalidOperationException>(() =>
        {
            shop.Update("a").Set(new Attr<string, ThrowingConverter>("x"), "boom");
            return Task.CompletedTask;
        });
        var json = shop.Update("b").Set("y", 1).ToRequestJson();

        // Assert
        await Assert.That((failure.Message, json)).IsEqualTo(("Cannot write this.", """{"TableName":"Shop","Key":{"pk":{"S":"b"}},"UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"y"},"ExpressionAttributeValues":{":v0":{"N":"1"}}}"""));
    }

    [Test]
    public async Task Describe_CommandInProgress_ShowsTheRequestSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = Describe(client.Table("Shop", "pk").Update("a").Set("n", 1));

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}},"UpdateExpression":"SET #n0 = :v0","ExpressionAttributeNames":{"#n0":"n"},"ExpressionAttributeValues":{":v0":{"N":"1"}}""");
    }

    [Test]
    public async Task Describe_OpenItem_ShowsIt()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var text = Describe(client.Table("Shop", "pk").Put().With("pk", "a"));

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Shop","Item":{"pk":{"S":"a"}""");
    }

    [Test]
    public async Task DebuggerText_State_ShowsTheRequestSoFar()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        CommandHandle handle = client.Table("Shop", "pk").Delete("a");

        // Act
        var text = handle.State!.DebuggerText;

        // Assert
        await Assert.That(text).IsEqualTo("""{"TableName":"Shop","Key":{"pk":{"S":"a"}}""");
    }

    [Test]
    public async Task Describe_StaleHandle_SaysSo()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        CommandHandle handle = client.Table("Shop", "pk").Update("a");

        // Act
        var state = handle.State!;
        var version = handle.Version;
        state.Release(version);
        var text = state.Describe(version);

        // Assert
        await Assert.That(text).IsEqualTo("(sent or extended)");
    }

    [Test]
    [Arguments("Get")]
    [Arguments("Put")]
    [Arguments("Update")]
    [Arguments("Delete")]
    [Arguments("Return")]
    public async Task DebuggerText_EveryCommand_NamesOperationAndTable(string command)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var text = DebuggerText(command, shop);

        // Assert
        await Assert.That(text).Contains(" on Shop: {\"TableName\":\"Shop\"");
    }

    [Test]
    public async Task DebuggerText_DefaultCommand_SaysSo()
    {
        // Arrange
        const string expected = "GetItem (default)";

        // Act
        var text = default(GetCommand<Item>).DebuggerText;

        // Assert
        await Assert.That(text).IsEqualTo(expected);
    }

    [Test]
    public async Task Interpolation_DefaultCommand_ThrowsSayingSo()
    {
        // Arrange
        var message = string.Empty;

        // Act
        try
        {
            _ = default(UpdateCommand<Item>).If($"{Attr("a")} = {1}").ToRequestJson();
        }
        catch (InvalidOperationException exception)
        {
            message = exception.Message;
        }

        // Assert
        await Assert.That(message).StartsWith("This UpdateItem command is a default value.");
    }

    private static async Task<bool> RaceAsync(CommandState state, int version, Barrier start)
    {
        await Task.Yield();
        start.SignalAndWait();
        try
        {
            state.Release(version);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string MessageOfStaleExtend(Table<Item> shop)
    {
        var first = shop.Update("a");
        _ = first.Set("x", 1);
        try
        {
            first.Set("y", 2);
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static (string Message, Task First) SendTwice(Table<Item> shop)
    {
        var command = shop.Put(new Item { ["pk"] = "a" });
        var first = command.SendAsync().AsTask();
        try
        {
            _ = command.SendAsync().AsTask();
            return (string.Empty, first);
        }
        catch (InvalidOperationException exception)
        {
            return (exception.Message, first);
        }
    }

    private static string RenderTwice(Table<Item> shop)
    {
        var command = shop.Delete("a");
        _ = command.ToRequestJson();
        try
        {
            _ = command.ToRequestJson();
            return string.Empty;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    private static (string Message, string Json) StaleHandleAfterReuse(Table<Item> shop)
    {
        var stale = shop.Update("first");
        _ = stale.ToRequestJson();

        // The state went back to the pool, so this takes the same object.
        var second = shop.Get("second");
        try
        {
            stale.Set("a", 1);
            return (string.Empty, second.ToRequestJson());
        }
        catch (InvalidOperationException exception)
        {
            return (exception.Message, second.ToRequestJson());
        }
    }

    private static (string Message, string Json) StaleInterpolation(Table<Item> shop)
    {
        var stale = shop.Update("first");
        _ = stale.ToRequestJson();
        var second = shop.Get("second");
        try
        {
            stale.If($"{Attr("a")} = {1}");
            return (string.Empty, second.ToRequestJson());
        }
        catch (InvalidOperationException exception)
        {
            return (exception.Message, second.ToRequestJson());
        }
    }

    private static string Describe(CommandHandle command) => command.State!.Describe(command.Version);

    private static string DebuggerText(string command, Table<Item> shop) => command switch
    {
        "Get" => shop.Get("a").DebuggerText,
        "Put" => shop.Put().DebuggerText,
        "Update" => shop.Update("a").DebuggerText,
        "Delete" => shop.Delete("a").DebuggerText,
        _ => shop.Update("a").Set("n", 1).Return(Attr<int>("n")).DebuggerText,
    };

    private readonly struct ThrowingConverter : DynaSharp.Serialization.IDynamoDbConverter<string>
    {
        public static void Write(DynaSharp.Serialization.DynamoDbWriter writer, string value) => throw new InvalidOperationException("Cannot write this.");

        public static string Read(ref DynaSharp.Serialization.DynamoDbReader reader, AttributeType type) => throw new NotSupportedException();
    }
}
