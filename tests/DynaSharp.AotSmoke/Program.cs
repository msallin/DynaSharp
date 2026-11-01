using System.Diagnostics;
using System.Diagnostics.Metrics;
using DynaSharp;
using DynaSharp.AotSmoke;
using DynaSharp.Credentials;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using static DynaSharp.Expr;

// Exercises the public API in a Native AOT binary: stubbed GetItem, PutItem, DescribeTable and an error, through the
// DynamoDbClient with its telemetry. Exit code 0 means every check passed.
var failures = new List<string>();

var activities = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "DynaSharp",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activities.Add,
};
ActivitySource.AddActivityListener(activityListener);

var measurements = 0;
using var meterListener = new MeterListener();
meterListener.InstrumentPublished = static (instrument, listener) =>
{
    if (instrument.Meter.Name == "DynaSharp")
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meterListener.SetMeasurementEventCallback<double>((_, _, _, _) => measurements++);
meterListener.Start();

using var handler = new StubHandler();
handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"user-1"},"visits":{"N":"42"},"tags":{"SS":["a","b"]},"active":{"BOOL":true}}}"""));
handler.Enqueue(StubResponse.Json("""{"Attributes":{"pk":{"S":"user-1"},"visits":{"N":"41"}}}"""));
handler.Enqueue(StubResponse.Json("""{"Table":{"TableName":"Users","TableStatus":"ACTIVE","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"AttributeDefinitions":[{"AttributeName":"pk","AttributeType":"S"}],"CreationDateTime":1.421866952062E9,"ItemCount":2}}"""));
handler.Enqueue(StubResponse.DynamoDbError("ResourceNotFoundException", "Requested resource not found"));

var credentials = new StaticCredentialsProvider(new AwsCredentials("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"));
using var client = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2"), Credentials = credentials, HttpHandler = handler });

var got = await client.GetItemAsync(new GetItemRequest
{
    TableName = "Users",
    Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-1" },
    ConsistentRead = true,
});
Check(got.Item is not null && got.Item["pk"].AsString() == "user-1", "GetItem returns the key");
Check(got.Item is not null && got.Item["visits"].AsNumber<int>() == 42, "GetItem returns the number");
Check(got.Item is not null && got.Item["tags"].AsStringSet().Count == 2, "GetItem returns the string set");
Check(got.Item is not null && got.Item["active"].AsBool(), "GetItem returns the boolean");

var put = await client.PutItemAsync(new PutItemRequest
{
    TableName = "Users",
    Item = new Dictionary<string, AttributeValue> { ["pk"] = "user-1", ["visits"] = AttributeValue.FromNumber(43) },
    ConditionExpression = "attribute_exists(pk)",
    ReturnValues = ReturnValue.AllOld,
});
Check(put.Attributes is not null && put.Attributes["visits"].AsNumber<int>() == 41, "PutItem returns the replaced item");

var table = (await client.DescribeTableAsync(new DescribeTableRequest { TableName = "Users" })).Table!;
Check(table.TableStatus == TableStatus.Active && table.ItemCount == 2 && table.KeySchema.Count == 1, "DescribeTable returns the table");

try
{
    await client.GetItemAsync(new GetItemRequest { TableName = "Missing", Key = new Dictionary<string, AttributeValue> { ["pk"] = "x" } });
    Check(false, "an error response throws");
}
catch (ResourceNotFoundException exception)
{
    Check(exception.Message == "Requested resource not found", "the error message is kept");
}

var requests = handler.Requests;
Check(requests.Count == 4, "four requests were sent");
Check(requests[0].Operation == "GetItem", "the first request is GetItem");
Check(requests[0].BodyText == """{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"ConsistentRead":true}""", "the GetItem body is DynamoDB JSON");
Check(requests[1].BodyText == """{"TableName":"Users","Item":{"pk":{"S":"user-1"},"visits":{"N":"43"}},"ConditionExpression":"attribute_exists(pk)","ReturnValues":"ALL_OLD"}""", "the PutItem body is DynamoDB JSON");
Check(requests[0].Headers["Authorization"].StartsWith("AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/", StringComparison.Ordinal), "requests are signed");

