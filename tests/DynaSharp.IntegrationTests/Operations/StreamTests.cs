using System.Numerics;
using DynaSharp.IntegrationTests.Backends;
using DynaSharp.IntegrationTests.Support;
using DynaSharp.Requests;
using DynaSharp.Streams;

namespace DynaSharp.IntegrationTests.Operations;

/// <summary>DynamoDbStreamsClient end to end: a table with a stream, writes, and the records read back.</summary>
[MethodDataSource(typeof(BackendSelection), nameof(BackendSelection.Selected))]
public sealed class StreamTests(Backend backend)
{
    [Test]
    public async Task ListStreams_TableWithStream_ReturnsItsStream()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewAndOldImages));

        // Act
        var response = await sandbox.Streams.ListStreamsAsync(new ListStreamsRequest { TableName = table });

        // Assert
        var stream = response.Streams.Single();
        await Assert.That(stream.TableName).IsEqualTo(table);
        await Assert.That(stream.StreamArn).Contains($"table/{table}/stream/", StringComparison.Ordinal);
        await Assert.That(string.IsNullOrEmpty(stream.StreamLabel)).IsFalse();
    }

    [Test]
    public async Task ListStreams_TableWithoutStream_ReturnsNoStreams()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync();

        // Act
        var response = await sandbox.Streams.ListStreamsAsync(new ListStreamsRequest { TableName = table });

        // Assert
        await Assert.That((response.Streams.Count, response.LastEvaluatedStreamArn)).IsEqualTo((0, null));
    }

    [Test]
    public async Task ListStreams_LatestStreamArnOfTheTable_IsTheListedStream()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        var described = (await sandbox.Client.DescribeTableAsync(new DescribeTableRequest { TableName = table })).Table!;

        // Act
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Assert
        await Assert.That(arn).IsEqualTo(described.LatestStreamArn);
    }

    [Test]
    public async Task DescribeStream_EnabledStream_ReportsStatusViewTypeAndOneShard()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewImage));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var description = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!;

        // Assert
        await Assert.That((description.StreamArn!, description.TableName, description.StreamStatus, description.StreamViewType))
            .IsEqualTo((arn, table, StreamStatus.Enabled, StreamViewType.NewImage));
        await Assert.That(description.Shards.Count).IsEqualTo(1);
        await Assert.That(string.IsNullOrEmpty(description.Shards[0].ShardId)).IsFalse();
        await Assert.That(description.Shards[0].ParentShardId).IsNull();
    }

    [Test]
    public async Task DescribeStream_NewStreamWithoutRecords_ShardHasAStartingSequenceNumber()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.StartingSequenceNumberOfAnEmptyShard);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewImage));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var description = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!;

        // Assert
        await Assert.That(string.IsNullOrEmpty(description.Shards[0].SequenceNumberRange!.StartingSequenceNumber)).IsFalse();
    }

    [Test]
    public async Task DescribeStream_EnabledStream_ReportsTheKeySchemaOfTheTable()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.StreamDescriptionKeySchema);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var description = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!;

        // Assert
        await Assert.That(description.KeySchema).IsEquivalentTo([new KeySchemaElement("pk", KeyType.Hash)]);
    }

    [Test]
    public async Task DescribeStream_OpenShard_HasNoEndingSequenceNumber()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OpenShardWithoutEndingSequenceNumber);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Key("a") });
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var description = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!;

        // Assert
        await Assert.That(description.Shards[0].SequenceNumberRange!.EndingSequenceNumber).IsNull();
    }

    [Test]
    public async Task GetRecords_InsertModifyRemove_DeliversThreeRecordsInOrderWithBothImages()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewAndOldImages));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var records = await StreamReading.ReadAsync(sandbox.Streams, arn, 3);

        // Assert
        await Assert.That(records.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Insert, OperationType.Modify, OperationType.Remove]);
        var insert = records[0].DynamoDb!;
        var modify = records[1].DynamoDb!;
        var remove = records[2].DynamoDb!;
        await Assert.That((insert.Keys!["pk"].AsString(), insert.NewImage!["v"].AsNumber(), insert.OldImage)).IsEqualTo(("a", "1", null));
        await Assert.That((modify.NewImage!["v"].AsNumber(), modify.OldImage!["v"].AsNumber())).IsEqualTo(("2", "1"));
        await Assert.That((remove.NewImage, remove.OldImage!["v"].AsNumber())).IsEqualTo((null, "2"));
        await Assert.That(records.Select(static record => record.DynamoDb!.StreamViewType).Distinct()).IsEquivalentTo([StreamViewType.NewAndOldImages]);
    }

    [Test]
    public async Task GetRecords_Records_CarryIdentifiersSourceAndAscendingSequenceNumbers()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var records = await StreamReading.ReadAsync(sandbox.Streams, arn, 3);

        // Assert
        await Assert.That(records.Select(static record => record.EventSource).Distinct().Single()).IsEqualTo("aws:dynamodb");
        await Assert.That(records.Select(static record => record.EventId).Distinct().Count()).IsEqualTo(3);
        await Assert.That(records.All(static record => !string.IsNullOrEmpty(record.EventVersion) && !string.IsNullOrEmpty(record.Region))).IsTrue();
        var sequence = records.Select(static record => BigInteger.Parse(record.DynamoDb!.SequenceNumber!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        await Assert.That(sequence.Zip(sequence.Skip(1), static (before, after) => after > before).All(static ascending => ascending)).IsTrue();
        await Assert.That(records.All(static record => record.DynamoDb!.SizeBytes > 0 && record.DynamoDb.ApproximateCreationDateTime is not null)).IsTrue();
    }

    [Test]
    [Arguments(StreamViewType.KeysOnly, false, false)]
    [Arguments(StreamViewType.NewImage, true, false)]
    [Arguments(StreamViewType.OldImage, false, true)]
    public async Task GetRecords_ViewType_DecidesWhichImagesAModifyCarries(StreamViewType viewType, bool hasNewImage, bool hasOldImage)
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(viewType));
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "1") });
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", "2") });
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);

        // Act
        var records = await StreamReading.ReadAsync(sandbox.Streams, arn, 2);

        // Assert
        var modify = records[1].DynamoDb!;
        await Assert.That((records[1].EventName, modify.Keys!["pk"].AsString(), modify.NewImage is not null, modify.OldImage is not null))
            .IsEqualTo((OperationType.Modify, "a", hasNewImage, hasOldImage));
    }

    [Test]
    public async Task GetRecords_Limit_ReturnsAtMostLimitRecordsAndAnIteratorForTheRest()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];
        var iterator = (await sandbox.Streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = arn,
            ShardId = shard.ShardId!,
            ShardIteratorType = ShardIteratorType.TrimHorizon,
        })).ShardIterator!;

        // Act
        var first = await sandbox.Streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = iterator, Limit = 1 });
        var rest = await StreamReading.ReadFromAsync(sandbox.Streams, first.NextShardIterator!, 2);

        // Assert
        await Assert.That(first.Records.Count).IsEqualTo(1);
        await Assert.That(first.Records[0].EventName).IsEqualTo(OperationType.Insert);
        await Assert.That(rest.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task GetShardIterator_AfterSequenceNumber_StartsBehindThatRecord()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var all = await StreamReading.ReadAsync(sandbox.Streams, arn, 3);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];

        // Act
        var iterator = (await sandbox.Streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = arn,
            ShardId = shard.ShardId!,
            ShardIteratorType = ShardIteratorType.AfterSequenceNumber,
            SequenceNumber = all[0].DynamoDb!.SequenceNumber,
        })).ShardIterator!;
        var records = await StreamReading.ReadFromAsync(sandbox.Streams, iterator, 2);

        // Assert
        await Assert.That(records.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task GetShardIterator_AtSequenceNumber_StartsWithThatRecord()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var all = await StreamReading.ReadAsync(sandbox.Streams, arn, 3);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];

        // Act
        var iterator = (await sandbox.Streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = arn,
            ShardId = shard.ShardId!,
            ShardIteratorType = ShardIteratorType.AtSequenceNumber,
            SequenceNumber = all[1].DynamoDb!.SequenceNumber,
        })).ShardIterator!;
        var records = await StreamReading.ReadFromAsync(sandbox.Streams, iterator, 2);

        // Assert
        await Assert.That(records.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task GetShardIterator_Latest_SkipsExistingRecordsAndSeesNewOnes()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("before", "x") });
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];
        var iterator = (await sandbox.Streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = arn,
            ShardId = shard.ShardId!,
            ShardIteratorType = ShardIteratorType.Latest,
        })).ShardIterator!;

        // Act
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("after", "y") });
        var records = await StreamReading.ReadFromAsync(sandbox.Streams, iterator, 1);

        // Assert
        await Assert.That(records.Select(static record => record.DynamoDb!.Keys!["pk"].AsString())).IsEquivalentTo(["after"]);
    }

    [Test]
    public async Task GetRecords_ShardOfADisabledStream_IsClosedAndEndsWithoutANextIterator()
    {
        // Arrange: the client has no UpdateTable, so the stream is switched off by hand
        Gaps.SkipIfMissing(backend, Gap.ClosedShardsAfterDisablingTheStream);
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        (await sandbox.Emulator.Raw.SendAsync("UpdateTable", $$$$$"""{"TableName":"{{{{{table}}}}}","StreamSpecification":{"StreamEnabled":false}}""")).EnsureSuccess();
        var description = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!;
        var shard = description.Shards[0];
        var iterator = (await sandbox.Streams.GetShardIteratorAsync(new GetShardIteratorRequest
        {
            StreamArn = arn,
            ShardId = shard.ShardId!,
            ShardIteratorType = ShardIteratorType.TrimHorizon,
        })).ShardIterator!;

        // Act
        var page = await sandbox.Streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = iterator });

        // Assert
        await Assert.That(description.StreamStatus).IsEqualTo(StreamStatus.Disabled);
        await Assert.That(string.IsNullOrEmpty(shard.SequenceNumberRange!.EndingSequenceNumber)).IsFalse();
        await Assert.That((page.Records.Count, page.NextShardIterator)).IsEqualTo((3, null));
    }

    [Test]
    public async Task ReadShard_OpenShard_ReturnsTheRecordsInTheOrderOfTheWrites()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Streams.ReadShardAsync(FromTrimHorizon(arn, shard.ShardId!), timeout.Token).Take(3).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(records.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Insert, OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task ReadShard_AfterSequenceNumberOfARecord_ResumesBehindIt()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var first = await sandbox.Streams.ReadShardAsync(FromTrimHorizon(arn, shard.ShardId!), timeout.Token).FirstAsync(timeout.Token);

        // Act
        var resumed = FromTrimHorizon(arn, shard.ShardId!) with { ShardIteratorType = ShardIteratorType.AfterSequenceNumber, SequenceNumber = first.DynamoDb!.SequenceNumber };
        var rest = await sandbox.Streams.ReadShardAsync(resumed, timeout.Token).Take(2).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(rest.Select(static record => record.EventName)).IsEquivalentTo([OperationType.Modify, OperationType.Remove]);
    }

    [Test]
    public async Task ReadShard_ShardOfADisabledStream_EndsAfterItsRecords()
    {
        // Arrange: the client has no UpdateTable, so the stream is switched off by hand
        Gaps.SkipIfMissing(backend, Gap.ClosedShardsAfterDisablingTheStream);
        Gaps.SkipIfMissing(backend, Gap.OneShardPerStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        (await sandbox.Emulator.Raw.SendAsync("UpdateTable", $$$$$"""{"TableName":"{{{{{table}}}}}","StreamSpecification":{"StreamEnabled":false}}""")).EnsureSuccess();
        var shard = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards[0];
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Streams.ReadShardAsync(FromTrimHorizon(arn, shard.ShardId!), timeout.Token).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(records.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ReadStream_OpenStream_ReturnsTheChangesOfAnItemInOrderFromAListedShard()
    {
        // Arrange: one item, so one shard, also on real DynamoDB
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var shards = (await sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest { StreamArn = arn })).StreamDescription!.Shards.Select(static shard => shard.ShardId).ToList();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Streams.ReadStreamAsync(StreamFromTrimHorizon(arn), timeout.Token).Take(3).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(string.Join(",", records.Select(static change => change.Record.EventName))).IsEqualTo("Insert,Modify,Remove");
        await Assert.That(shards.Contains(records.Select(static change => change.ShardId).Distinct().Single())).IsTrue();
    }

    [Test]
    public async Task ReadStream_DisabledStream_EndsAfterItsRecords()
    {
        // Arrange: the client has no UpdateTable, so the stream is switched off by hand
        Gaps.SkipIfMissing(backend, Gap.ClosedShardsAfterDisablingTheStream);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        (await sandbox.Emulator.Raw.SendAsync("UpdateTable", $$$$$"""{"TableName":"{{{{{table}}}}}","StreamSpecification":{"StreamEnabled":false}}""")).EnsureSuccess();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Streams.ReadStreamAsync(StreamFromTrimHorizon(arn), timeout.Token).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(string.Join(",", records.Select(static change => change.Record.EventName))).IsEqualTo("Insert,Modify,Remove");
    }

    [Test]
    public async Task GetShardIterator_AtTheStartingSequenceNumberOfAShard_ReadsFromItsFirstRecord()
    {
        // Arrange: ReadStreamAsync reads a child that opened 23 hours ago or more at the starting sequence number of its range, so that trimmed records throw, and
        // this shows whether DynamoDB reads from there without a false TrimmedDataAccessException. The first iterators are taken while the shards are empty, the
        // second ones after the write; one table and one item, so it is cheap on real DynamoDB, which is where it counts.
        Gaps.SkipIfMissing(backend, Gap.StartingSequenceNumberOfAnEmptyShard);
        Gaps.SkipIfMissing(backend, Gap.ReadAtTheStartingSequenceNumber);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        var empty = await StreamReading.IteratorsAtStartAsync(sandbox.Streams, arn);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", AttributeValue.FromNumber(1)) });

        // Act
        var early = await StreamReading.ReadFromAsync(sandbox.Streams, empty, 1);
        var late = await StreamReading.ReadFromAsync(sandbox.Streams, await StreamReading.IteratorsAtStartAsync(sandbox.Streams, arn), 1);

        // Assert
        await Assert.That((early[0].EventName, late[0].EventName, late[0].DynamoDb!.SequenceNumber == early[0].DynamoDb!.SequenceNumber))
            .IsEqualTo((OperationType.Insert, OperationType.Insert, true));
    }

    [Test]
    public async Task ReadStream_ShardSplitWithALateSecondChild_ReturnsEveryChangeOfEachItemInOrder()
    {
        // Arrange: only the fake splits a shard on demand. The second child is listed two seconds after the split, which the reader waits for.
        await using var sandbox = await Sandbox.StartAsync(backend);
        if (sandbox.Emulator.Fake is not { } fake)
        {
            Skip.Test($"{backend}: only the fake splits a shard on demand.");
            return;
        }

        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewImage));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        string[] keys = ["k0", "k1", "k2", "k3", "k4", "k5", "k6", "k7"];
        await PutAllAsync(sandbox, table, keys, 1);
        fake.SplitStreamShards(table, secondChildListedAfter: TimeSpan.FromSeconds(2));
        await PutAllAsync(sandbox, table, keys, 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Streams.ReadStreamAsync(StreamFromTrimHorizon(arn), timeout.Token).Take(2 * keys.Length).ToListAsync(timeout.Token);

        // Assert
        var changes = records
            .GroupBy(static change => change.Record.DynamoDb!.Keys!["pk"].AsString())
            .OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => item.Key + ":" + string.Join(">", item.Select(static change => change.Record.DynamoDb!.NewImage!["v"].AsNumber())));
        await Assert.That(string.Join(",", changes)).IsEqualTo(string.Join(",", keys.Select(static key => key + ":1>2")));
    }

    [Test]
    public async Task ReadStream_LatestDuringASplitWithALateSecondChild_ReadsTheWritesOfBothChildren()
    {
        // Arrange: the split is before the start, its second child is listed five seconds later, well after the start, and the items are written once
        // the reader of the first child holds its iterator, so that Latest sees them
        await using var sandbox = await Sandbox.StartAsync(backend);
        if (sandbox.Emulator.Fake is not { } fake)
        {
            Skip.Test($"{backend}: only the fake splits a shard on demand.");
            return;
        }

        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.NewImage));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        fake.SplitStreamShards(table, secondChildListedAfter: TimeSpan.FromSeconds(5));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        string[] keys = ["k0", "k1", "k2", "k3", "k4", "k5", "k6", "k7", "k8", "k9", "k10", "k11", "k12", "k13", "k14", "k15"];
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = StreamFromTrimHorizon(arn) with { ShardIteratorType = ShardIteratorType.Latest, OnJoined = () => joined.TrySetResult() };
        var reading = sandbox.Streams.ReadStreamAsync(latest, timeout.Token).Take(keys.Length).ToListAsync(timeout.Token).AsTask();
        await joined.Task.WaitAsync(timeout.Token);

        // Act
        await PutAllAsync(sandbox, table, keys, 1);
        var records = await reading;

        // Assert
        await Assert.That(string.Join(",", records.Select(static change => change.Record.DynamoDb!.Keys!["pk"].AsString()).Order(StringComparer.Ordinal)))
            .IsEqualTo(string.Join(",", keys.Order(StringComparer.Ordinal)));
    }

    [Test]
    public async Task ReadStream_Table_ReadsTheStreamTheTableNames()
    {
        // Arrange: the stream is listed first, as a new stream of real DynamoDB is described some time after the table turned active
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await sandbox.Client.Table(table, "pk").ReadStreamAsync(sandbox.Streams, ShardIteratorType.TrimHorizon, cancellationToken: timeout.Token)
            .Take(3).ToListAsync(timeout.Token);

        // Assert
        await Assert.That(string.Join(",", records.Select(static change => change.Record.EventName))).IsEqualTo("Insert,Modify,Remove");
    }

    [Test]
    public async Task ReadStream_LatestOnceJoined_ReadsAWriteAfterTheCall()
    {
        // Arrange: a write before the start, which Latest leaves out, and one after the signal. ReadStreamAsync reads only the open shards from Latest.
        Gaps.SkipIfMissing(backend, Gap.OpenShardWithoutEndingSequenceNumber);
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("before", AttributeValue.FromNumber(1)) });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var latest = StreamFromTrimHorizon(arn) with { ShardIteratorType = ShardIteratorType.Latest, OnJoined = () => joined.TrySetResult() };
        var reading = sandbox.Streams.ReadStreamAsync(latest, timeout.Token).Take(1).ToListAsync(timeout.Token).AsTask();
        await joined.Task.WaitAsync(timeout.Token);

        // Act
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("after", AttributeValue.FromNumber(1)) });
        var records = await reading;

        // Assert
        await Assert.That(records.Single().Record.DynamoDb!.Keys!["pk"].AsString()).IsEqualTo("after");
    }

    [Test]
    public async Task ReadStream_PageFailsWithAServerError_ReturnsEveryChangeOnce()
    {
        // Arrange: the first GetRecords gets a 500 that the stream never sent, and the client makes one attempt per call, so the reader of the shard sees it.
        // One item, so one shard, also on real DynamoDB.
        await using var sandbox = await Sandbox.StartAsync(backend);
        var table = await sandbox.CreateTableAsync(TableSpec.WithStream(StreamViewType.KeysOnly));
        await WriteInsertModifyRemoveAsync(sandbox, table);
        var arn = await StreamReading.FindStreamArnAsync(sandbox.Streams, table);
        using var failing = new FailingHandler(sandbox.Emulator.CreateHandler(), failOnCall: 1);
        using var streams = new DynamoDbStreamsClient(sandbox.Emulator.CreateOptions() with { HttpHandler = failing, MaxAttempts = 1 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        // Act
        var records = await streams.ReadStreamAsync(StreamFromTrimHorizon(arn), timeout.Token).Take(3).ToListAsync(timeout.Token);

        // Assert
        await Assert.That((string.Join(",", records.Select(static change => change.Record.EventName)), failing.Failed)).IsEqualTo(("Insert,Modify,Remove", true));
    }

    [Test]
    public async Task DescribeStream_UnknownStream_ThrowsResourceNotFoundException()
    {
        // Arrange
        Gaps.SkipIfMissing(backend, Gap.StreamOfAnUnknownAccountIsNotFound);
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ResourceNotFoundException>(() => sandbox.Streams.DescribeStreamAsync(new DescribeStreamRequest
        {
            StreamArn = "arn:aws:dynamodb:us-east-1:000000000000:table/no-such-table/stream/2020-01-01T00:00:00.000",
        }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ResourceNotFoundException");
    }

    [Test]
    public async Task GetRecords_InvalidIterator_ThrowsValidationException()
    {
        // Arrange
        await using var sandbox = await Sandbox.StartAsync(backend);

        // Act
        var failure = await Items.FailureOfAsync<ValidationException>(() => sandbox.Streams.GetRecordsAsync(new GetRecordsRequest { ShardIterator = "not-an-iterator" }));

        // Assert
        await Assert.That(failure.ErrorCode).IsEqualTo("ValidationException");
    }

    // The emulators deliver a record at once, so an empty page waits only briefly.
    private static ReadShardRequest FromTrimHorizon(string streamArn, string shardId) => new()
    {
        StreamArn = streamArn,
        ShardId = shardId,
        ShardIteratorType = ShardIteratorType.TrimHorizon,
        EmptyPageDelay = TimeSpan.FromMilliseconds(50),
    };

    private static ReadStreamRequest StreamFromTrimHorizon(string streamArn) => new()
    {
        StreamArn = streamArn,
        ShardIteratorType = ShardIteratorType.TrimHorizon,
        EmptyPageDelay = TimeSpan.FromMilliseconds(50),
    };

    private static async Task PutAllAsync(Sandbox sandbox, string table, string[] keys, int value)
    {
        foreach (var key in keys)
        {
            await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item(key, AttributeValue.FromNumber(value)) });
        }
    }

    private static async Task WriteInsertModifyRemoveAsync(Sandbox sandbox, string table)
    {
        await sandbox.Client.PutItemAsync(new PutItemRequest { TableName = table, Item = Items.Item("a", AttributeValue.FromNumber(1)) });
        await sandbox.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = table,
            Key = Items.Key("a"),
            UpdateExpression = "SET v = :v",
            ExpressionAttributeValues = Items.Values((":v", AttributeValue.FromNumber(2))),
        });
        await sandbox.Client.DeleteItemAsync(new DeleteItemRequest { TableName = table, Key = Items.Key("a") });
    }

    // Answers the nth GetRecords with a 500 of DynamoDB without sending it on.
    private sealed class FailingHandler(HttpMessageHandler inner, int failOnCall) : DelegatingHandler(inner)
    {
        private int _calls;

        public bool Failed => Volatile.Read(ref _calls) >= failOnCall;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.TryGetValues("X-Amz-Target", out var targets) && targets.Single().EndsWith(".GetRecords", StringComparison.Ordinal)
                && Interlocked.Increment(ref _calls) == failOnCall)
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""{"__type":"com.amazon.coral.service#InternalFailure","message":"Failure of the test."}""", System.Text.Encoding.UTF8, "application/x-amz-json-1.0"),
                });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
