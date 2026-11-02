using System.Globalization;
using System.Text.Json;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Requests;
using DynaSharp.Serialization;
using DynaSharp.Streams;

namespace DynaSharp.Testing;

/// <content>The answers of DynamoDB Streams, for a <see cref="DynamoDbStreamsClient"/> over a <see cref="StubHandler"/>, which <see cref="StubClient.CreateStreams"/> makes.</content>
public sealed partial class StubResponse
{
    // The creation time of the tables here, as the label of a stream prints it.
    private const string StreamLabel = "2026-01-01T00:00:00.000";
    private const string FirstSequenceNumber = "100000000000000000000";

    // What a sequence number of a record is made of, 21 digits as FirstSequenceNumber: the position in the page goes behind this.
    private const string SequenceNumberPrefix = "1000000000000000";
    private const string PositionFormat = "D5";

    /// <summary>
    /// Creates the answer to a <c>DescribeStream</c> of the enabled stream of a table with the key <c>pk</c>, with old and new images: <c>{"StreamDescription":{...}}</c>. Read the ARN from the response, or give one with the
    /// <see cref="StreamDescription(DynaSharp.Streams.StreamDescription)"/> overload, which also says the rest.
    /// </summary>
    /// <param name="tableName">The table of the stream.</param>
    /// <param name="shards">The shards, as <see cref="Shard"/> makes them.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    public static StubResponse StreamDescription(string tableName, params ReadOnlySpan<Shard> shards)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        return StreamDescription(new StreamDescription
        {
            StreamArn = ArnPrefix + tableName + "/stream/" + StreamLabel,
            StreamLabel = StreamLabel,
            StreamStatus = StreamStatus.Enabled,
            StreamViewType = StreamViewType.NewAndOldImages,
            CreationRequestDateTime = DateTimeOffset.FromUnixTimeSeconds(CreationEpochSeconds),
            TableName = tableName,
            KeySchema = [new KeySchemaElement(DefaultKey, KeyType.Hash)],
            Shards = shards.ToArray(),
        });
    }

    /// <summary>
    /// Creates the answer to a <c>DescribeStream</c> from a whole description, for what a test cares about: a status that is not <c>ENABLED</c>, or <see cref="StreamDescription.LastEvaluatedShardId"/> for a page that more shards
    /// follow. It sends what the description holds and adds nothing.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    public static StubResponse StreamDescription(StreamDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return Json(Render(json =>
        {
            json.WriteStartObject("StreamDescription");
            OperationJson.WriteStringIfSet(json, "StreamArn"u8, description.StreamArn);
            OperationJson.WriteStringIfSet(json, "StreamLabel"u8, description.StreamLabel);
            WireNames.StreamStatuses.WriteIfSet(json, "StreamStatus"u8, description.StreamStatus);
            WireNames.StreamViewTypes.WriteIfSet(json, "StreamViewType"u8, description.StreamViewType);
            WriteEpochSecondsIfSet(json, "CreationRequestDateTime", description.CreationRequestDateTime);
            OperationJson.WriteStringIfSet(json, "TableName"u8, description.TableName);
            TableJson.WriteKeySchema(json, description.KeySchema);
            json.WriteStartArray("Shards");
            foreach (var shard in description.Shards)
            {
                WriteShard(json, shard);
            }

            json.WriteEndArray();
            OperationJson.WriteStringIfSet(json, "LastEvaluatedShardId"u8, description.LastEvaluatedShardId);
            json.WriteEndObject();
        }));
    }

    /// <summary>Creates a shard for <see cref="StreamDescription(string, ReadOnlySpan{Shard})"/>. A shard without an end is open: DynamoDB still writes to it.</summary>
    /// <param name="shardId">The id of the shard, which a <c>GetShardIterator</c> names.</param>
    /// <param name="parentShardId">The shard this one split from, or <see langword="null"/> for a shard without a parent.</param>
    /// <param name="endingSequenceNumber">The last sequence number of a closed shard, or <see langword="null"/> for an open one.</param>
    /// <returns>The shard.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shardId"/> is <see langword="null"/>.</exception>
    public static Shard Shard(string shardId, string? parentShardId = null, string? endingSequenceNumber = null)
    {
        ArgumentNullException.ThrowIfNull(shardId);
        return new Shard
        {
            ShardId = shardId,
            ParentShardId = parentShardId,
            SequenceNumberRange = new SequenceNumberRange { StartingSequenceNumber = FirstSequenceNumber, EndingSequenceNumber = endingSequenceNumber },
        };
    }

    /// <summary>Creates the answer to a <c>GetShardIterator</c>: <c>{"ShardIterator":"..."}</c>.</summary>
    /// <param name="iterator">The iterator, which a <c>GetRecords</c> of the test then sends.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="iterator"/> is <see langword="null"/>.</exception>
    public static StubResponse ShardIterator(string iterator)
    {
        ArgumentNullException.ThrowIfNull(iterator);
        return Json(Render(json => json.WriteString("ShardIterator", iterator)));
    }

    /// <summary>
    /// Creates the answer to a <c>GetRecords</c>: <c>Records("next", StubResponse.Inserted(order), StubResponse.Removed(other))</c>. A record without <see cref="ChangeRecord.EventId"/> or
    /// <see cref="StreamRecord.SequenceNumber"/> gets one from its position in the page, so that the records of a page differ. The next page starts over, so a test of a checkpoint gives the numbers.
    /// </summary>
    /// <param name="nextShardIterator">The iterator of the next page. On an open shard there is always one, also for a page without records. <see langword="null"/> says that a closed shard was read to its end.</param>
    /// <param name="records">The records, as <see cref="Inserted{T}"/>, <see cref="Modified{T}"/>, <see cref="Removed{T}"/> and <see cref="Change"/> make them. A record is sent with what it holds.</param>
    /// <returns>The response.</returns>
    public static StubResponse Records(string? nextShardIterator, params ReadOnlySpan<ChangeRecord> records)
    {
        var all = records.ToArray();
        return Json(Render(json =>
        {
            json.WriteStartArray("Records");
            for (var i = 0; i < all.Length; i++)
            {
                WriteChangeRecord(json, all[i], i + 1);
            }

            json.WriteEndArray();
            OperationJson.WriteStringIfSet(json, "NextShardIterator"u8, nextShardIterator);
        }));
    }

    /// <summary>Creates the record of a new item, for <see cref="Records"/>.</summary>
    /// <typeparam name="T">The entity. <see cref="Change"/> takes an <see cref="DynaSharp.Item"/>, which does not say what its key is.</typeparam>
    /// <param name="newImage">The item, written as it is stored, and the source of the key.</param>
    /// <param name="sequenceNumber">The sequence number, or <see langword="null"/> for one from the position in the page.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="newImage"/> is <see langword="null"/>.</exception>
    public static ChangeRecord Inserted<T>(T newImage, string? sequenceNumber = null)
        where T : IDynamoDbEntity<T> => Change(OperationType.Insert, KeyOf(newImage), null, DynaSharp.Item.From(newImage), sequenceNumber);

    /// <summary>Creates the record of a changed item, with both images, for <see cref="Records"/>.</summary>
    /// <typeparam name="T">The entity. <see cref="Change"/> takes an <see cref="DynaSharp.Item"/>, which does not say what its key is.</typeparam>
    /// <param name="oldImage">The item before the change.</param>
    /// <param name="newImage">The item after the change, and the source of the key.</param>
    /// <param name="sequenceNumber">The sequence number, or <see langword="null"/> for one from the position in the page.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldImage"/> or <paramref name="newImage"/> is <see langword="null"/>.</exception>
    public static ChangeRecord Modified<T>(T oldImage, T newImage, string? sequenceNumber = null)
        where T : IDynamoDbEntity<T> => Change(OperationType.Modify, KeyOf(newImage), DynaSharp.Item.From(oldImage), DynaSharp.Item.From(newImage), sequenceNumber);

    /// <summary>
    /// Creates the record of a deleted item, for <see cref="Records"/>. A delete by time to live also has a <see cref="ChangeRecord.UserIdentity"/>, which a test adds:
    /// <c>Removed(order) with { UserIdentity = new RecordIdentity { PrincipalId = "dynamodb.amazonaws.com", Type = "Service" } }</c>.
    /// </summary>
    /// <typeparam name="T">The entity. <see cref="Change"/> takes an <see cref="DynaSharp.Item"/>, which does not say what its key is.</typeparam>
    /// <param name="oldImage">The item before the delete, and the source of the key.</param>
    /// <param name="sequenceNumber">The sequence number, or <see langword="null"/> for one from the position in the page.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="oldImage"/> is <see langword="null"/>.</exception>
    public static ChangeRecord Removed<T>(T oldImage, string? sequenceNumber = null)
        where T : IDynamoDbEntity<T> => Change(OperationType.Remove, KeyOf(oldImage), DynaSharp.Item.From(oldImage), null, sequenceNumber);

    /// <summary>
    /// Creates a record from attributes, for <see cref="Records"/>: images of an <see cref="DynaSharp.Item"/>, or a record without images, as a stream with <see cref="StreamViewType.KeysOnly"/> has them. Whatever the images, the record says
    /// <see cref="StreamViewType.NewAndOldImages"/>, the stream of <see cref="StreamDescription(string, ReadOnlySpan{Shard})"/>. <see cref="ChangeRecord"/> and <see cref="StreamRecord"/> are records, so <c>with</c> changes the rest.
    /// </summary>
    /// <param name="eventName">What happened to the item.</param>
    /// <param name="keys">The key attributes, as <see cref="KeyOf{T}"/> makes them from an entity.</param>
    /// <param name="oldImage">The item before the change, or <see langword="null"/> for none.</param>
    /// <param name="newImage">The item after the change, or <see langword="null"/> for none.</param>
    /// <param name="sequenceNumber">The sequence number, or <see langword="null"/> for one from the position in the page.</param>
    /// <returns>The record.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    public static ChangeRecord Change(OperationType eventName, Item keys, Item? oldImage = null, Item? newImage = null, string? sequenceNumber = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return new ChangeRecord
        {
            EventName = eventName,
            EventVersion = "1.1",
            EventSource = "aws:dynamodb",
            Region = StubClient.RegionName,
            DynamoDb = new StreamRecord
            {
                ApproximateCreationDateTime = DateTimeOffset.FromUnixTimeSeconds(CreationEpochSeconds),
                Keys = keys,
                OldImage = oldImage,
                NewImage = newImage,
                SequenceNumber = sequenceNumber,
                SizeBytes = 100,
                StreamViewType = StreamViewType.NewAndOldImages,
            },
        };
    }

    // The service model types these as timestamps, which this protocol sends as seconds since the epoch, with a fraction when there is one: 1767225600 or 1767225600.125.
    private static void WriteEpochSecondsIfSet(Utf8JsonWriter json, string member, DateTimeOffset? value)
    {
        if (value is { } time)
        {
            json.WriteNumber(member, time.ToUnixTimeMilliseconds() / 1000m);
        }
    }

    private static void WriteShard(Utf8JsonWriter json, Shard shard)
    {
        json.WriteStartObject();
        OperationJson.WriteStringIfSet(json, "ShardId"u8, shard.ShardId);
        if (shard.SequenceNumberRange is { } range)
        {
            json.WriteStartObject("SequenceNumberRange");
            OperationJson.WriteStringIfSet(json, "StartingSequenceNumber"u8, range.StartingSequenceNumber);
            OperationJson.WriteStringIfSet(json, "EndingSequenceNumber"u8, range.EndingSequenceNumber);
            json.WriteEndObject();
        }

        OperationJson.WriteStringIfSet(json, "ParentShardId"u8, shard.ParentShardId);
        json.WriteEndObject();
    }

    // {"eventID":"1","eventName":"INSERT",...,"dynamodb":{...}}: this one structure of the service model spells its members in lower case.
    private static void WriteChangeRecord(Utf8JsonWriter json, ChangeRecord record, int position)
    {
        json.WriteStartObject();
        json.WriteString("eventID", record.EventId ?? position.ToString(CultureInfo.InvariantCulture));
        WireNames.OperationTypes.WriteIfSet(json, "eventName"u8, record.EventName);
        OperationJson.WriteStringIfSet(json, "eventVersion"u8, record.EventVersion);
        OperationJson.WriteStringIfSet(json, "eventSource"u8, record.EventSource);
        OperationJson.WriteStringIfSet(json, "awsRegion"u8, record.Region);
        if (record.DynamoDb is { } change)
        {
            json.WriteStartObject("dynamodb");
            WriteEpochSecondsIfSet(json, "ApproximateCreationDateTime", change.ApproximateCreationDateTime);
            var writer = new DynamoDbWriter(json);
            OperationJson.WriteItemIfSet(writer, "Keys"u8, change.Keys);
            OperationJson.WriteItemIfSet(writer, "NewImage"u8, change.NewImage);
            OperationJson.WriteItemIfSet(writer, "OldImage"u8, change.OldImage);
            json.WriteString("SequenceNumber", change.SequenceNumber ?? SequenceNumberPrefix + position.ToString(PositionFormat, CultureInfo.InvariantCulture));
            if (change.SizeBytes is { } size)
            {
                json.WriteNumber("SizeBytes", size);
            }

            WireNames.StreamViewTypes.WriteIfSet(json, "StreamViewType"u8, change.StreamViewType);
            json.WriteEndObject();
        }

        if (record.UserIdentity is { } identity)
        {
            json.WriteStartObject("userIdentity");
            OperationJson.WriteStringIfSet(json, "PrincipalId"u8, identity.PrincipalId);
            OperationJson.WriteStringIfSet(json, "Type"u8, identity.Type);
            json.WriteEndObject();
        }

        json.WriteEndObject();
    }
}