Check(activities.Count == 4 && activities[0].OperationName == "DynamoDB.GetItem", "one activity per operation");
Check(measurements == 4, "one duration measurement per operation");

// A throttled request is sent again after a random wait of up to 50 ms, with its own signature.
handler.Enqueue(StubResponse.DynamoDbError("ProvisionedThroughputExceededException", "The level of configured provisioned throughput for the table was exceeded."));
handler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"user-2"}}}"""));
var retried = await client.GetItemAsync(new GetItemRequest { TableName = "Users", Key = new Dictionary<string, AttributeValue> { ["pk"] = "user-2" } });
Check(retried.Item is not null && retried.Item["pk"].AsString() == "user-2", "a throttled request is retried");
Check(handler.Requests.Count == 6 && handler.Requests[4].BodyText == handler.Requests[5].BodyText, "the retry sends the same body again");

// Table operations: the request carries the nested structures, the response the new members of the description.
handler.Enqueue(StubResponse.Json("""{"TableDescription":{"TableName":"Orders","TableStatus":"CREATING","GlobalSecondaryIndexes":[{"IndexName":"ByStatus","IndexStatus":"CREATING","Backfilling":true}],"StreamSpecification":{"StreamEnabled":true,"StreamViewType":"NEW_IMAGE"},"DeletionProtectionEnabled":true}}"""));
var created = await client.CreateTableAsync(new CreateTableRequest
{
    TableName = "Orders",
    AttributeDefinitions = [new("pk", ScalarAttributeType.String), new("status", ScalarAttributeType.String)],
    KeySchema = [new("pk", KeyType.Hash)],
    GlobalSecondaryIndexes = [new() { IndexName = "ByStatus", KeySchema = [new("status", KeyType.Hash)], Projection = new() { ProjectionType = ProjectionType.KeysOnly } }],
    BillingMode = BillingMode.PayPerRequest,
    StreamSpecification = new() { StreamEnabled = true, StreamViewType = StreamViewType.NewImage },
});
Check(created.TableDescription!.GlobalSecondaryIndexes.Single().Backfilling && created.TableDescription!.StreamSpecification?.StreamViewType == StreamViewType.NewImage, "CreateTable returns the indexes and the stream");
Check(handler.Requests[^1].BodyText.Contains("\"StreamViewType\":\"NEW_IMAGE\"", StringComparison.Ordinal), "the CreateTable body has the stream specification");

// DynamoDB Streams: another target prefix, another service name in the telemetry, records with images.
using var streamsHandler = new StubHandler();
streamsHandler.Enqueue(StubResponse.Json("""{"NextShardIterator":"next","Records":[{"eventID":"e1","eventName":"MODIFY","dynamodb":{"ApproximateCreationDateTime":1.46480431E9,"Keys":{"pk":{"S":"user-1"}},"NewImage":{"pk":{"S":"user-1"},"visits":{"N":"2"}},"OldImage":{"pk":{"S":"user-1"},"visits":{"N":"1"}},"StreamViewType":"NEW_AND_OLD_IMAGES"}}]}"""));
using var streamsClient = new DynamoDbStreamsClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2"), Credentials = credentials, HttpHandler = streamsHandler });
var records = await streamsClient.GetRecordsAsync(new GetRecordsRequest { ShardIterator = "iterator", Limit = 10 });
Check(records.NextShardIterator == "next" && records.Records.Single().DynamoDb?.NewImage?["visits"].AsNumber<int>() == 2, "GetRecords returns the records with their images");
Check(streamsHandler.Requests.Single().Headers["X-Amz-Target"] == "DynamoDBStreams_20120810.GetRecords", "GetRecords uses the streams target");
Check(activities[^1].OperationName == "DynamoDB Streams.GetRecords", "the streams activity is named after the streams service");

// The command API, on items and on an entity: generic code over structs and static abstract members, which is what AOT has to see through.
using var commandHandler = new StubHandler();
using var commandClient = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2"), Credentials = credentials, HttpHandler = commandHandler });
var users = commandClient.Table("Users", "pk");

commandHandler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"user-1"},"visits":{"N":"3"}}}"""));
var gotItem = await users.Get("user-1").Consistent().Project("visits").SendAsync();
Check(gotItem.Found && gotItem.Item!.GetNumber<int>("visits") == 3, "Get reads an Item");

