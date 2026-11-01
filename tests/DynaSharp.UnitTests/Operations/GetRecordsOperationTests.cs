using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class GetRecordsOperationTests
{
    private const string Iterator = "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252|1|AAAAAAAAAAEvJp6D+zaQ...  <remaining characters omitted> ...";

    private static readonly Dictionary<string, GetRecordsRequest> BotocoreRequests = new()
    {
        ["RetrieveStreamRecordsFromApiReference"] = new() { ShardIterator = Iterator },
        ["WithLimit"] = new() { ShardIterator = Iterator, Limit = 1000 },
        ["LimitIntMax"] = new() { ShardIterator = Iterator, Limit = int.MaxValue },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("GetRecords");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("GetRecords");

    public static IEnumerable<(OperationType Value, string Wire)> OperationTypes()
    {
        yield return (OperationType.Insert, "INSERT");
        yield return (OperationType.Modify, "MODIFY");
        yield return (OperationType.Remove, "REMOVE");
        yield return (OperationType.Unknown, "TRUNCATE");
    }

    public static IEnumerable<(StreamViewType Value, string Wire)> StreamViewTypes()
    {
        yield return (StreamViewType.NewImage, "NEW_IMAGE");
        yield return (StreamViewType.OldImage, "OLD_IMAGE");
        yield return (StreamViewType.NewAndOldImages, "NEW_AND_OLD_IMAGES");
        yield return (StreamViewType.KeysOnly, "KEYS_ONLY");
        yield return (StreamViewType.Unspecified, "SOMETHING_NEW");
    }

    [Test]
    public async Task Name_Always_IsGetRecords()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = GetRecordsOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("GetRecords");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsNull()
    {
        // Arrange
        var request = new GetRecordsRequest { ShardIterator = Iterator };

        // Act
        var tableName = GetRecordsOperation.GetTableName(request);

        // Assert
        await Assert.That(tableName).IsNull();
    }

    [Test]
    public async Task BotocoreRequests_Always_CoverEveryVector()
    {
        // Arrange (the vectors come from the generator, the requests from this class)

        // Act
        var missing = RequestVectorNames().Except(BotocoreRequests.Keys).Concat(BotocoreRequests.Keys.Except(RequestVectorNames()));

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(RequestVectorNames))]
    public async Task WriteRequest_BotocoreVector_WritesTheJsonThatBotocoreWrites(string name)
    {
        // Arrange
        var vector = OperationVectors.Request("GetRecords", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_NoLimit_WritesOnlyTheIterator()
    {
        // Arrange
        var request = new GetRecordsRequest { ShardIterator = "it" };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"ShardIterator":"it"}""");
    }

    [Test]
    [Arguments(int.MinValue)]
    [Arguments(0)]
    [Arguments(1)]
    public async Task WriteRequest_Limit_IsPassedThrough(int limit)
    {
        // Arrange
        var request = new GetRecordsRequest { ShardIterator = "it", Limit = limit };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo($$"""{"ShardIterator":"it","Limit":{{limit}}}""");
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithEmptyListIfAbsent(vector.Parsed, "Records"), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheThreeRecordsInOrder()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RetrieveStreamRecordsFromApiReference");

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(response.Records.Select(record => record.EventName)).IsEquivalentTo([OperationType.Insert, OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheKeysAndTheSequenceNumber()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RetrieveStreamRecordsFromApiReference");

        // Act
        var change = Read(vector.Wire).Records[0].DynamoDb;

        // Assert
        await Assert.That((change?.Keys?["ForumName"].AsString(), change?.SequenceNumber, change?.SizeBytes, change?.StreamViewType)).IsEqualTo(("DynamoDB", "300000000000000499659", (long?)41, (StreamViewType?)StreamViewType.KeysOnly));
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheCreationTimeAndTheMetadata()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RetrieveStreamRecordsFromApiReference");

        // Act
        var record = Read(vector.Wire).Records[0];

        // Assert
        await Assert.That((record.DynamoDb?.ApproximateCreationDateTime, record.EventId, record.EventVersion, record.EventSource, record.Region))
            .IsEqualTo(((DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(1464804310), "e2fd9c34eff2d779b297b26f5fef4206", "1.0", "aws:dynamodb", "us-west-2"));
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_HasANextShardIterator()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RetrieveStreamRecordsFromApiReference");

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(response.NextShardIterator).StartsWith("arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/");
    }

    [Test]
    public async Task ReadResponse_NoRecordsButANextShardIterator_IsAnEmptyListAndTheIterator()
    {
        // Arrange
        var json = $$"""{"Records":[],"NextShardIterator":"{{Iterator}}"}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.Records.Count, response.NextShardIterator)).IsEqualTo((0, Iterator));
    }

    [Test]
    public async Task ReadResponse_ClosedShard_HasNoNextShardIterator()
    {
        // Arrange
        const string json = """{"Records":[]}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.NextShardIterator).IsNull();
    }

    [Test]
    public async Task ReadResponse_NullNextShardIterator_IsNull()
    {
        // Arrange
        const string json = """{"Records":[],"NextShardIterator":null}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.NextShardIterator).IsNull();
    }

    [Test]
    public async Task ReadResponse_RemovedByTimeToLive_ReturnsTheServiceIdentity()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RemovedByTimeToLive");

        // Act
        var identity = Read(vector.Wire).Records.Single().UserIdentity;

        // Assert
        await Assert.That((identity?.PrincipalId, identity?.Type)).IsEqualTo(("dynamodb.amazonaws.com", "Service"));
    }

    [Test]
    public async Task ReadResponse_RecordWithoutIdentity_HasNone()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "RetrieveStreamRecordsFromApiReference");

        // Act
        var record = Read(vector.Wire).Records[0];

        // Assert
        await Assert.That(record.UserIdentity).IsNull();
    }

    [Test]
    public async Task ReadResponse_NewAndOldImages_ReturnsBoth()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "StreamViewNewAndOldImages");

        // Act
        var change = Read(vector.Wire).Records[1].DynamoDb;

        // Assert
        await Assert.That((change?.NewImage?["Views"].AsNumber<int>(), change?.OldImage?["Views"].AsNumber<int>())).IsEqualTo(((int?)6, (int?)5));
    }

    [Test]
    public async Task ReadResponse_KeysOnly_HasNoImages()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "StreamViewKeysOnly");

        // Act
        var change = Read(vector.Wire).Records[1].DynamoDb;

        // Assert
        await Assert.That((change?.NewImage, change?.OldImage)).IsEqualTo(((IReadOnlyDictionary<string, AttributeValue>?)null, (IReadOnlyDictionary<string, AttributeValue>?)null));
    }

    [Test]
    [MethodDataSource(nameof(OperationTypes))]
    public async Task ReadResponse_EventName_MapsToTheEnum((OperationType Value, string Wire) type)
    {
        // Arrange
        var json = $$$"""{"Records":[{"eventName":"{{{type.Wire}}}"}]}""";

        // Act
        var record = Read(json).Records.Single();

        // Assert
        await Assert.That(record.EventName).IsEqualTo(type.Value);
    }

    [Test]
    [MethodDataSource(nameof(StreamViewTypes))]
    public async Task ReadResponse_StreamViewType_MapsToTheEnum((StreamViewType Value, string Wire) type)
    {
        // Arrange
        var json = $$$"""{"Records":[{"dynamodb":{"StreamViewType":"{{{type.Wire}}}"}}]}""";

        // Act
        var change = Read(json).Records.Single().DynamoDb;

        // Assert
        await Assert.That(change?.StreamViewType).IsEqualTo(type.Value);
    }

    [Test]
    public async Task ReadResponse_EmptyRecord_HasOnlyDefaults()
    {
        // Arrange
        const string json = """{"Records":[{}]}""";

        // Act
        var record = Read(json).Records.Single();

        // Assert
        await Assert.That((record.EventId, record.EventName, record.EventVersion, record.EventSource, record.Region, record.DynamoDb, record.UserIdentity))
            .IsEqualTo(((string?)null, OperationType.Unknown, (string?)null, (string?)null, (string?)null, (StreamRecord?)null, (RecordIdentity?)null));
    }

    [Test]
    public async Task ReadResponse_EmptyStreamRecordAndIdentity_HaveOnlyDefaults()
    {
        // Arrange
        const string json = """{"Records":[{"dynamodb":{},"userIdentity":{}}]}""";

        // Act
        var record = Read(json).Records.Single();

        // Assert
        await Assert.That((record.DynamoDb?.Keys, record.DynamoDb?.SequenceNumber, record.DynamoDb?.SizeBytes, record.DynamoDb?.ApproximateCreationDateTime, record.UserIdentity?.PrincipalId, record.UserIdentity?.Type))
            .IsEqualTo(((IReadOnlyDictionary<string, AttributeValue>?)null, (string?)null, (long?)null, (DateTimeOffset?)null, (string?)null, (string?)null));
    }

    [Test]
    public async Task ReadResponse_UnknownPropertiesEverywhere_AreSkipped()
    {
        // Arrange
        const string json = """
            {"A":[1],"Records":[{"B":{"x":[]},"eventID":"e","dynamodb":{"C":1,"SequenceNumber":"s"},"userIdentity":{"D":[2],"Type":"Service"}}],"E":null}
            """;

        // Act
        var record = Read(json).Records.Single();

        // Assert
        await Assert.That((record.EventId, record.DynamoDb?.SequenceNumber, record.UserIdentity?.Type)).IsEqualTo(("e", "s", "Service"));
    }

    [Test]
    public async Task ReadResponse_CreationTimeOutsideTheDateTimeRange_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Records":[{"dynamodb":{"ApproximateCreationDateTime":-1E300}}]}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("outside the range", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_RecordsThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"Records":{}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_Records_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"Records":[{"eventID":"e"}]}""";

        // Act
        var records = Read(json).Records;

        // Assert
        await Assert.That(records is IList<ChangeRecord> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task ReadResponse_ManyRecords_ReturnsEveryRecordInOrder()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "ManyRecords");

        // Act
        var records = Read(vector.Wire).Records;

        // Assert
        await Assert.That(records.Select(record => record.EventId!)).IsEquivalentTo(Enumerable.Range(0, 40).Select(index => $"id-{index}"));
    }

    [Test]
    public async Task ReadResponse_ManyRecords_ReturnsEveryRentedArrayToThePool()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "ManyRecords");
        var pool = new CountingArrayPool<ChangeRecord>();

        // Act
        _ = TestJson.Read(vector.Wire, (ref reader) => GetRecordsOperation.ReadResponse(ref reader, pool));

        // Assert
        await Assert.That((pool.Outstanding, pool.Returned, pool.RentedLengths.Count)).IsEqualTo((0, 3, 3));
    }

    [Test]
    public async Task ReadResponse_ManyRecords_ClearsTheReferencesBeforeReturningTheArrays()
    {
        // Arrange
        var vector = OperationVectors.Response("GetRecords", "ManyRecords");
        var pool = new CountingArrayPool<ChangeRecord>();

        // Act
        _ = TestJson.Read(vector.Wire, (ref reader) => GetRecordsOperation.ReadResponse(ref reader, pool));

        // Assert
        await Assert.That(pool.ReturnedUncleared).IsFalse();
    }

    [Test]
    public async Task ReadResponse_RecordThatFailsToParse_ReturnsTheArrayToThePool()
    {
        // Arrange
        const string json = """{"Records":[{"eventID":"e"},{"dynamodb":{"SizeBytes":"large"}}]}""";
        var pool = new CountingArrayPool<ChangeRecord>();

        // Act
        var act = () => TestJson.Read(json, (ref reader) => GetRecordsOperation.ReadResponse(ref reader, pool));

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(pool.Outstanding).IsEqualTo(0);
    }

    [Test]
    public async Task SetActivityTags_NoLimit_SetsNoTag()
    {
        // Arrange
        var request = new GetRecordsRequest { ShardIterator = Iterator };
        using var activity = new Activity("test");

        // Act
        GetRecordsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task SetActivityTags_Limit_SetsTheLimitTagAndNeverTheIterator()
    {
        // Arrange
        var request = new GetRecordsRequest { ShardIterator = Iterator, Limit = 500 };
        using var activity = new Activity("test");

        // Act
        GetRecordsOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.limit=500");
    }

    private static string Write(GetRecordsRequest request) => TestJson.Write(writer => GetRecordsOperation.WriteRequest(writer, request));

    private static GetRecordsResponse Read(string json) => TestJson.Read(json, static (ref reader) => GetRecordsOperation.ReadResponse(ref reader));
}
