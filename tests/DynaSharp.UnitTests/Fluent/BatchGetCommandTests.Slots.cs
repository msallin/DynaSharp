using DynaSharp.Testing;
using static DynaSharp.UnitTests.Fluent.BatchStubs;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>The gets of a batch get that hand out a slot: keys of other tables, and of other types on its own table.</summary>
public sealed partial class BatchGetCommandTests
{
    [Test]
    public async Task ToRequestJson_SlotOnAnotherTable_GroupsTheKeysByTableAndProjectsOnlyItsOwn()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id", "n");

        // Act
        var json = client.Table("Shop", "pk").BatchGet()
            .Get("a")
            .Get(other, "x", 1, out _)
            .Get("b")
            .Consistent()
            .Project("v")
            .ToRequestJson();

        // Assert
        await Assert.That(json).IsEqualTo(
            """{"RequestItems":{"Shop":{"Keys":[{"pk":{"S":"a"}},{"pk":{"S":"b"}}],"ConsistentRead":true,"ProjectionExpression":"#n0","ExpressionAttributeNames":{"#n0":"v"}},"Other":{"Keys":[{"id":{"S":"x"},"n":{"N":"1"}}],"ConsistentRead":true}}}""");
    }

    [Test]
    public async Task SendAsync_SlotOnAnotherTable_ReadsItsItemAndLeavesItOutOfItems()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get("a").Get(other, "x", out var slot).SendAsync();

        // Assert
        await Assert.That((result.Items.Single().GetString("pk"), result.Read(slot).Item!.GetString("id"))).IsEqualTo(("a", "x"));
    }

    [Test]
    [Arguments("1.50", "1.5")]
    [Arguments("100", "1E2")]
    [Arguments("-0.5", "-.50")]
    public async Task SendAsync_NumberKeyInAnotherForm_FindsTheItem(string sent, string returned)
    {
        // Arrange (DynamoDB returns a number in its own form)
        using var handler = new StubHandler(_ => StubResponse.Json($$$"""{"Responses":{"Other":[{"id":{"N":"{{{returned}}}"},"v":{"S":"found"}}]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, AttributeValue.FromNumber(sent), out var slot).SendAsync();

        // Assert
        await Assert.That(result.Read(slot).Item!.GetString("v")).IsEqualTo("found");
    }

    [Test]
    public async Task SendAsync_StringKeyEscapedInTheResponse_FindsTheItem()
    {
        // Arrange (the response escapes what the request did not)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Other":[{"id":{"S":"café \"x\""},"n":{"B":"AQI="}}]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id", "n");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "café \"x\"", new byte[] { 1, 2 }, out var slot).SendAsync();

        // Assert
        await Assert.That(result.Read(slot).Found).IsTrue();
    }

    [Test]
    public async Task SendAsync_NumberKeyThatIsNoNumber_MatchesNothing()
    {
        // Arrange (an answer DynamoDB would not give: a key that is no number)
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Other":[{"id":{"N":"x"}}]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, 1, out var slot).SendAsync();

        // Assert
        await Assert.That(result.Read(slot).Found).IsFalse();
    }

    [Test]
    public async Task SendAsync_AnotherTypeOnTheSameTable_GoesToItsSlotAndTheRestIsReadAsTheEntity()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"Responses":{"Shop":[{"pk":{"S":"ORDER#1"},"sk":{"S":"x"},"total":{"N":"5"}},{"pk":{"S":"CUSTOMER#a"},"_type":{"S":"CUSTOMER"},"fullName":{"S":"Alice"}}]}}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var result = await client.Table<Customer>("Shop").BatchGet().Get(Customer.Keys.Of("a")).Get(shop, "ORDER#1", "x", out var order).SendAsync();

        // Assert
        await Assert.That((result.Items.Single().FullName, result.Read(order).Item!.GetNumber<int>("total"))).IsEqualTo(("Alice", 5));
    }

    [Test]
    public async Task SendAsync_ItemOfAnotherTableThatNoSlotAskedFor_IsLeftAlone()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Other":[{"id":{"S":"y"}},{"id":{"S":"x"}}]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "x", out var slot).SendAsync();

        // Assert
        await Assert.That((result.Items.Count, result.Read(slot).Item!.GetString("id"))).IsEqualTo((0, "x"));
    }

    [Test]
    public async Task SendAsync_SlotOfAKeyWrittenByHand_ReadsItsItem()
    {
        // Arrange (a key that is a class, where a generated one is a struct)
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "pk");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, new TextKey("x"), out var slot).SendAsync();

        // Assert
        await Assert.That(result.Read(slot).Item!.GetString("pk")).IsEqualTo("x");
    }

    [Test]
    public async Task SendAsync_KeyWithoutAnItem_ReadsAsNotFound()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Other":[]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "x", out var slot).SendAsync();

        // Assert
        await Assert.That((result.Read(slot).Found, result.Read(slot).Item)).IsEqualTo((false, (Item?)null));
    }

    [Test]
    public async Task SendAsync_OneKeyInTwoSlots_BothReadTheItem()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Other":[{"id":{"S":"x"}}]}}"""));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "x", out var first).Get(other, "x", out var second).SendAsync();

        // Assert
        await Assert.That((result.Read(first).Found, result.Read(second).Found)).IsEqualTo((true, true));
    }

    [Test]
    public async Task SendAsync_SlotsOfTwoTables_ReadEachFromItsTable()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");
        var third = client.Table("Third", "id");

        // Act
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "x", out var x).Get(third, "y", out var y).SendAsync();

        // Assert
        await Assert.That((result.Read(x).Item!.GetString("id"), result.Read(y).Item!.GetString("id"))).IsEqualTo(("x", "y"));
    }

    [Test]
    public async Task SendAsync_ItemOfTheEntityAfterAnItemOfASlot_NamesTheKeyOfTheItemThatFailed()
    {
        // Arrange (the second item of the array is the one the entity cannot read)
        using var handler = new StubHandler(_ => StubResponse.Json(
            """{"Responses":{"Shop":[{"pk":{"S":"ORDER#1"},"sk":{"S":"x"}},{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"},"_type":{"S":"ORDER"}}]}}"""));
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk", "sk");

        // Act
        var failure = await TestClient.CaptureAsync<DynamoDbMappingException>(
            async () => await client.Table<Customer>("Shop").BatchGet().Get(Customer.Keys.Of("a")).Get(shop, "ORDER#1", "x", out _).SendAsync());

        // Assert
        await Assert.That(failure.Key!.GetString("pk")).IsEqualTo("CUSTOMER#a");
    }

    [Test]
    public async Task Read_ItemThatDoesNotFitTheType_ThrowsThere()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("""{"Responses":{"Shop":[{"pk":{"S":"CUSTOMER#a"},"sk":{"S":"PROFILE"},"_type":{"S":"ORDER"}}]}}"""));
        using var client = TestClient.Create(handler);
        var result = await client.Table("Shop", "pk").BatchGet().Get(client.Table<Customer>("Shop"), Customer.Keys.Of("a"), out var slot).SendAsync();

        // Act
        var failure = TestClient.Catch(() => result.Read(slot));

        // Assert
        await Assert.That(failure).IsTypeOf<DynamoDbMappingException>();
    }

    [Test]
    public async Task Read_DefaultSlot_Throws()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var result = await client.Table("Shop", "pk").BatchGet().Get("a").SendAsync();

        // Act
        var failure = TestClient.Catch(() => result.Read(default(BatchSlot<Item>)));

        // Assert
        await Assert.That(failure.Message).Contains("default value");
    }

    [Test]
    public async Task Read_SlotOfAnotherBatch_Throws()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");
        await client.Table("Shop", "pk").BatchGet().Get(other, "x", out _).Get(other, "y", out var second).SendAsync();
        var result = await client.Table("Shop", "pk").BatchGet().Get(other, "x", out _).SendAsync();

        // Act
        var failure = TestClient.Catch(() => result.Read(second));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Read_BatchWithoutSlots_Throws()
    {
        // Arrange
        using var handler = new StubHandler(AnswerKeys);
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");
        await client.Table("Shop", "pk").BatchGet().Get(other, "x", out var slot).SendAsync();
        var result = await client.Table("Shop", "pk").BatchGet().Get("a").SendAsync();

        // Act
        var failure = TestClient.Catch(() => result.Read(slot));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Get_SlotOnTheOwnTableOfAProjectedBatch_ThrowsAndLeavesTheCommandUsable()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var command = client.Table("Shop", "pk").BatchGet().Project("v");
        var failure = RefuseSlot(command, client.Table("Shop", "pk"));

        // Act
        var json = command.Get("a").ToRequestJson();

        // Assert
        await Assert.That((failure is InvalidOperationException, json.Contains("\"a\"", StringComparison.Ordinal))).IsEqualTo((true, true));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Project_AfterASlotOnTheOwnTable_Throws(bool handles)
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = TestClient.Catch(() => _ = handles
            ? client.Table<Customer>("Shop").BatchGet().Get(shop, "x", out _).Project(Customer.Attrs.FullName)
            : client.Table<Customer>("Shop").BatchGet().Get(shop, "x", out _).Project("fullName"));

        // Assert
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task Project_SlotOnlyOnAnotherTable_Projects()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var json = client.Table("Shop", "pk").BatchGet().Get(other, "x", out _).Project("v").Get("a").ToRequestJson();

        // Assert
        await Assert.That(json).Contains("\"ProjectionExpression\":\"#n0\"");
    }

    [Test]
    public async Task Get_SlotWithoutTheSortKeyOfTheTable_ThrowsAtTheCall()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id", "n");

        // Act
        var failure = TestClient.Catch(() => _ = client.Table("Shop", "pk").BatchGet().Get(other, "x", out _));

        // Assert
        await Assert.That(failure).IsTypeOf<ArgumentException>();
    }

    [Test]
    public async Task Get_SlotOnANullTable_Throws()
    {
        // Arrange
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var failures = new[]
        {
            TestClient.Catch(() => _ = client.Table("Shop", "pk").BatchGet().Get((Table<Item>)null!, "x", out _)),
            TestClient.Catch(() => _ = client.Table("Shop", "pk").BatchGet().Get((Table<Customer>)null!, Customer.Keys.Of("a"), out _)),
        };

        // Assert
        await Assert.That(failures.All(static failure => failure is ArgumentNullException)).IsTrue();
    }

    [Test]
    public async Task SendAsync_GivesUpWithSlots_ReadsWhatWasAnsweredAndRefusesWhatIsLeft()
    {
        // Arrange (the first round answers x, finds no item under w, and leaves y and a; the second gets nothing done)
        using var handler = new StubHandler(LeaveAllKeys);
        handler.Enqueue(StubResponse.Json(
            """{"Responses":{"Other":[{"id":{"S":"x"}}]},"UnprocessedKeys":{"Other":{"Keys":[{"id":{"S":"y"}}],"ConsistentRead":true},"Shop":{"Keys":[{"pk":{"S":"a"}}],"ConsistentRead":true}}}"""));
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var other = client.Table("Other", "id");
        var shop = client.Table("Shop", "pk");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await shop.BatchGet()
            .Get("a").Get(other, "x", out _).Get(other, "y", out _).Get(shop, "w", out _).Consistent().MaxStalledRounds(1).SendAsync());

        // Assert
        await Assert.That((failure.Read(SlotOf<Item>(0)).Found, failure.Read(SlotOf<Item>(2)).Found, TestClient.Catch(() => failure.Read(SlotOf<Item>(1))) is InvalidOperationException, failure.TableName))
            .IsEqualTo((true, false, true, (string?)null));
    }

    [Test]
    public async Task SendAsync_GivesUpBeforeTheRequestOfASlot_RefusesIt()
    {
        // Arrange (100 keys in the first request, which stays throttled, and the slot in the second)
        using var handler = new StubHandler(LeaveAllKeys);
        using var client = TestClient.Create(handler, random: FixedRandom.Zero);
        var ids = Enumerable.Range(0, 100).Select(static i => new TextKey("id-" + i));
        var other = client.Table("Other", "id");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(
            async () => await client.Table("Shop", "pk").BatchGet().GetAll(ids).Get(other, "x", out _).MaxStalledRounds(1).SendAsync());

        // Assert
        await Assert.That((TestClient.Catch(() => failure.Read(SlotOf<Item>(0))) is InvalidOperationException, failure.UnprocessedKeys["Other"].Keys.Count)).IsEqualTo((true, 1));
    }

    [Test]
    public async Task SendAsync_FirstRequestTimesOutWithASlot_RefusesIt()
    {
        // Arrange
        using var handler = new StubHandler(_ => throw new TimeoutException("No answer."));
        using var client = TestClient.Create(handler);
        var other = client.Table("Other", "id");

        // Act
        var failure = await TestClient.CaptureAsync<BatchGetIncompleteException<Item>>(async () => await client.Table("Shop", "pk").BatchGet().Get(other, "x", out _).SendAsync());

        // Assert
        await Assert.That(TestClient.Catch(() => failure.Read(SlotOf<Item>(0)))).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task Read_ExceptionThatNoBatchThrew_Throws()
    {
        // Arrange
        var failure = new BatchGetIncompleteException<Item>("Given up.", new Dictionary<string, DynaSharp.Requests.KeysAndAttributes>(), []);

        // Act
        var thrown = TestClient.Catch(() => failure.Read(SlotOf<Item>(0)));

        // Assert
        await Assert.That(thrown).IsTypeOf<ArgumentException>();
    }

    private static InvalidOperationException? RefuseSlot(BatchGetCommand<Item> command, Table<Item> table)
    {
        try
        {
            _ = command.Get(table, "x", out _);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    // A slot as a batch hands it out: the slots of a batch are numbered from 0, in the order of their gets.
    private static BatchSlot<T> SlotOf<T>(int index)
        where T : DynaSharp.Mapping.IDynamoDbDocument<T> => new(index);
}