commandHandler.Enqueue(StubResponse.Json("""{"Attributes":{"visits":{"N":"4"}}}"""));
var visits = await users.Update("user-1").Add("visits", 1).If($"attribute_exists({Attr("pk")}) AND {Attr("visits")} < {100}").Return(Attr<int>("visits")).SendAsync();
Check(visits == 4, "Update returns the attribute typed");
Check(
    commandHandler.Requests[^1].BodyText == """{"TableName":"Users","Key":{"pk":{"S":"user-1"}},"UpdateExpression":"ADD #n0 :v0","ConditionExpression":"attribute_exists(#n1) AND #n0 < :v1","ExpressionAttributeNames":{"#n0":"visits","#n1":"pk"},"ExpressionAttributeValues":{":v0":{"N":"1"},":v1":{"N":"100"}},"ReturnValues":"UPDATED_NEW"}""",
    "the Update body has placeholders for every name and value");

commandHandler.Enqueue(StubResponse.DynamoDbError("ValidationException", "Invalid expression."));
try
{
    await users.Put().With("pk", "user-2").With("visits", 1).If(Attr("visits").Eq(0).Or(Condition.Of(Attr("pk").NotExists()))).SendAsync();
    Check(false, "a rejected Put throws");
}
catch (ValidationException exception)
{
    Check(exception.ExpressionNames?["#n0"] == "visits" && exception.ExpressionValueTypes?[":v0"] == "N", "a rejected command explains its placeholders");
}

var visitors = users.Of<Visitor>();
Check(ReferenceEquals(visitors, users.Of<Visitor>()), "Of caches the table per type");

commandHandler.Enqueue(StubResponse.Json("""{"Attributes":{"pk":{"S":"v-1"},"visits":{"N":"2"}}}"""));
var replaced = await visitors.Put(new Visitor("v-1", 3)).ReturnOld().SendAsync();
Check(replaced.Attributes == new Visitor("v-1", 2), "Put reads the replaced entity");

commandHandler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"v-1"},"visits":{"N":"3"}}}"""));
var gotVisitor = await visitors.Get(new Visitor.Key("v-1")).SendAsync();
Check(gotVisitor.Item == new Visitor("v-1", 3), "Get reads the entity");

commandHandler.Enqueue(StubResponse.Json("{}"));
await visitors.Delete(new Visitor("v-1", 3)).IfExists().SendAsync();
Check(commandHandler.Requests[^1].Operation == "DeleteItem", "Delete takes the key from the entity");

// Reads: pages followed through an async iterator, a token through its text, a count, and a parallel scan on tasks and a channel.
commandHandler.Enqueue(StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":3,"LastEvaluatedKey":{"pk":{"S":"v-0"}}}"""));
commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"v-1"},"visits":{"N":"1"}}],"Count":1,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"v-1"}}}"""));
commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"v-2"},"visits":{"N":"2"}}],"Count":1,"ScannedCount":1}"""));
var followed = new List<Visitor>();
await foreach (var visitor in visitors.Query("v", default).Filter($"{Attr("visits")} > {0}").Project("visits").Limit(1).ItemsAsync())
{
    followed.Add(visitor);
}

Check(followed.Count == 2 && followed[1] == new Visitor("v-2", 2), "Query follows pages, an empty one too");
Check(commandHandler.Requests[^1].BodyText.EndsWith(""","ExclusiveStartKey":{"pk":{"S":"v-1"}}}""", StringComparison.Ordinal), "the next page sends the key of the last one");

commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"user-1"},"visits":{"N":"3"}}],"Count":1,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"user-1"}}}"""));
var firstPage = await users.Query("user-1").PageAsync();
commandHandler.Enqueue(StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":0}"""));
var lastPage = await users.Query("user-1").PageAsync(ContinuationToken.Parse(firstPage.Next.ToString()));
Check(firstPage.Items[0].GetNumber<int>("visits") == 3 && firstPage.Next.ToKey()?.GetString("pk") == "user-1" && !lastPage.HasMore, "a token round-trips through its text");

