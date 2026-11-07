using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Serialization;
using static DynaSharp.Expr;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// The wire shape of the commands against botocore: for every vector there is a command that means the same, and both bodies must say the same
/// once their placeholders are resolved. The expected side comes from generate_operation_vectors.py, so nothing here confirms the implementation with itself.
/// </summary>
public sealed partial class CommandVectorTests
{
    // The token the vectors of a transaction carry, which botocore does not fill in.
    private const string Token = "3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54";

    private static readonly string[] ExpressionMembers = ["UpdateExpression", "ConditionExpression", "ProjectionExpression", "KeyConditionExpression", "FilterExpression"];

    // The command for each vector of the request API that a command can express.
    private static readonly Dictionary<(string Operation, string Name), Func<DynamoDbClient, string>> Commands = new()
    {
        [("PutItem", "ReturnItemCollectionMetrics")] = client => Orders(client).Put(new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" }).ReturnMetrics().ToRequestJson(),
        [("UpdateItem", "Minimal")] = client => client.Table("Users", "pk").Update("user-1").ToRequestJson(),
        [("UpdateItem", "ConditionalUpdateFromApiReference")] = client => Thread(client).Update("Amazon DynamoDB", "Maximum number of items?")
            .Set("LastPostedBy", "alice@example.com")
            .If(Attr("LastPostedBy").Eq("fred@example.com"))
            .ReturnNew()
            .ToRequestJson(),
        [("UpdateItem", "AtomicCounterFromApiReference")] = client => Thread(client).Update("Amazon DynamoDB", "A question about updates")
            .Set($"{Attr("Replies")} = {Attr("Replies")} + {1}")
            .ToRequestJson(),
        [("UpdateItem", "AllOptions")] = client => Orders(client).Update("CUSTOMER#1", "ORDER#1")
            .Set("status", "SHIPPED")
            .Set($"{Attr("visits")} = {Attr("visits")} + {1}")
            .Remove("tempFlag")
            .Add("tags", AttributeValue.FromStringSet(["fragile"]))
            .Delete("oldTags", AttributeValue.FromStringSet(["draft"]))
            .If($"{Attr("status")} = {"OPEN"} AND {Attr("version")} = {7}")
            .ReturnOldOnFailure()
            .ReturnCapacity(ReturnConsumedCapacity.Indexes)
            .ReturnMetrics()
            .Return(Attr("status"))
            .ToRequestJson(),
        [("UpdateItem", "ReturnValuesAllOld")] = client => client.Table("Users", "pk").Update("user-1").Set("a", 1).ReturnOld().ToRequestJson(),
        [("UpdateItem", "ReturnValuesAllNew")] = client => client.Table("Users", "pk").Update("user-1").Set("a", 1).ReturnNew().ToRequestJson(),
        [("UpdateItem", "ReturnValuesUpdatedNew")] = client => client.Table("Users", "pk").Update("user-1").Set("a", 1).Return(Attr<int>("a")).ToRequestJson(),
        [("DeleteItem", "Minimal")] = client => client.Table("Users", "pk").Delete("user-1").ToRequestJson(),
        [("DeleteItem", "FromApiReference")] = client => Thread(client).Delete("Amazon DynamoDB", "How do I update multiple items?")
            .If(Attr("Replies").NotExists())
            .ReturnOld()
            .ToRequestJson(),
        [("DeleteItem", "AllOptions")] = client => Orders(client).Delete("CUSTOMER#1", "ORDER#1")
            .If($"{Attr("status")} = {"CANCELLED"} AND {Attr("version")} = {7}")
            .ReturnOld()
            .ReturnOldOnFailure()
            .ReturnCapacity()
            .ReturnMetrics()
            .ToRequestJson(),
        [("DeleteItem", "ConsumedCapacityIndexes")] = client => client.Table("Users", "pk").Delete("user-1").ReturnCapacity(ReturnConsumedCapacity.Indexes).ToRequestJson(),
        [("Query", "Minimal")] = client => client.Table("Users", "pk").Query("user-1").ToRequestJson(),
        [("Query", "RangeOfItemsFromApiReference")] = client => client.Table("Reply", "Id", "ReplyDateTime").Index("PostedBy-Index", "Id", "PostedBy")
            .Query("Amazon DynamoDB#DynamoDB Thread 1", SortKeyCondition.Between("User A", "User C"))
            .Limit(3)
            .Consistent()
            .Project("Id", "PostedBy", "ReplyDateTime")
            .ReturnCapacity()
            .ToRequestJson(),
        [("Query", "CountItemsFromApiReference")] = client => client.Table("Thread", "ForumName", "Subject").Query("Amazon DynamoDB").Consistent().ToRequestJson(),
        [("Query", "ScanIndexForwardFalseWithLimitOne")] = client => client.Table("Users", "pk").Query("user-1").Descending().Limit(1).ToRequestJson(),
        [("Query", "LimitIntMax")] = client => client.Table("Users", "pk").Query("user-1").Limit(int.MaxValue).ToRequestJson(),
        [("Query", "SpecialCharacters")] = client => client.Table("Users", OperationVectors.SpecialText).Query(OperationVectors.SpecialText)
            .Filter($"contains({new Attr(EscapedPath)}, {OperationVectors.SpecialText})")
            .ToRequestJson(),
        [("Scan", "Minimal")] = client => client.Table("Users", "pk").Scan().ToRequestJson(),
        [("Scan", "ReturnAllItemsFromApiReference")] = client => client.Table("Reply", "Id").Scan().ReturnCapacity().ToRequestJson(),
        [("Scan", "FilterExpressionFromApiReference")] = client => client.Table("Reply", "Id").Scan().Filter(Attr("PostedBy").Eq("joe@example.com")).ReturnCapacity().ToRequestJson(),
        [("Scan", "ParallelSegment")] = client => client.Table("Users", "pk").Scan().Segment(3, of: 4).ToRequestJson(),
        [("Scan", "SegmentZeroOfOne")] = client => client.Table("Users", "pk").Scan().Segment(0, of: 1).ToRequestJson(),
        [("Scan", "SegmentBounds")] = client => client.Table("Users", "pk").Scan().Segment(999999, of: 1000000).ToRequestJson(),
        [("Scan", "LimitIntMax")] = client => client.Table("Users", "pk").Scan().Limit(int.MaxValue).ToRequestJson(),
        [("Scan", "SecondaryIndex")] = client => client.Table("Orders", "pk").Index("ByStatus", "pk", "status").Scan().Consistent().ToRequestJson(),
        [("BatchGetItem", "Minimal")] = client => client.Table("Users", "pk").BatchGet().Get("user-1").ToRequestJson(),
        [("BatchGetItem", "TableArnAsKey")] = client => client.Table(OperationVectors.TableArn, "pk").BatchGet().Get("user-1").ReturnCapacity().ToRequestJson(),
        [("BatchGetItem", "BinaryAndNumberKeys")] = client => client.Table("Blobs", "pk", "sk").BatchGet()
            .Get(new byte[] { 0, 255, 107, 101, 121 }, AttributeValue.FromNumber("-42.5"))
            .Get(new byte[] { 1 }, AttributeValue.FromNumber("1E+3"))
            .ToRequestJson(),
        [("BatchGetItem", "HundredKeys")] = client => Numbered(client.Table("Users", "pk", "sk").BatchGet(), 100).ToRequestJson(),
        [("BatchGetItem", "SpecialCharacters")] = client => client.Table(OperationVectors.SpecialText, OperationVectors.SpecialText).BatchGet()
            .Get(OperationVectors.SpecialText)
            .Project(EscapedPath)
            .ToRequestJson(),
        [("BatchWriteItem", "Minimal")] = client => client.Table("Users", "pk").BatchWrite().Put(new Item { ["pk"] = "user-1" }).ToRequestJson(),
        [("BatchWriteItem", "PutAndDeleteInOneTable")] = client => client.Table("Users", "pk").BatchWrite()
            .Put(new Item { ["pk"] = "user-1", ["name"] = "Ada" })
            .Delete("user-2")
            .Delete("user-3")
            .ToRequestJson(),
        [("BatchWriteItem", "MultipleTablesAllOptions")] = client => Orders(client).BatchWrite()
            .Put(new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" })
            .Put(client.Table("Customers", "pk"), new Item { ["pk"] = "CUSTOMER#1" })
            .Delete("CUSTOMER#1", "ORDER#0")
            .ReturnCapacity(ReturnConsumedCapacity.Indexes)
            .ReturnMetrics()
            .ToRequestJson(),
        [("BatchWriteItem", "TwentyFivePuts")] = client => Numbered(client.Table("Users", "pk", "sk").BatchWrite(), 25).ToRequestJson(),
        [("BatchWriteItem", "TableArnAsKey")] = client => client.Table(OperationVectors.TableArn, "pk").BatchWrite().Delete("user-1").ToRequestJson(),
        [("BatchWriteItem", "SpecialCharacters")] = client => client.Table(OperationVectors.SpecialText, OperationVectors.NaiveName).BatchWrite()
            .Put(new Item { [OperationVectors.SpecialText] = OperationVectors.SpecialText })
            .Delete(new string((char)0xE9, 1))
            .ReturnMetrics()
            .ToRequestJson(),
        [("TransactGetItems", "Minimal")] = client => client.TransactGet().Get(client.Table("Users", "pk"), "user-1", default, out _).ToRequestJson(),
        [("TransactGetItems", "ReturnConsumedCapacityIndexes")] = client => client.TransactGet().Get(client.Table("Users", "pk"), "user-1", default, out _).ReturnCapacity(ReturnConsumedCapacity.Indexes).ToRequestJson(),
        [("TransactGetItems", "HundredActions")] = client => Numbered(client.TransactGet(), client.Table("Users", "pk", "sk"), 100).ToRequestJson(),
        [("TransactWriteItems", "PutOnly")] = client => client.TransactWrite().Token(Token).Put(client.Table("Users", "pk"), new Item { ["pk"] = "user-1" }).ToRequestJson(),
        [("TransactWriteItems", "EveryActionKindMinimal")] = client => client.TransactWrite()
            .Check(client.Table("Customers", "pk"), "CUSTOMER#1").If($"attribute_exists({Attr("pk")})")
            .Put(Orders(client), new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1" })
            .Update(client.Table("Customers", "pk"), "CUSTOMER#1").Add("orders", 1)
            .Delete(client.Table("Coupons", "code"), "WELCOME10")
            .Token(Token)
            .ToRequestJson(),
        [("TransactWriteItems", "EveryActionKindWithEveryOption")] = client => client.TransactWrite()
            .Check(client.Table("Stock", "sku", "warehouse"), "SKU-1", 7).If(Attr("quantity").Ge(2)).ReturnOldOnFailure()
            .Put(Orders(client), new Item { ["pk"] = "CUSTOMER#1", ["sk"] = "ORDER#1", ["total"] = 12.5m }).IfNotExists().ReturnOldOnFailure()
            .Update(client.Table("Customers", "pk"), "CUSTOMER#1")
                .Set($"{Attr("orders")} = {Attr("orders")} + {1}")
                .Set("status", "ACTIVE")
                .If($"attribute_exists({Attr("pk")}) AND {Attr("version")} = {4}")
                .ReturnOldOnFailure()
            .Delete(client.Table("Coupons", "code"), "WELCOME10").If(Attr("used").Eq(false)).ReturnOldOnFailure()
            .ReturnCapacity(ReturnConsumedCapacity.Indexes)
            .ReturnMetrics()
            .Token(Token)
            .ToRequestJson(),
        [("TransactWriteItems", "HundredActions")] = client => Numbered(client.TransactWrite(), client.Table("Users", "pk", "sk"), 100).Token(Token).ToRequestJson(),
        [("TransactWriteItems", "SpecialCharacters")] = client => client.TransactWrite()
            .Put(client.Table(OperationVectors.SpecialText, "pk"), new Item { [OperationVectors.SpecialText] = OperationVectors.SpecialText })
            .If(new Attr(EscapedPath).Eq(OperationVectors.SpecialText))
            .Token(Token)
            .ToRequestJson(),
        [("TransactWriteItems", "ReturnConsumedCapacityTotalAndTableArn")] = client => client.TransactWrite()
            .Delete(client.Table(OperationVectors.TableArn, "pk"), "user-1")
            .ReturnCapacity()
            .Token("a")
            .ToRequestJson(),
    };

    // The vectors that only a sent request can show, because the terminal adds to the body: the start key of PageAsync, the Select of CountAsync.
    private static readonly Dictionary<(string Operation, string Name), Func<DynamoDbClient, Task>> Sent = new()
    {
        [("Query", "ExclusiveStartKeyWithBinaryAndNumber")] = async client => await client.Table("Blobs", "pk", "sk").Query(new byte[] { 0, 255, 107, 101, 121 })
            .PageAsync(ContinuationToken.FromKey(new Item { ["pk"] = new byte[] { 0, 255, 107, 101, 121 }, ["sk"] = AttributeValue.FromNumber("-42.5") })),
        [("Query", "SelectCount")] = async client => await client.Table("Users", "pk").Query("user-1").CountAsync(),
        [("Scan", "SelectCount")] = async client => await client.Table("Users", "pk").Scan().CountAsync(),
        [("Scan", "ParallelSegmentWithStartKey")] = async client => await client.Table("Users", "pk").Scan().Segment(3, of: 4).Limit(100)
            .PageAsync(ContinuationToken.FromKey(new Item { ["pk"] = "user-77" })),
    };

    // The command for each vector written for the commands.
    private static readonly Dictionary<(string Operation, string Name), Func<DynamoDbClient, string>> CommandOnly = new()
    {
        [("CreateTable", "TableWithoutAnEntity")] = client => Written(client.Table("Shop", "pk", "sk").CreateTable()),
        [("CreateTable", "UnionOfThreeEntities")] = client => Written(client.Table<SchemaOrder>("Shop").CreateTable().Including<SchemaCustomer>().Including<SchemaProduct>()),
        [("CreateTable", "ProvisionedWithGlobalAndLocalIndexes")] = client => Written(client.Table<SchemaOrder>("Shop").CreateTable().Provisioned(read: 5, write: 7)),
        [("CreateTable", "NumberAndBinaryKeys")] = client => Written(client.Table("Readings", "Sensor", "Stamp").CreateTable().Including<SchemaReading>()),
        [("GetItem", "Key")] = client => Shop(client).Get("CUSTOMER#alice", "PROFILE").ToRequestJson(),
        [("GetItem", "NumberKey")] = client => Shop(client).Get(7, 2.5m).ToRequestJson(),
        [("GetItem", "ConsistentProjection")] = client => Shop(client).Get("CUSTOMER#alice", "PROFILE").Consistent().Project("fullName", "address.city", "lines[0].sku").ReturnCapacity().ToRequestJson(),
        [("PutItem", "AllValueTypes")] = client => client.Table("Shop", "pk").Put()
            .With("pk", "a")
            .With("n", AttributeValue.FromNumber("-12.5E+3"))
            .With("b", new byte[] { 0, 1, 2, 253, 254, 255 })
            .With("t", true)
            .With("z", AttributeValue.Null)
            .With("ss", AttributeValue.FromStringSet(["a", "b"]))
            .With("l", AttributeValue.FromList(["x", AttributeValue.FromNumber("1")]))
            .With("m", AttributeValue.FromMap([KeyValuePair.Create("inner", (AttributeValue)"y")]))
            .ToRequestJson(),
        [("PutItem", "IfNotExists")] = client => client.Table("Shop", "pk").Put(new Item { ["pk"] = "a", ["name"] = "Alice" })
            .IfNotExists()
            .ReturnOld()
            .ReturnOldOnFailure()
            .ReturnCapacity(ReturnConsumedCapacity.Indexes)
            .ReturnMetrics()
            .ToRequestJson(),
        [("PutItem", "SpecialCharacters")] = client => client.Table("Shop", "pk").Put(new Item { ["pk"] = OperationVectors.SpecialText, [OperationVectors.NaiveName] = OperationVectors.SpecialText })
            .If(new Attr(EscapedPath).Ne(OperationVectors.SpecialText))
            .ToRequestJson(),
        [("UpdateItem", "ReservedWords")] = client => Shop(client).Update("CUSTOMER#alice", "PROFILE").Add("size", 1).Set("status", "open").Remove("order").ToRequestJson(),
        [("UpdateItem", "NestedPaths")] = client => Shop(client).Update("CUSTOMER#alice", "PROFILE").Set("address.city", "Bern").Set("lines[2].sku", "x-1").Remove("matrix[0][1]").ToRequestJson(),
        [("UpdateItem", "SetIfNotExists")] = client => Shop(client).Update("CUSTOMER#alice", "PROFILE").SetIfNotExists("visits", 0).ToRequestJson(),
        [("UpdateItem", "ListAppend")] = client => Shop(client).Update("CUSTOMER#alice", "PROFILE")
            .Set($"{Attr("tags")} = list_append({Attr("tags")}, {AttributeValue.FromList(["x"])})")
            .If($"attribute_exists({Attr("pk")}) AND size({Attr("tags")}) < {10}")
            .ToRequestJson(),
        [("UpdateItem", "Conditions")] = client => Shop(client).Update("CUSTOMER#alice", "PROFILE")
            .Set("a", 1)
            .If(Attr("v").Between(1, 2).And(Attr("s").BeginsWith("x")).Or(Attr("n").Ne(1).Negate()))
            .If(Attr("gone").NotExists())
            .ToRequestJson(),
        [("DeleteItem", "IfExists")] = client => Shop(client).Delete("CUSTOMER#alice", "PROFILE").IfExists().ReturnOldOnFailure().ToRequestJson(),
        [("Query", "Partition")] = client => Shop(client).Query("CUSTOMER#alice").ToRequestJson(),
        [("Query", "SortKeyEquals")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Eq("PROFILE")).ToRequestJson(),
        [("Query", "SortKeyBelow")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Lt("ORDER#5")).ToRequestJson(),
        [("Query", "SortKeyUpTo")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Le("ORDER#5")).ToRequestJson(),
        [("Query", "SortKeyAbove")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Gt(5)).ToRequestJson(),
        [("Query", "SortKeyFrom")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Ge("ORDER#5")).ToRequestJson(),
        [("Query", "SortKeyBetween")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.Between("ORDER#1", "ORDER#9")).ToRequestJson(),
        [("Query", "SortKeyBeginsWith")] = client => Shop(client).Query("CUSTOMER#alice", SortKeyCondition.BeginsWith("ORDER#")).ToRequestJson(),
        [("Query", "ReservedWords")] = client => Shop(client).Query("CUSTOMER#alice")
            .Filter($"{Attr("status")} = {"open"} AND {Attr("size")} > {1}")
            .Project("status", "order", "address.city")
            .ToRequestJson(),
        [("Query", "TwoFilters")] = client => Shop(client).Query("CUSTOMER#alice").Filter(Attr("a").Eq(1)).Filter($"size({Attr("tags")}) < {10}").ToRequestJson(),
        [("Query", "Entity")] = client => client.Table<Customer>("Shop").Query(Customer.Keys.Pk.Of("alice"), SortKeyCondition.BeginsWith("P")).Filter(Customer.Attrs.FullName.Eq("Alice")).ToRequestJson(),
        [("Query", "EntityProjection")] = client => client.Table<Customer>("Shop").Query(Customer.Keys.Pk.Of("alice")).Project("fullName").ToRequestJson(),
        [("Query", "EntityIndexProjection")] = client => client.Table<Customer>("Shop").Index("ByName").Query(Customer.Keys.ByNamePk.Of("Alice")).Project("email").ToRequestJson(),
        [("Scan", "Entity")] = client => client.Table<Customer>("Shop").Scan().Filter(Customer.Attrs.OrderCount.Gt(0)).ToRequestJson(),
        [("Scan", "Interpolated")] = client => Shop(client).Scan().Filter($"begins_with({Attr("sk")}, {"ORDER#"}) AND {Attr("amount")} > {100}").Project("sk", "amount").ToRequestJson(),
    };

    // The special text holds dots and brackets, which a path would take apart, so it stands in for the name through a handle of its own.
    private static string EscapedPath => OperationVectors.SpecialText.Replace('.', '-').Replace('[', '-');

    public static IEnumerable<string> RequestVectors() => Commands.Keys.Select(static key => key.Operation + "/" + key.Name);

    public static IEnumerable<string> CommandVectors() => CommandOnly.Keys.Select(static key => key.Operation + "/" + key.Name);

    public static IEnumerable<string> SentVectors() => Sent.Keys.Select(static key => key.Operation + "/" + key.Name);

    [Test]
    [MethodDataSource(nameof(RequestVectors))]
    public async Task ToRequestJson_VectorOfTheRequestApi_SaysTheSame(string vector)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(vector);
        var (operation, name) = Parse(vector);
        var expected = OperationVectors.Request(operation, name);
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = Commands[(operation, name)](client);

        // Assert
        await Assert.That(JsonComparer.Differences(Resolve(expected.Body), Resolve(json))).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(CommandVectors))]
    public async Task ToRequestJson_VectorOfACommand_SaysTheSame(string vector)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(vector);
        var (operation, name) = Parse(vector);
        var expected = OperationVectors.Commands.Single(command => command.Operation == operation && command.Name == name);
        using var handler = new StubHandler();
        using var client = TestClient.Create(handler);

        // Act
        var json = CommandOnly[(operation, name)](client);

        // Assert
        await Assert.That(JsonComparer.Differences(Resolve(expected.Body), Resolve(json))).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(SentVectors))]
    public async Task Send_VectorOfTheRequestApi_SaysTheSame(string vector)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(vector);
        var (operation, name) = Parse(vector);
        var expected = OperationVectors.Request(operation, name);
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await Sent[(operation, name)](client);

