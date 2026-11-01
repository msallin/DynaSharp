using DynaSharp.Expressions;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using static DynaSharp.Expr;

namespace DynaSharp.IntegrationTests.Commands;

/// <summary>Every command on a table without a schema, against the emulators.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class ItemCommandTests(Backend backend)
{
    [Test]
    public async Task Put_Item_IsStored()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["name"] = "Alice", ["n"] = 1 }).SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((stored!.GetString("name"), stored.GetNumber<int>("n"))).IsEqualTo(("Alice", 1));
    }

    [Test]
    public async Task Put_AttributeByAttribute_IsStored()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Put().With("pk", "a").With("sk", "s").With("name", "Alice").With("price", 12.5m).With("active", true).SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((stored!.GetString("name"), stored.GetNumber<decimal>("price"), stored.GetBool("active"))).IsEqualTo(("Alice", 12.5m, true));
    }

    [Test]
    public async Task Put_Await_SendsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" });
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(stored).IsNotNull();
    }

    [Test]
    public async Task Put_IfNotExistsOnANewKey_Stores()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).IfNotExists().SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(stored!.GetNumber<int>("v")).IsEqualTo(1);
    }

    [Test]
    public async Task Put_IfNotExistsOnAnExistingKey_FailsWithTheStoredItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 2 }).IfNotExists().ReturnOldOnFailure().SendAsync());

        // Assert
        await Assert.That(failure.Item!["v"].AsNumber<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Put_IfExistsOnANewKey_Fails()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).IfExists().SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    [Test]
    public async Task Put_ReturnOld_ReadsTheReplacedItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var result = await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 2 }).ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.Attributes!.GetNumber<int>("v")).IsEqualTo(1);
    }

    [Test]
    public async Task Put_ReturnOldOfANewItem_HasNoAttributes()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.HasAttributes).IsFalse();
    }

    [Test]
    public async Task Put_ReturnCapacity_ReportsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity!.CapacityUnits > 0).IsTrue();
    }

    [Test]
    public async Task Get_MissingItem_IsNotFound()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await sandbox.Shop.Get("nobody", "s").Consistent().SendAsync();

        // Assert
        await Assert.That(result.Found).IsFalse();
    }

    [Test]
    public async Task Get_Projection_ReturnsThosePaths()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var address = AttributeValue.FromMap([KeyValuePair.Create("city", (AttributeValue)"Bern"), KeyValuePair.Create("zip", (AttributeValue)"3000")]);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["name"] = "Alice", ["status"] = "open", ["address"] = address, ["lines"] = AttributeValue.FromList(["x", "y"]) }).SendAsync();

        // Act
        var result = await sandbox.Shop.Get("a", "s").Consistent().Project("status", "address.city", "lines[1]").SendAsync();

        // Assert
        var item = result.Item!;
        await Assert.That((item.Count, item.GetString("status"), item.GetMap("address")!.GetString("city"), item.GetMap("address")!.Count, item.GetList("lines")![0].AsString())).IsEqualTo((3, "open", "Bern", 1, "y"));
    }

    [Test]
    public async Task Get_Capacity_ReportsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).SendAsync();

        // Act
        var result = await sandbox.Shop.Get("a", "s").Consistent().ReturnCapacity().SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity!.CapacityUnits > 0).IsTrue();
    }

    [Test]
    public async Task Update_NewKey_CreatesTheItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Update("a", "s").Set("name", "Alice").SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(stored!.GetString("name")).IsEqualTo("Alice");
    }

    [Test]
    public async Task Update_IfExistsOnANewKey_Fails()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Update("a", "s").Set("name", "Alice").IfExists().SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    [Test]
    public async Task Update_IfNotExistsOnAnExistingKey_Fails()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s" }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Update("a", "s").Set("name", "Alice").IfNotExists().SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    [Test]
    public async Task Update_EveryClause_Applies()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["gone"] = 1, ["kept"] = 1, ["tags"] = AttributeValue.FromStringSet(["x", "y"]), ["count"] = 5 }).SendAsync();

        // Act
        await sandbox.Shop.Update("a", "s")
            .Set("name", "Alice")
            .Add("count", 2)
            .Add("more", AttributeValue.FromStringSet(["z"]))
            .Remove("gone")
            .Delete("tags", AttributeValue.FromStringSet(["x"]))
            .SetIfNotExists("kept", 2)
            .SetIfNotExists("fresh", 3)
            .SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((
            stored!.GetString("name"),
            stored.GetNumber<int>("count"),
            stored.GetStringSet("more")!.Single(),
            stored.ContainsKey("gone"),
            stored.GetStringSet("tags")!.Single(),
            stored.GetNumber<int>("kept"),
            stored.GetNumber<int>("fresh"))).IsEqualTo(("Alice", 7, "z", false, "y", 1, 3));
    }

    [Test]
    public async Task Update_ReservedWords_Work()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Update("a", "s").Set("status", "open").Set("size", 3).Set("order", "x").Set("name", "n").Set("count", 1).Set("data", "d").SendAsync();
        await sandbox.Shop.Update("a", "s").Add("size", 1).Remove("order").If(Attr("status").Eq("open").And(Attr("count").Exists())).SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((stored!.GetNumber<int>("size"), stored.ContainsKey("order"))).IsEqualTo((4, false));
    }

    [Test]
    public async Task Update_NestedPathsAndListIndexes_Work()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var line = AttributeValue.FromMap([KeyValuePair.Create("sku", (AttributeValue)"s1"), KeyValuePair.Create("qty", (AttributeValue)1)]);
        await sandbox.Shop.Put(new Item
        {
            ["pk"] = "a",
            ["sk"] = "s",
            ["address"] = AttributeValue.FromMap([KeyValuePair.Create("city", (AttributeValue)"Bern"), KeyValuePair.Create("zip", (AttributeValue)"3000")]),
            ["lines"] = AttributeValue.FromList([line, line]),
            ["matrix"] = AttributeValue.FromList([AttributeValue.FromList([0, 1])]),
        }).SendAsync();

        // Act
        await sandbox.Shop.Update("a", "s")
            .Set("address.city", "Zurich")
            .Remove("address.zip")
            .Set("lines[1].sku", "s2")
            .Add("lines[0].qty", 4)
            .Set("matrix[0][1]", 9)
            .If(Attr("address.city").Eq("Bern").And(Attr("lines[0].sku").BeginsWith("s")))
            .SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        var lines = stored!.GetList("lines")!;
        await Assert.That((
            stored.GetMap("address")!.GetString("city"),
            stored.GetMap("address")!.ContainsKey("zip"),
            lines[1].AsMap()["sku"].AsString(),
            lines[0].AsMap()["qty"].AsNumber<int>(),
            stored.GetList("matrix")![0].AsList()[1].AsNumber<int>())).IsEqualTo(("Zurich", false, "s2", 5, 9));
    }

    [Test]
    public async Task Update_InterpolatedSet_RunsArithmeticAndListAppend()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["total"] = 10, ["tags"] = AttributeValue.FromList(["a"]) }).SendAsync();

        // Act
        await sandbox.Shop.Update("a", "s")
            .Set($"{Attr("total")} = {Attr("total")} + {5}")
            .Set($"{Attr("tags")} = list_append({Attr("tags")}, {AttributeValue.FromList(["b"])})")
            .SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((stored!.GetNumber<int>("total"), stored.GetList("tags")!.Count)).IsEqualTo((15, 2));
    }

    [Test]
    public async Task Update_ReturnNew_ReadsTheWholeItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var result = await sandbox.Shop.Update("a", "s").Set("w", 2).ReturnNew().SendAsync();

        // Assert
        await Assert.That((result.Attributes!.GetNumber<int>("v"), result.Attributes.GetNumber<int>("w"), result.Attributes.GetString("pk"))).IsEqualTo((1, 2, "a"));
    }

    [Test]
    public async Task Update_ReturnOld_ReadsTheItemBefore()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var result = await sandbox.Shop.Update("a", "s").Set("v", 2).ReturnOld().SendAsync();

        // Assert
        await Assert.That(result.Attributes!.GetNumber<int>("v")).IsEqualTo(1);
    }

    [Test]
    public async Task Update_Return_ReadsTheCounter()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["count"] = 41 }).SendAsync();

        // Act
        var count = await sandbox.Shop.Update("a", "s").Add("count", 1).MaxAttempts(1).Return(Attr<int>("count")).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(42);
    }

    [Test]
    public async Task Update_ReturnOfANestedAttribute_ReadsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["stats"] = AttributeValue.FromMap([KeyValuePair.Create("count", (AttributeValue)1), KeyValuePair.Create("other", (AttributeValue)7)]) }).SendAsync();

        // Act
        var count = await sandbox.Shop.Update("a", "s").Add("stats.count", 1).Return(Attr<int>("stats.count")).SendAsync();

        // Assert
        await Assert.That(count).IsEqualTo(2);
    }

    [Test]
    public async Task Update_ReturnOfAnUntypedAttribute_ReadsTheValue()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var value = await sandbox.Shop.Update("a", "s").Set("name", "Alice").Return(Attr("name")).SendAsync();

        // Assert
        await Assert.That(value.AsString()).IsEqualTo("Alice");
    }

    [Test]
    public async Task Update_ReturnOfARemovedAttribute_ThrowsWithTheUpdateApplied()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["count"] = 1 }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<DynamoDbMappingException>(async () =>
            await sandbox.Shop.Update("a", "s").Remove("count").Return(Attr<int>("count")).SendAsync());
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((failure.AttributeName, stored!.ContainsKey("count"))).IsEqualTo(("count", false));
    }

    [Test]
    public async Task Update_ConditionFails_ReturnsTheItemOnTheException()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Update("a", "s").Set("v", 2).If(Attr("v").Eq(5)).ReturnOldOnFailure().SendAsync());

        // Assert
        await Assert.That(failure.Item!["v"].AsNumber<int>()).IsEqualTo(1);
    }

    [Test]
    public async Task Update_Capacity_ReportsItAndTheCommandCanBeTheLastInTheChain()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await sandbox.Shop.Update("a", "s").Set("v", 2).ReturnCapacity(ReturnConsumedCapacity.Indexes).SendAsync();

        // Assert
        await Assert.That(result.ConsumedCapacity!.CapacityUnits > 0).IsTrue();
    }

    [Test]
    public async Task Delete_ExistingItem_RemovesItAndReturnsIt()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["v"] = 1 }).SendAsync();

        // Act
        var result = await sandbox.Shop.Delete("a", "s").ReturnOld().SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((result.Attributes!.GetNumber<int>("v"), stored)).IsEqualTo((1, (Item?)null));
    }

    [Test]
    public async Task Delete_MissingItem_IsNoError()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var result = await sandbox.Shop.Delete("nobody", "s").SendAsync();

        // Assert
        await Assert.That(result.HasAttributes).IsFalse();
    }

    [Test]
    public async Task Delete_IfExistsOnAMissingItem_Fails()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Delete("nobody", "s").IfExists().SendAsync());

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ConditionalCheckFailedException");
    }

    [Test]
    public async Task Delete_ConditionFails_LeavesTheItem()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        await sandbox.Shop.Put(new Item { ["pk"] = "a", ["sk"] = "s", ["orderCount"] = 1 }).SendAsync();

        // Act
        await Items.FailureOfAsync<ConditionalCheckFailedException>(async () =>
            await sandbox.Shop.Delete("a", "s").If(Attr("orderCount").Eq(0)).SendAsync());
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That(stored).IsNotNull();
    }

    [Test]
    public async Task Command_Character_IsAStringOnEveryPath()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Put().With("pk", "a").With("sk", "s").With("put", 'p').SendAsync();
        await sandbox.Shop.Update("a", "s").Set("updated", 'u').If(Attr("put").Eq('p')).If($"{Attr("put")} = {'p'}").SendAsync();
        var stored = await sandbox.ReadAsync("a");

        // Assert
        await Assert.That((stored!["put"], stored["updated"])).IsEqualTo(((AttributeValue)"p", (AttributeValue)"u"));
    }

    [Test]
    public async Task Command_NameThatNeedsEscaping_RoundTrips()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        await sandbox.Shop.Update("a\"b", "é").Set("na\"me", "va\"lue é").SendAsync();
        var stored = await sandbox.ReadAsync("a\"b", "é");

        // Assert
        await Assert.That(stored!.GetString("na\"me")).IsEqualTo("va\"lue é");
    }

    [Test]
    public async Task Command_ValidationFailure_CarriesTheExpression()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(async () =>
            await DanglingAnd(sandbox.Shop.Update("a", "s")).SendAsync());

        // Assert
        await Assert.That((failure.Expressions!["ConditionExpression"], failure.Message.Contains("Expression: #n0 = :v0 AND, where #n0 = status, :v0 (S)", StringComparison.Ordinal))).IsEqualTo(("#n0 = :v0 AND", true));
    }

    [Test]
    public async Task Command_MissingTable_Throws()
    {
        // Arrange
        await using var sandbox = await CommandSandbox.StartAsync(backend);
        var table = sandbox.Client.Table("no-such-table", "pk");

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(async () => await table.Get("a").SendAsync());

        // Assert
        await Assert.That((failure.OperationName, failure.TableName)).IsEqualTo(((string?)"GetItem", (string?)"no-such-table"));
    }

    // The analyzer refuses this text in an interpolated string, so the handler is driven by hand, as the compiler would.
    private static UpdateCommand<Item> DanglingAnd(UpdateCommand<Item> command)
    {
        var condition = new ExpressionHandler(0, 2, command);
        condition.AppendFormatted(Expr.Attr("status"));
        condition.AppendLiteral(" = ");
        condition.AppendFormatted((AttributeValue)"open");
        condition.AppendLiteral(" AND");
        return command.If(condition);
    }
}