commandHandler.Enqueue(StubResponse.Json("""{"Count":2,"ScannedCount":5}"""));
Check(await visitors.Scan().CountAsync() == 2, "CountAsync reads the count");

commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"v-1"},"visits":{"N":"1"}}]}"""));
commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"v-2"},"visits":{"N":"2"}}]}"""));
var scanned = 0;
await foreach (var visitor in visitors.Scan().Parallel(2).ItemsAsync())
{
    scanned += visitor.Visits;
}

Check(scanned == 3, "a parallel scan reads every segment");

commandHandler.Enqueue(StubResponse.Json("""{"Table":{"TableName":"Users","TableStatus":"ACTIVE"}}"""));
Check((await users.WaitUntilActiveAsync(TimeSpan.FromSeconds(1))).TableStatus == TableStatus.Active, "WaitUntilActiveAsync returns the description");

// Mixed reads: a document that is generic over its types, read through static abstract members, with a generated type beside one written by hand.
const string MixedItems = """{"Items":[{"pk":{"S":"v-1"},"visits":{"N":"1"},"_type":{"S":"VISITOR"}},{"_type":{"S":"BASKET"},"pk":{"S":"b-1"},"Priority":{"S":"High"}}],"Count":2,"ScannedCount":3}""";
commandHandler.Enqueue(StubResponse.Json(MixedItems));
var mixedSeen = new List<string>();
await foreach (var item in users.Query("v").Of<Visitor, Basket>().ItemsAsync())
{
    mixedSeen.Add(item.Match(static visitor => visitor.Id, static basket => basket.Id));
}

Check(mixedSeen.Count == 2 && mixedSeen[0] == "v-1" && mixedSeen[1] == "b-1", "a mixed query reads each item as its type");
Check(commandHandler.Requests[^1].BodyText.Contains("#n1 IN (:v1, :v2)", StringComparison.Ordinal), "a mixed query filters on the discriminators");

commandHandler.Enqueue(StubResponse.Json(MixedItems));
var mixedPage = await users.Scan().Of<Basket, Visitor>().PageAsync();
Check(mixedPage.ScannedCount == 3 && mixedPage.Items[0].Is(out Visitor? mixedVisitor) && mixedVisitor.Visits == 1, "a mixed scan reads a page");

// A fan-out: tasks that read at once, a heap, a token that holds a key per partition, and numbers that merge by value.
using var fanOutHandler = new StubHandler(request => StubResponse.Json(request.BodyText.Contains("ExclusiveStartKey", StringComparison.Ordinal)
    ? """{"Items":[]}"""
    : """{"Items":[{"pk":{"S":"a"},"sk":{"N":"-1.5"}},{"pk":{"S":"a"},"sk":{"N":"10"}}],"Count":2,"ScannedCount":2}"""));
using var fanOutClient = new DynamoDbClient(new DynamoDbClientOptions { Region = AwsRegion.FromName("eu-central-2"), Credentials = credentials, HttpHandler = fanOutHandler });
var shards = fanOutClient.Table("Shards", "pk", "sk");
AttributeValue[] partitions = ["a", "b", "c"];
var merged = new List<string>();
await foreach (var item in shards.Query(partitions).Filter(Attr("x").NotExists()).MaxConcurrency(2).ItemsAsync())
{
    merged.Add(item["sk"].AsNumber());
}

Check(string.Join(',', merged) == "-1.5,-1.5,-1.5,10,10,10", "a fan-out merges by value");
var firstShards = await shards.Query(partitions).PageAsync(1);
var nextShards = await shards.Query(partitions).PageAsync(10, ContinuationToken.Parse(firstShards.Next.ToString()));
Check(firstShards.Count == 1 && nextShards.Count == 4, "a fan-out continues through its token");
var stringShards = await shards.Query(["a", "b"]).MaxAttempts(1).ReturnCapacity().PageAsync(1);
Check(stringShards.Count == 1 && stringShards.ConsumedCapacity is null && fanOutHandler.Requests[^1].BodyText.Contains("\"ReturnConsumedCapacity\":\"TOTAL\"", StringComparison.Ordinal), "a fan-out takes string keys and the options of a call");
Check(await shards.Query(partitions, SortKey.Ge(1)).Filter($"{Attr("x")} > {0}").CountAsync() == 6, "a fan-out counts");