        // Assert
        await Assert.That(JsonComparer.Differences(Resolve(expected.Body), Resolve(handler.Requests.Single().BodyText))).IsEmpty();
    }

    [Test]
    public async Task CommandOnly_Always_CoversEveryVectorOfTheCommands()
    {
        // Arrange
        var vectors = OperationVectors.Commands.Select(static vector => (vector.Operation, vector.Name)).ToList();

        // Act
        var missing = vectors.Except(CommandOnly.Keys).Concat(CommandOnly.Keys.Except(vectors));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task SendAsync_Command_UsesTheTargetOfTheVector()
    {
        // Arrange
        using var handler = new StubHandler(_ => StubResponse.Json("{}"));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Users", "pk").Update("user-1").SendAsync();

        // Assert
        await Assert.That(handler.Requests.Single().Headers["X-Amz-Target"]).IsEqualTo(OperationVectors.Request("UpdateItem", "Minimal").Target);
    }

    [Test]
    public async Task DeleteTableAsync_Table_WritesWhatBotocoreWrites()
    {
        // Arrange
        var expected = OperationVectors.Request("DeleteTable", "DeleteATableFromApiReference");
        using var handler = new StubHandler(_ => StubResponse.Json("""{"TableDescription":{"TableName":"Reply"}}"""));
        using var client = TestClient.Create(handler);

        // Act
        await client.Table("Reply", "Id", "ReplyDateTime").DeleteTableAsync();

        // Assert
        var request = handler.Requests.Single();
        await Assert.That((request.Headers["X-Amz-Target"], JsonComparer.Differences(expected.Body, request.BodyText))).IsEqualTo((expected.Target, string.Empty));
    }

    private static string Written(CreateTableCommand command) => TestJson.Write(writer => CreateTableOperation.WriteRequest(writer, command.ToRequest()));

    private static BatchGetCommand<Item> Numbered(BatchGetCommand<Item> command, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var key = OperationVectors.NumberedKey(i);
            command = command.Get(key["pk"], key["sk"]);
        }

        return command;
    }

    private static BatchWriteCommand<Item> Numbered(BatchWriteCommand<Item> command, int count)
    {
        for (var i = 0; i < count; i++)
        {
            command = command.Put(NumberedItem(i));
        }

        return command;
    }

    private static TransactGetCommand Numbered(TransactGetCommand command, Table<Item> table, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var key = OperationVectors.NumberedKey(i);
            command = command.Get(table, key["pk"], key["sk"], out _);
        }

        return command;
    }

    private static Item NumberedItem(int index)
    {
        var key = OperationVectors.NumberedKey(index);
        return new Item { ["pk"] = key["pk"], ["sk"] = key["sk"] };
    }

    private static PutStep Numbered(TransactWriteCommand command, Table<Item> table, int count)
    {
        var step = command.Put(table, NumberedItem(0));
        for (var i = 1; i < count; i++)
        {
            step = step.Put(table, NumberedItem(i));
        }

        return step;
    }

    private static Table<Item> Orders(DynamoDbClient client) => client.Table("Orders", "pk", "sk");

    private static Table<Item> Thread(DynamoDbClient client) => client.Table("Thread", "ForumName", "Subject");

    private static Table<Item> Shop(DynamoDbClient client) => client.Table("Shop", "pk", "sk");

    private static (string Operation, string Name) Parse(string vector)
    {
        var parts = vector.Split('/');
        return (parts[0], parts[1]);
    }

    // {"ConditionExpression":"#n0 = :v0","ExpressionAttributeNames":{"#n0":"a"},"ExpressionAttributeValues":{":v0":{"S":"x"}}}
    // becomes {"ConditionExpression":"A = {\"S\":\"X\"}"}: every placeholder replaced, and the case of keywords and functions set aside.
    // A transaction has this in each action, and a batch get in each table.
    private static string Resolve(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        ResolveExpressions(root);
        foreach (var action in root["TransactItems"]?.AsArray() ?? [])
        {
            ResolveExpressions(action!.AsObject().Single().Value!.AsObject());
        }

        foreach (var (_, table) in root["RequestItems"] as JsonObject ?? [])
        {
            if (table is JsonObject keysAndAttributes)
            {
                ResolveExpressions(keysAndAttributes);
            }
        }

        return root.ToJsonString();
    }

    private static void ResolveExpressions(JsonObject owner)
    {
        var names = owner["ExpressionAttributeNames"]?.AsObject();
        var values = owner["ExpressionAttributeValues"]?.AsObject();
        foreach (var member in ExpressionMembers)
        {
            if (owner[member] is { } expression)
            {
                owner[member] = Placeholder().Replace(expression.GetValue<string>(), match => match.Value[0] == '#'
                    ? names![match.Value]!.GetValue<string>().Replace('.', '-').Replace('[', '-')
                    : values![match.Value]!.ToJsonString(new JsonSerializerOptions { Encoder = WireJson.WriterOptions.Encoder })).ToUpperInvariant();
            }
        }

        owner.Remove("ExpressionAttributeNames");
        owner.Remove("ExpressionAttributeValues");
    }

    [GeneratedRegex(@"[#:]\w+")]
    private static partial Regex Placeholder();
}
