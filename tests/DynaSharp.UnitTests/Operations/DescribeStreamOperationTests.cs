using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.UnitTests.Serialization;

namespace DynaSharp.UnitTests.Operations;

public sealed class DescribeStreamOperationTests
{
    private const string StreamArn = "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252";
    private const string ShardId = "shardId-00000001414576573621-f55eea83";

    private static readonly Dictionary<string, DescribeStreamRequest> BotocoreRequests = new()
    {
        ["Minimal"] = new() { StreamArn = StreamArn },
        ["ChildShardsFromApiReference"] = new()
        {
            StreamArn = StreamArn,
            ShardFilter = new() { Type = ShardFilterType.ChildShards, ShardId = "shardId-00000001741631711871-1f6a72cf" },
        },
        ["AllOptions"] = new()
        {
            StreamArn = StreamArn,
            Limit = 100,
            ExclusiveStartShardId = ShardId,
            ShardFilter = new() { Type = ShardFilterType.ChildShards, ShardId = ShardId },
        },
    };

    public static IEnumerable<string> RequestVectorNames() => OperationVectors.RequestNames("DescribeStream");

    public static IEnumerable<string> ResponseVectorNames() => OperationVectors.ResponseNames("DescribeStream");

    public static IEnumerable<(StreamStatus Value, string Wire)> StreamStatuses()
    {
        yield return (StreamStatus.Enabling, "ENABLING");
        yield return (StreamStatus.Enabled, "ENABLED");
        yield return (StreamStatus.Disabling, "DISABLING");
        yield return (StreamStatus.Disabled, "DISABLED");
        yield return (StreamStatus.Unknown, "PAUSED");
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
    public async Task Name_Always_IsDescribeStream()
    {
        // Arrange (nothing to arrange)

        // Act
        var name = DescribeStreamOperation.Name;

        // Assert
        await Assert.That(name).IsEqualTo("DescribeStream");
    }

    [Test]
    public async Task GetTableName_Request_ReturnsNull()
    {
        // Arrange
        var request = new DescribeStreamRequest { StreamArn = StreamArn };

        // Act
        var tableName = DescribeStreamOperation.GetTableName(request);

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
        var vector = OperationVectors.Request("DescribeStream", name);
        var request = BotocoreRequests[name];

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(JsonComparer.Differences(vector.Body, json)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task WriteRequest_OnlyTheArn_WritesOnlyTheArn()
    {
        // Arrange
        var request = new DescribeStreamRequest { StreamArn = "arn" };

        // Act
        var json = Write(request);

        // Assert
        await Assert.That(json).IsEqualTo("""{"StreamArn":"arn"}""");
    }

    [Test]
    [Arguments(ShardFilterType.Unknown)]
    [Arguments((ShardFilterType)99)]
    public async Task WriteRequest_ShardFilterTypeThatCannotBeSent_ThrowsArgumentOutOfRangeException(ShardFilterType type)
    {
        // Arrange
        var request = new DescribeStreamRequest { StreamArn = "arn", ShardFilter = new() { Type = type, ShardId = ShardId } };

        // Act
        var act = () => Write(request);

        // Assert
        await Assert.That(act).Throws<ArgumentOutOfRangeException>().WithMessageContaining("ShardFilterType", StringComparison.Ordinal);
    }

    [Test]
    [MethodDataSource(nameof(ResponseVectorNames))]
    public async Task ReadResponse_BotocoreVector_ReturnsWhatBotocoreParsed(string name)
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeStream", name);

        // Act
        var response = Read(vector.Wire);

        // Assert
        await Assert.That(JsonComparer.Differences(ResponseJson.WithStreamDescriptionDefaults(vector.Parsed), ResponseJson.Describe(response))).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_FindsTheOpenShard()
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeStream", "RetrieveInformationAboutAStreamFromApiReference");

        // Act
        var shards = Read(vector.Wire).StreamDescription!.Shards;

        // Assert
        await Assert.That(shards.Where(shard => shard.SequenceNumberRange?.EndingSequenceNumber is null).Select(shard => shard.ShardId!)).IsEquivalentTo(["shardId-00000001414608446368-3a1afbaf"]);
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_ReturnsTheCreationTime()
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeStream", "RetrieveInformationAboutAStreamFromApiReference");

        // Act
        var description = Read(vector.Wire).StreamDescription!;

        // Assert
        await Assert.That(description.CreationRequestDateTime).IsEqualTo(new DateTimeOffset(2015, 5, 20, 20, 51, 10, 252, TimeSpan.Zero));
    }

    [Test]
    public async Task ReadResponse_DocumentationExample_LinksChildToParent()
    {
        // Arrange
        var vector = OperationVectors.Response("DescribeStream", "RetrieveInformationAboutAStreamFromApiReference");

        // Act
        var shards = Read(vector.Wire).StreamDescription!.Shards;

        // Assert
        await Assert.That(shards[1].ParentShardId).IsEqualTo(shards[0].ShardId);
    }

    [Test]
    [MethodDataSource(nameof(StreamStatuses))]
    public async Task ReadResponse_StreamStatus_MapsToTheEnum((StreamStatus Value, string Wire) status)
    {
        // Arrange
        var json = $$$"""{"StreamDescription":{"StreamArn":"arn","StreamStatus":"{{{status.Wire}}}"}}""";

        // Act
        var description = Read(json).StreamDescription!;

        // Assert
        await Assert.That(description.StreamStatus).IsEqualTo(status.Value);
    }

    [Test]
    [MethodDataSource(nameof(StreamViewTypes))]
    public async Task ReadResponse_StreamViewType_MapsToTheEnum((StreamViewType Value, string Wire) type)
    {
        // Arrange
        var json = $$$"""{"StreamDescription":{"StreamArn":"arn","StreamViewType":"{{{type.Wire}}}"}}""";

        // Act
        var description = Read(json).StreamDescription!;

        // Assert
        await Assert.That(description.StreamViewType).IsEqualTo(type.Value);
    }

    [Test]
    public async Task ReadResponse_OnlyTheArn_HasTheDefaults()
    {
        // Arrange
        var json = $$$"""{"StreamDescription":{"StreamArn":"{{{StreamArn}}}"}}""";

        // Act
        var description = Read(json).StreamDescription!;

        // Assert
        await Assert.That((description.StreamStatus, description.StreamViewType, description.CreationRequestDateTime, description.TableName, description.KeySchema.Count, description.Shards.Count, description.LastEvaluatedShardId))
            .IsEqualTo((StreamStatus.Unknown, StreamViewType.Unspecified, (DateTimeOffset?)null, (string?)null, 0, 0, (string?)null));
    }

    [Test]
    public async Task ReadResponse_NoStreamDescription_HasNone()
    {
        // Arrange
        const string json = """{"Unknown":1}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That(response.StreamDescription).IsNull();
    }

    [Test]
    public async Task ReadResponse_DescriptionWithoutArn_HasNoArn()
    {
        // Arrange
        const string json = """{"StreamDescription":{"TableName":"Forum"}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.StreamDescription!.StreamArn, response.StreamDescription.TableName)).IsEqualTo(((string?)null, "Forum"));
    }

    [Test]
    public async Task ReadResponse_ShardWithoutId_HasNoId()
    {
        // Arrange
        const string json = """{"StreamDescription":{"StreamArn":"arn","Shards":[{"ParentShardId":"p"}]}}""";

        // Act
        var response = Read(json);

        // Assert
        await Assert.That((response.StreamDescription!.Shards.Single().ShardId, response.StreamDescription.Shards.Single().ParentShardId)).IsEqualTo(((string?)null, "p"));
    }

    [Test]
    public async Task ReadResponse_ShardsThatIsNotAnArray_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"StreamDescription":{"StreamArn":"arn","Shards":{}}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("JSON array", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_CreationTimeOutsideTheDateTimeRange_ThrowsJsonException()
    {
        // Arrange
        const string json = """{"StreamDescription":{"StreamArn":"arn","CreationRequestDateTime":1E300}}""";

        // Act
        var act = () => Read(json);

        // Assert
        await Assert.That(act).Throws<JsonException>().WithMessageContaining("outside the range", StringComparison.Ordinal);
    }

    [Test]
    public async Task ReadResponse_Shards_CannotBeChangedThroughACast()
    {
        // Arrange
        const string json = """{"StreamDescription":{"StreamArn":"arn","Shards":[{"ShardId":"s"}],"KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}]}}""";

        // Act
        var description = Read(json).StreamDescription!;

        // Assert
        await Assert.That(description.Shards is IList<Shard> { IsReadOnly: true } && description.KeySchema is IList<KeySchemaElement> { IsReadOnly: true }).IsTrue();
    }

    [Test]
    public async Task SetActivityTags_NoLimit_SetsNoTag()
    {
        // Arrange
        var request = new DescribeStreamRequest { StreamArn = StreamArn };
        using var activity = new Activity("test");

        // Act
        DescribeStreamOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(activity.TagObjects).IsEmpty();
    }

    [Test]
    public async Task SetActivityTags_Limit_SetsTheLimitTagAndNeverTheArn()
    {
        // Arrange
        var request = new DescribeStreamRequest { StreamArn = StreamArn, Limit = 10 };
        using var activity = new Activity("test");

        // Act
        DescribeStreamOperation.SetActivityTags(activity, request);

        // Assert
        await Assert.That(string.Join(',', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))).IsEqualTo("aws.dynamodb.limit=10");
    }

    private static string Write(DescribeStreamRequest request) => TestJson.Write(writer => DescribeStreamOperation.WriteRequest(writer, request));

    private static DescribeStreamResponse Read(string json) => TestJson.Read(json, static (ref reader) => DescribeStreamOperation.ReadResponse(ref reader));
}