// Batches and transactions: generic code over static abstract members and extension methods over ref structs, with what comes back unprocessed and cancelled.
commandHandler.Enqueue(StubResponse.Json("""{"UnprocessedItems":{"Users":[{"PutRequest":{"Item":{"pk":{"S":"v-2"}}}}]}}"""));
commandHandler.Enqueue(StubResponse.Json("{}"));
var written = await visitors.BatchWrite().Put(new Visitor("v-1", 1)).Put(new Visitor("v-2", 2)).Delete(new Visitor.Key("v-3")).Put(users, new Item { ["pk"] = "u-1" }).SendAsync();
Check(written.Written == 4 && written.Requests == 2, "BatchWrite sends what comes back unprocessed");

commandHandler.Enqueue(StubResponse.Json("""{"Responses":{"Users":[{"pk":{"S":"v-1"},"visits":{"N":"1"}}]},"UnprocessedKeys":{"Users":{"Keys":[{"pk":{"S":"v-2"}}]}}}"""));
commandHandler.Enqueue(StubResponse.Json("""{"Responses":{"Users":[{"pk":{"S":"v-2"},"visits":{"N":"2"}}]}}"""));
var found = await visitors.BatchGet().Key(new Visitor.Key("v-1")).Keys([new Visitor.Key("v-2")]).Consistent().SendAsync();
Check(found.Items.Count == 2 && found.Items.Contains(new Visitor("v-2", 2)), "BatchGet reads the entities of every round");

commandHandler.DefaultResponse = StubResponse.Json("""{"UnprocessedItems":{"Users":[{"DeleteRequest":{"Key":{"pk":{"S":"v-1"}}}}]}}""");
try
{
    await users.BatchWrite().Delete("v-1").MaxStalledRounds(1).SendAsync();
    Check(false, "a batch without progress throws");
}
catch (BatchIncompleteException exception)
{
    Check(exception.UnprocessedItems["Users"].Single().DeleteRequest?.Key["pk"].AsString() == "v-1", "BatchIncompleteException parses what is left");
}

commandHandler.DefaultResponse = null;
commandHandler.Enqueue(StubResponse.Json("""{"Responses":[{"Item":{"pk":{"S":"v-1"},"visits":{"N":"3"}}},{}]}"""));
var snapshot = await commandClient.TransactGet().Get(visitors, new Visitor.Key("v-1"), out var visitorSlot).Get(users, "u-1", out var missingSlot).SendAsync();
Check(snapshot.Read(visitorSlot).Item == new Visitor("v-1", 3) && !snapshot.Read(missingSlot).Found, "TransactGet reads a slot on demand");

commandHandler.Enqueue(StubResponse.Json("{}"));
await commandClient.TransactWrite()
    .Put(visitors, new Visitor("v-1", 1)).IfNotExists()
    .Update(users, "u-1").Add("visits", 1).If($"{Attr("visits")} < {100}").ReturnOldOnFailure()
    .Delete(visitors, new Visitor.Key("v-2"))
    .Check(users, "u-2").IfExists()
    .Token("token-1")
    .SendAsync();
Check(commandHandler.Requests[^1].BodyText.Contains("\"ClientRequestToken\":\"token-1\"", StringComparison.Ordinal), "TransactWrite carries the token");

commandHandler.Enqueue(new StubResponse
{
    StatusCode = System.Net.HttpStatusCode.BadRequest,
    Body = """{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","Message":"Cancelled","CancellationReasons":[{"Code":"None"},{"Code":"ConditionalCheckFailed","Item":{"pk":{"S":"taken"}}}]}""",
});
var taken = default(TransactMarker);
try
{
    await commandClient.TransactWrite().Delete(users, "u-1").Unique(users, new Item { ["pk"] = "taken" }, out taken).ReturnOldOnFailure().SendAsync();
    Check(false, "a cancelled transaction throws");
}
catch (TransactionCanceledException exception) when (exception.ReasonFor(taken) == TransactionFailure.ConditionalCheckFailed)
{
    Check(exception.ItemFor(taken)?.GetString("pk") == "taken" && exception.Failures.Single().Index == 1, "a marker finds its action in a cancelled transaction");
}

