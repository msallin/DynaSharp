using System.Text.Json;
using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;
using DynaSharp.UnitTests.Operations;
using DynaSharp.UnitTests.Testing.Consumer;

namespace DynaSharp.UnitTests.Testing;

/// <summary>
/// The answers of DynamoDB Streams, read through the client. The bodies that are spelled out come from the service model of botocore, with the values of a stream of a table that has one key.
/// </summary>
public sealed class StubResponseStreamTests
{
    private const string Label = "2026-01-01T00:00:00.000";
    private const string Arn = "arn:aws:dynamodb:us-east-1:123456789012:table/Orders/stream/" + Label;

    private static readonly Subscriber Ada = new("ada", "ada@example.com", "pro");
    private static readonly Subscriber AdaOnFree = new("ada", "ada@example.com", "free");
    private static readonly Item AdaKey = new() { ["pk"] = "ada" };

    [Test]
    public async Task StreamDescription_Table_IsTheBodyOfADescribeStream()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.StreamDescription("Orders", StubResponse.Shard("shard-1"));

        // Assert
        await Assert.That(response.Body).IsEqualTo(
            $$$$$"""{"StreamDescription":{"StreamArn":"{{{{{Arn}}}}}","StreamLabel":"{{{{{Label}}}}}","StreamStatus":"ENABLED","StreamViewType":"NEW_AND_OLD_IMAGES","CreationRequestDateTime":1767225600,"TableName":"Orders","KeySchema":[{"AttributeName":"pk","KeyType":"HASH"}],"Shards":[{"ShardId":"shard-1","SequenceNumberRange":{"StartingSequenceNumber":"100000000000000000000"}}]}}""");
    }

    [Test]
    public async Task StreamDescription_Table_ReadsBackFromADescribeStream()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.StreamDescription("Orders", StubResponse.Shard("shard-1"), StubResponse.Shard("shard-2", parentShardId: "shard-1")));

        // Act
        var description = (await client.DescribeStreamAsync(TestClient.DescribeStream())).StreamDescription!;

        // Assert
        await Assert.That((description.StreamArn, description.TableName, description.StreamStatus, description.StreamViewType, description.Shards.Count, description.Shards[1].ParentShardId))
            .IsEqualTo((Arn, "Orders", StreamStatus.Enabled, StreamViewType.NewAndOldImages, 2, "shard-1"));
    }

    [Test]
    public async Task StreamDescription_NoShards_HasAnEmptyList()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = JsonElement.Parse(StubResponse.StreamDescription("Orders").Body).GetProperty("StreamDescription");

        // Assert
        await Assert.That(description.GetProperty("Shards").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    [Arguments(StreamStatus.Disabled, "DISABLED")]
    [Arguments(StreamStatus.Disabling, "DISABLING")]
    [Arguments(StreamStatus.Enabling, "ENABLING")]
    public async Task StreamDescription_TableAndStatus_SendsTheStatus(StreamStatus status, string expected)
    {
        // Arrange (nothing to arrange)

        // Act
        var description = JsonElement.Parse(StubResponse.StreamDescription("Orders", status, StubResponse.Shard("shard-1", endingSequenceNumber: "200")).Body).GetProperty("StreamDescription");

        // Assert
        await Assert.That((description.GetProperty("StreamStatus").GetString(), description.GetProperty("Shards").GetArrayLength())).IsEqualTo((expected, 1));
    }

    [Test]
    public async Task StreamDescription_UnknownStatus_SendsNone()
    {
        // Arrange (nothing to arrange)

        // Act
        var description = JsonElement.Parse(StubResponse.StreamDescription("Orders", StreamStatus.Unknown).Body).GetProperty("StreamDescription");

        // Assert
        await Assert.That(description.TryGetProperty("StreamStatus", out _)).IsFalse();
    }

    [Test]
    public async Task StreamDescription_DisabledStreamWhoseLastShardClosed_EndsTheReading()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.StreamDescription("Orders", StreamStatus.Disabled, StubResponse.Shard("shard-1", endingSequenceNumber: "200")));
        stub.Enqueue(StubResponse.ShardIterator("iterator-1"));
        stub.Enqueue(StubResponse.Records(null));
        stub.Enqueue(StubResponse.StreamDescription("Orders", StreamStatus.Disabled));
        var request = new ReadStreamRequest { StreamArn = Arn, ShardIteratorType = ShardIteratorType.TrimHorizon };

        // Act
        var records = await client.ReadStreamAsync(request).ToListAsync();

        // Assert
        await Assert.That(records.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StreamDescription_NullTable_Throws()
    {
        // Arrange
        string tableName = null!;

        // Act
        var act = () => StubResponse.StreamDescription(tableName);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    // A reader pages through the shards of a stream and stops when the status says that it ended.
    [Test]
    public async Task StreamDescription_Description_SendsWhatItHolds()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        var created = new DateTimeOffset(2026, 11, 2, 12, 0, 0, 125, TimeSpan.Zero);
        stub.Enqueue(StubResponse.StreamDescription(new StreamDescription
        {
            StreamArn = TestClient.StreamArn,
            StreamLabel = "label",
            StreamStatus = StreamStatus.Disabled,
            StreamViewType = StreamViewType.KeysOnly,
            CreationRequestDateTime = created,
            TableName = "Users",
            KeySchema = [new KeySchemaElement("id", KeyType.Hash), new KeySchemaElement("at", KeyType.Range)],
            LastEvaluatedShardId = "shard-1",
        }));

        // Act
        var description = (await client.DescribeStreamAsync(TestClient.DescribeStream())).StreamDescription!;

        // Assert
        await Assert.That((description.StreamArn, description.StreamLabel, description.StreamStatus, description.StreamViewType, description.CreationRequestDateTime, description.TableName, description.KeySchema[1].AttributeName, description.LastEvaluatedShardId))
            .IsEqualTo((TestClient.StreamArn, "label", StreamStatus.Disabled, StreamViewType.KeysOnly, created, "Users", "at", "shard-1"));
    }

    [Test]
    public async Task StreamDescription_EmptyDescription_SendsNoMemberItDoesNotHold()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.StreamDescription(new StreamDescription());

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"StreamDescription":{"KeySchema":[],"Shards":[]}}""");
    }

    [Test]
    public async Task StreamDescription_NullDescription_Throws()
    {
        // Arrange
        StreamDescription description = null!;

        // Act
        var act = () => StubResponse.StreamDescription(description);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    [Arguments(StreamStatus.Enabling, "ENABLING")]
    [Arguments(StreamStatus.Enabled, "ENABLED")]
    [Arguments(StreamStatus.Disabling, "DISABLING")]
    [Arguments(StreamStatus.Disabled, "DISABLED")]
    public async Task StreamDescription_Status_SendsTheNameOfTheServiceModel(StreamStatus status, string wireName)
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.StreamDescription(new StreamDescription { StreamStatus = status });

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("StreamDescription").GetProperty("StreamStatus").GetString()).IsEqualTo(wireName);
    }

    [Test]
    public async Task Shard_Id_IsOpenAndHasNoParent()
    {
        // Arrange (nothing to arrange)

        // Act
        var shard = StubResponse.Shard("shard-1");

        // Assert
        await Assert.That((shard.ShardId, shard.ParentShardId, shard.SequenceNumberRange!.StartingSequenceNumber, shard.SequenceNumberRange.EndingSequenceNumber))
            .IsEqualTo(("shard-1", null, "100000000000000000000", null));
    }

    // A closed shard has an end, which is what tells a reader to go on with its children.
    [Test]
    public async Task Shard_ParentAndEnd_ReadBackFromADescribeStream()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.StreamDescription("Orders", StubResponse.Shard("shard-2", "shard-1", "200")));

        // Act
        var shard = (await client.DescribeStreamAsync(TestClient.DescribeStream())).StreamDescription!.Shards.Single();

        // Assert
        await Assert.That((shard.ParentShardId, shard.SequenceNumberRange!.EndingSequenceNumber)).IsEqualTo(("shard-1", "200"));
    }

    [Test]
    public async Task Shard_NullId_Throws()
    {
        // Arrange
        string shardId = null!;

        // Act
        var act = () => StubResponse.Shard(shardId);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StreamDescription_ShardOfYourOwnWithoutARange_LeavesItOut()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.StreamDescription(new StreamDescription { Shards = [new Shard { ShardId = "a" }] });

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"StreamDescription":{"KeySchema":[],"Shards":[{"ShardId":"a"}]}}""");
    }

    [Test]
    public async Task ShardIterator_Iterator_IsTheBodyOfAGetShardIterator()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.ShardIterator("iterator-1");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"ShardIterator":"iterator-1"}""");
    }

    [Test]
    public async Task ShardIterator_Iterator_ReadsBackFromAGetShardIterator()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.ShardIterator(TestClient.ShardIterator));

        // Act
        var response = await client.GetShardIteratorAsync(TestClient.GetShardIterator());

        // Assert
        await Assert.That(response.ShardIterator).IsEqualTo(TestClient.ShardIterator);
    }

    [Test]
    public async Task ShardIterator_NullIterator_Throws()
    {
        // Arrange
        string iterator = null!;

        // Act
        var act = () => StubResponse.ShardIterator(iterator);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Records_Inserted_IsTheBodyOfAGetRecords()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Records("next", StubResponse.Inserted(Ada));

        // Assert
        await Assert.That(JsonComparer.Differences(
            """{"Records":[{"eventID":"1","eventName":"INSERT","eventVersion":"1.1","eventSource":"aws:dynamodb","awsRegion":"us-east-1","dynamodb":{"ApproximateCreationDateTime":1767225600,"Keys":{"pk":{"S":"ada"}},"NewImage":{"pk":{"S":"ada"},"_type":{"S":"SUBSCRIBER"},"email":{"S":"ada@example.com"},"plan":{"S":"pro"}},"SequenceNumber":"100000000000000000001","SizeBytes":100,"StreamViewType":"NEW_AND_OLD_IMAGES"}}],"NextShardIterator":"next"}""",
            response.Body)).IsEmpty();
    }

    [Test]
    public async Task Records_Inserted_ReadsBackAsTheEntity()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records("next", StubResponse.Inserted(Ada)));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        var change = response.Records.Single();
        await Assert.That((change.EventName, change.DynamoDb!.NewImage!.To<Subscriber>(), change.DynamoDb.OldImage)).IsEqualTo((OperationType.Insert, Ada, null));
    }

    [Test]
    public async Task Records_Modified_ReadsBackWithBothImages()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records("next", StubResponse.Modified(Ada, AdaOnFree)));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        var change = response.Records.Single();
        await Assert.That((change.EventName, change.DynamoDb!.OldImage!.To<Subscriber>(), change.DynamoDb.NewImage!.To<Subscriber>())).IsEqualTo((OperationType.Modify, Ada, AdaOnFree));
    }

    [Test]
    public async Task Records_Removed_ReadsBackWithTheOldImageAndTheKey()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records("next", StubResponse.Removed(Ada)));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        var change = response.Records.Single();
        await Assert.That((change.EventName, change.DynamoDb!.OldImage!.To<Subscriber>(), change.DynamoDb.NewImage, new Item(change.DynamoDb.Keys!))).IsEqualTo((OperationType.Remove, Ada, null, AdaKey));
    }

    [Test]
    public async Task Records_Change_TakesItemsAsImages()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        var before = new Item { ["pk"] = "ada", ["visits"] = 1 };
        var after = new Item { ["pk"] = "ada", ["visits"] = 2 };
        stub.Enqueue(StubResponse.Records("next", StubResponse.Change(OperationType.Modify, AdaKey, before, after)));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        var change = response.Records.Single().DynamoDb!;
        await Assert.That((change.OldImage, change.NewImage)).IsEqualTo((before, after));
    }

    [Test]
    public async Task Records_ChangeWithoutImages_SendsTheKeyAlone()
    {
        // Arrange (nothing to arrange)

        // Act
        var change = JsonElement.Parse(StubResponse.Records("next", StubResponse.Change(OperationType.Remove, AdaKey)).Body).GetProperty("Records")[0].GetProperty("dynamodb");

        // Assert
        await Assert.That((change.TryGetProperty("OldImage", out _), change.TryGetProperty("NewImage", out _), change.TryGetProperty("Keys", out _))).IsEqualTo((false, false, true));
    }

    [Test]
    [Arguments(OperationType.Insert, "INSERT")]
    [Arguments(OperationType.Modify, "MODIFY")]
    [Arguments(OperationType.Remove, "REMOVE")]
    public async Task Change_Operation_SendsTheNameOfTheServiceModel(OperationType operation, string wireName)
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Records("next", StubResponse.Change(operation, AdaKey));

        // Assert
        await Assert.That(JsonElement.Parse(response.Body).GetProperty("Records")[0].GetProperty("eventName").GetString()).IsEqualTo(wireName);
    }

    [Test]
    public async Task Change_NullKeys_Throws()
    {
        // Arrange
        Item keys = null!;

        // Act
        var act = () => StubResponse.Change(OperationType.Insert, keys);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Records_SeveralChanges_NumbersThemInTheOrderOfThePage()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records("next", StubResponse.Inserted(Ada), StubResponse.Removed(Ada)));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(response.Records.Select(change => (change.EventId!, change.DynamoDb!.SequenceNumber!)))
            .IsEquivalentTo([("1", "100000000000000000001"), ("2", "100000000000000000002")]);
    }

    // A reader that keeps a checkpoint sees different numbers on the second page only if the test gives them.
    [Test]
    public async Task Records_SequenceNumbersGiven_KeepsThem()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records(
            "next",
            StubResponse.Inserted(Ada, "501"),
            StubResponse.Modified(Ada, AdaOnFree, "502"),
            StubResponse.Removed(AdaOnFree, "503"),
            StubResponse.Change(OperationType.Remove, AdaKey, sequenceNumber: "504")));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(response.Records.Select(change => change.DynamoDb!.SequenceNumber!)).IsEquivalentTo(["501", "502", "503", "504"]);
    }

    // A delete by time to live is the one record that carries a user identity.
    [Test]
    public async Task Records_ChangeWithIdentity_SendsTheIdentity()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        var identity = new RecordIdentity { PrincipalId = "dynamodb.amazonaws.com", Type = "Service" };
        stub.Enqueue(StubResponse.Records("next", StubResponse.Removed(Ada) with { UserIdentity = identity }));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(response.Records.Single().UserIdentity).IsEqualTo(identity);
    }

    [Test]
    public async Task Records_RecordOfYourOwn_SendsWhatItHolds()
    {
        // Arrange
        var record = new ChangeRecord { EventId = "e-1", DynamoDb = new StreamRecord { SequenceNumber = "7" } };

        // Act
        var response = StubResponse.Records("next", record);

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Records":[{"eventID":"e-1","dynamodb":{"SequenceNumber":"7"}}],"NextShardIterator":"next"}""");
    }

    [Test]
    public async Task Records_RecordWithoutAStreamRecord_LeavesItOut()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Records("next", new ChangeRecord());

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Records":[{"eventID":"1"}],"NextShardIterator":"next"}""");
    }

    // An open shard answers with no records and an iterator, and the reader polls on.
    [Test]
    public async Task Records_NoChanges_IsAnEmptyPageOfAnOpenShard()
    {
        // Arrange (nothing to arrange)

        // Act
        var response = StubResponse.Records("next");

        // Assert
        await Assert.That(response.Body).IsEqualTo("""{"Records":[],"NextShardIterator":"next"}""");
    }

    // A shard that was closed and read to its end has no next iterator, which ends the reader.
    [Test]
    public async Task Records_NoIterator_EndsTheShard()
    {
        // Arrange
        using var stub = new StubHandler();
        using var client = StubClient.CreateStreams(stub);
        stub.Enqueue(StubResponse.Records(null));

        // Act
        var response = await client.GetRecordsAsync(TestClient.GetRecords());

        // Assert
        await Assert.That(response.NextShardIterator).IsNull();
    }
}