var failedMisuse = false;
try
{
    SendTwice(visitors);
}
catch (InvalidOperationException)
{
    failedMisuse = true;
}

Check(failedMisuse, "a command that is used twice throws");

// A generated entity: closed generic instantiations in generated source, with no reflection for AOT to trip over.
var baskets = users.Of<Basket>();
var basket = new Basket
{
    Id = "b-1",
    Priority = Priority.High,
    Lines = [new BasketLine("box") { Parts = [new BasketLine("lid")] }],
    Tags = ["gift"],
    Seen = [Priority.Low],
    Counts = new Dictionary<string, int> { ["views"] = 2 },
    Weight = new Weight(1500),
};
const string StoredBasket = """{"_type":{"S":"BASKET"},"pk":{"S":"b-1"},"Priority":{"S":"High"},"Lines":{"L":[{"M":{"Sku":{"S":"box"},"Parts":{"L":[{"M":{"Sku":{"S":"lid"},"Parts":{"L":[]}}}]}}}]},"Tags":{"SS":["gift"]},"Seen":{"SS":["Low"]},"Counts":{"M":{"views":{"N":"2"}}},"Weight":{"S":"1500g"}}""";

commandHandler.Enqueue(StubResponse.Json("{}"));
await baskets.Put(basket).SendAsync();
Check(commandHandler.Requests[^1].BodyText == "{\"TableName\":\"Users\",\"Item\":" + StoredBasket + "}", "Put writes the generated attributes");

commandHandler.Enqueue(StubResponse.Json("{\"Item\":" + StoredBasket + "}"));
var gotBasket = (await baskets.Get(basket).SendAsync()).Item;
Check(commandHandler.Requests[^1].BodyText == """{"TableName":"Users","Key":{"pk":{"S":"b-1"}}}""", "Get takes the generated key");
Check(
    gotBasket is { Priority: Priority.High, Weight.Grams: 1500 } && gotBasket.Lines[0].Parts[0].Sku == "lid" && gotBasket.Tags.Contains("gift") && gotBasket.Seen.Contains(Priority.Low) && gotBasket.Counts["views"] == 2,
    "Get reads the generated attributes");
Check(Item.From(basket).To<Basket>().Weight == basket.Weight, "an entity round trips through an Item");

commandHandler.Enqueue(StubResponse.Json("""{"Item":{"_type":{"S":"OTHER"},"pk":{"S":"b-1"},"Priority":{"S":"High"}}}"""));
try
{
    await baskets.Get(basket).SendAsync();
    Check(false, "an item of another type throws");
}
catch (DynamoDbMappingException exception)
{
    Check(exception.AttributeName == "_type", "the failure names the discriminator");
}

// Every other shape the generator can produce: key builders, both directions of the attributes, and the typed commands over handles and generated keys.
ShapeChecks.CheckKeys(Check);
ShapeChecks.CheckShapes(Check);
await ShapeChecks.CheckCommandsAsync(commandHandler, commandClient, Check);

// A version and key templates: a second generated write path, and generated delegates that format keys from boxed values.
var vouchers = users.Of<Voucher>();

commandHandler.Enqueue(StubResponse.Json("{}"));
var putVoucher = await vouchers.Put(new Voucher { Id = "v-1", Revision = 2 }).SendAsync();
Check(putVoucher.Version == 3 && commandHandler.Requests[^1].BodyText.Contains("\"Revision\":{\"N\":\"3\"}", StringComparison.Ordinal), "Put stores the next version");

commandHandler.Enqueue(StubResponse.Json("{}"));
var importedVoucher = await vouchers.Put(new Voucher { Id = "v-1", Revision = 2 }).IgnoreVersion().SendAsync();
Check(
    importedVoucher.Version == 2 && commandHandler.Requests[^1].BodyText.Contains("\"Revision\":{\"N\":\"2\"}", StringComparison.Ordinal) && !commandHandler.Requests[^1].BodyText.Contains("ConditionExpression", StringComparison.Ordinal),
    "IgnoreVersion writes the entity as it is, through a delegate per type");

commandHandler.Enqueue(StubResponse.Json("{}"));
var updatedVoucher = await vouchers.Update(Voucher.Keys.Of("v-1")).Set(Voucher.Attrs.Rank, 7).Set(Voucher.Attrs.Campaign, "x").Remove(Voucher.Attrs.Owner).IfVersion(3).SendAsync();
Check(
    updatedVoucher.Version == 4 && commandHandler.Requests[^1].BodyText.Contains("{\"S\":\"007-x\"}", StringComparison.Ordinal) && commandHandler.Requests[^1].BodyText.Contains("byownerpk", StringComparison.Ordinal),
    "Update writes the derived key and removes the sparse one");

var missingVersion = false;
try
{
    SendWithoutAVersion(vouchers);
}
catch (InvalidOperationException)
{
    missingVersion = true;
}

Check(missingVersion, "an update that says nothing about the version throws");

commandHandler.Enqueue(StubResponse.Json("{}"));
await commandClient.TransactWrite()
    .Put(vouchers, new Voucher { Id = "v-2" })
    .Delete(vouchers, Voucher.Keys.Of("v-1")).IfVersion(4)
    .Unique(vouchers, Voucher.Keys.Of("v-3"), out _)
    .SendAsync();
Check(commandHandler.Requests[^1].BodyText.Contains("attribute_not_exists", StringComparison.Ordinal), "a transaction carries the versions and the marker");

commandHandler.Enqueue(StubResponse.Json("{}"));
var batchedVouchers = await vouchers.BatchWrite().Put(new Voucher { Id = "v-4" }).IgnoreVersion().SendAsync();
Check(batchedVouchers.Written == 1, "a batch takes a version it ignores");

// What a sample asks for: the item alone, the names of the indexes, a batch of sequences, the version of an entity and a transaction held across a loop.
commandHandler.Enqueue(StubResponse.Json("""{"Item":{"pk":{"S":"VOUCHER#v-1"},"_type":{"S":"VOUCHER"},"Id":{"S":"v-1"},"Revision":{"N":"5"}}}"""));
var readVoucher = await vouchers.Get(Voucher.Keys.Of("v-1")).ItemAsync();
Check(readVoucher?.Revision == 5, "ItemAsync reads the entity");
Check(vouchers.Index(Voucher.Indexes.ByOwner).PartitionKeyName == "byownerpk", "a generated constant finds its index");

commandHandler.Enqueue(StubResponse.Json("{}"));
await vouchers.Delete(readVoucher!).SendAsync();
Check(commandHandler.Requests[^1].BodyText.Contains("{\":v0\":{\"N\":\"5\"}}", StringComparison.Ordinal), "a delete of an entity checks its version");

string[] voucherIds = ["v-5", "v-6"];
commandHandler.Enqueue(StubResponse.Json("{}"));
var sequences = await vouchers.BatchWrite().PutAll(voucherIds.Select(static id => new Voucher { Id = id })).DeleteAll(voucherIds.Select(Voucher.Keys.Of)).IgnoreVersion().SendAsync();
Check(sequences.Written == 4, "a batch takes sequences of items and keys");

var looped = commandClient.TransactWrite();
foreach (var id in voucherIds)
{
    looped = looped.Put(vouchers, new Voucher { Id = id }).IfNotExists();
}

commandHandler.Enqueue(StubResponse.Json("{}"));
await looped.SendAsync();
Check(commandHandler.Requests[^1].BodyText.Contains("VOUCHER#v-6", StringComparison.Ordinal), "a transaction goes on from a command that holds a step");

// The answer of STS is XML, the one format here that does not go through System.Text.Json, and XmlReader is a large part of the BCL to keep through trimming.
const string stsResponse = """
    <AssumeRoleWithWebIdentityResponse xmlns="https://sts.amazonaws.com/doc/2011-06-15/">
      <AssumeRoleWithWebIdentityResult>
        <AssumedRoleUser><Arn>arn:aws:sts::123456789012:assumed-role/orders/s</Arn></AssumedRoleUser>
        <Credentials>
          <SessionToken>IQoJb3JpZ2luX2VjEEM/Example==</SessionToken>
          <SecretAccessKey>wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY</SecretAccessKey>
          <Expiration>2099-01-01T00:00:00Z</Expiration>
          <AccessKeyId>ASIAIOSFODNN7EXAMPLE</AccessKeyId>
        </Credentials>
      </AssumeRoleWithWebIdentityResult>
    </AssumeRoleWithWebIdentityResponse>
    """;
using var stsHandler = new StubHandler(_ => new StubResponse { Body = stsResponse });
var webIdentity = new WebIdentityCredentialsProvider(
    name => name switch
    {
        "AWS_WEB_IDENTITY_TOKEN_FILE" => "/token",
        "AWS_ROLE_ARN" => "arn:aws:iam::123456789012:role/orders",
        _ => null,
    },
    _ => "the-token",
    stsHandler,
    TimeProvider.System,
    NullLogger.Instance,
    AwsRegion.FromName("eu-central-2"));
var assumed = await webIdentity.GetCredentialsAsync(CancellationToken.None);
Check(assumed.AccessKeyId == "ASIAIOSFODNN7EXAMPLE" && assumed.SessionToken == "IQoJb3JpZ2luX2VjEEM/Example==" && assumed.Expiration?.Year == 2099, "web identity reads the XML of STS");
Check(stsHandler.Requests[^1].BodyText.Contains("WebIdentityToken=the-token", StringComparison.Ordinal), "web identity posts the token as a form");
Check(StsDocument.TryParse("<ErrorResponse><Error><Code>AccessDenied</Code></Error></ErrorResponse>"u8.ToArray(), out var stsError) && stsError.ErrorCode == "AccessDenied", "web identity reads an error of STS");
Check(!StsDocument.TryParse("<!DOCTYPE a><a/>"u8.ToArray(), out _), "web identity refuses a document type definition");

// [OmitEmpty]: a handle whose converter wraps another through a type parameter, and one item from a query, over a page that a filter left empty.
var memos = users.Of<Memo>();

commandHandler.Enqueue(StubResponse.Json("{}"));
await memos.Put(new Memo { Id = "m-1", Text = string.Empty }).SendAsync();
Check(!commandHandler.Requests[^1].BodyText.Contains("Text", StringComparison.Ordinal), "a put leaves an empty string out");

commandHandler.Enqueue(StubResponse.Json("{}"));
await memos.Update(Memo.Keys.Of("m-1")).Set(Memo.Attrs.Text, string.Empty).SendAsync();
Check(commandHandler.Requests[^1].BodyText.Contains("REMOVE", StringComparison.Ordinal), "a set of an empty string by handle is a remove");

commandHandler.Enqueue(StubResponse.Json("""{"Attributes":{"Text":{"S":"x"}}}"""));
var returnedMemo = await memos.Update(Memo.Keys.Of("m-1")).SetIfNotNull(Memo.Attrs.Text, "x").Return(Memo.Attrs.Text).SendAsync();
Check(returnedMemo == "x", "a handle with OmitEmpty writes and reads through the converter it wraps");

commandHandler.Enqueue(StubResponse.Json("""{"Items":[],"Count":0,"ScannedCount":1,"LastEvaluatedKey":{"pk":{"S":"MEMO#m-1"}}}"""));
commandHandler.Enqueue(StubResponse.Json("""{"Items":[{"pk":{"S":"MEMO#m-1"},"_type":{"S":"MEMO"},"Id":{"S":"m-1"},"Text":{"S":""}}],"Count":1,"ScannedCount":1}"""));
var firstMemo = await memos.Query(Memo.Keys.Of("m-1")).Filter(Memo.Attrs.Text.Eq(string.Empty)).ItemAsync();
Check(firstMemo?.Text?.Length == 0, "ItemAsync follows a page a filter left empty, and a stored empty string reads as it is");

foreach (var failure in failures)
{
    await Console.Error.WriteLineAsync($"FAILED: {failure}");
}

return failures.Count == 0 ? 0 : 1;

static void SendWithoutAVersion(Table<Voucher> table) => _ = table.Update(Voucher.Keys.Of("v-1")).Set("Rank", 1).ToRequestJson();

static void SendTwice(Table<Visitor> table)
{
    var command = table.Update(new Visitor.Key("v-1")).Set("visits", 1);
    _ = command.ToRequestJson();
    var again = command;
    _ = again.ToRequestJson();
}

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures.Add(what);
    }
}
