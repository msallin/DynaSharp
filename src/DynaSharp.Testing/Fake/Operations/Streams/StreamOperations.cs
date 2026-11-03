using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Streams;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The operations of DynamoDB Streams: <c>ListStreams</c>, <c>DescribeStream</c>, <c>GetShardIterator</c> and <c>GetRecords</c>, over the <see cref="FakeStream"/>s of the tables.
/// A stream has one shard, without a parent, open while the stream is enabled and closed from when it is disabled or its table deleted.
/// </summary>
/// <remarks>
/// <para>
/// An iterator is valid for 15 minutes on the clock of the fake and is then <c>ExpiredIteratorException</c>; a position older than the 24 hours records are kept is
/// <c>TrimmedDataAccessException</c>; a stream closed for 24 hours is <c>ResourceNotFoundException</c>. A page holds at most <c>Limit</c> records, 1,000 by default, and at most
/// 1 MB of them by <c>SizeBytes</c>, and the last page of a closed shard has no <c>NextShardIterator</c>. A record arrives in the answer of the request that made it.
/// </para>
/// <para>The records, the shard ids and the iterators are those of <see cref="FakeStream"/> and <see cref="ShardIterator"/>.</para>
/// </remarks>
internal static class StreamOperations
{
    /// <summary>The operation.</summary>
    public const string ListStreamsName = "ListStreams";

    /// <summary>The operation.</summary>
    public const string DescribeStreamName = "DescribeStream";

    /// <summary>The operation.</summary>
    public const string GetShardIteratorName = "GetShardIterator";

    /// <summary>The operation.</summary>
    public const string GetRecordsName = "GetRecords";

    private const int MaxStreams = 100;
    private const int MaxShards = 100;
    private const int MaxRecords = 1000;
    private const long MaxPageBytes = 1024 * 1024;
    private const int MinArnLength = 37;
    private const int MaxArnLength = 1024;
    private const int MinSequenceNumberLength = 21;
    private const int MaxSequenceNumberLength = 40;

    /// <summary>Answers <c>ListStreams</c>: the streams in the order they were enabled, of one table or of all, after <c>ExclusiveStartStreamArn</c>, at most <c>Limit</c>, and the last one as <c>LastEvaluatedStreamArn</c> when more follow. A table without a stream, a table that does not exist and an <c>ExclusiveStartStreamArn</c> not in the list give an empty list.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleListStreams(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, ListStreamsName, "TableName", "Limit", "ExclusiveStartStreamArn");
        var tableName = RequestJson.Optional(request, "TableName") is { } name ? RequestJson.ReadString(name, "TableName") : null;
        if (tableName is not null)
        {
            TableOperations.ThrowIfInvalidName(tableName, "tableName");
        }

        var limit = ReadLimit(request, MaxStreams);
        var start = RequestJson.Optional(request, "ExclusiveStartStreamArn") is { } startArn ? ReadArn(startArn, "ExclusiveStartStreamArn") : null;
        var now = db.Clock.GetUtcNow();
        var streams = db.Streams.Where(stream => !stream.IsGone(now) && (tableName is null || stream.TableName == tableName)).ToList();
        if (start is not null)
        {
            var after = streams.FindIndex(stream => stream.Arn == start);
            streams = after < 0 ? [] : streams[(after + 1)..];
        }

        var page = streams.Take(limit).ToList();
        response.WriteStartArray("Streams"u8);
        foreach (var stream in page)
        {
            response.WriteStartObject();
            response.WriteString("StreamArn"u8, stream.Arn);
            response.WriteString("TableName"u8, stream.TableName);
            response.WriteString("StreamLabel"u8, stream.Label);
            response.WriteEndObject();
        }

        response.WriteEndArray();
        OperationJson.WriteStringIfSet(response, "LastEvaluatedStreamArn"u8, streams.Count > page.Count ? page[^1].Arn : null);
    }

    /// <summary>
    /// Answers <c>DescribeStream</c>: the settings of the stream and its one shard, after <c>ExclusiveStartShardId</c>. A <c>ShardFilter</c> of <c>CHILD_SHARDS</c> asks for the
    /// children of a shard, and a shard of the fake has none.
    /// </summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleDescribeStream(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, DescribeStreamName, "StreamArn", "Limit", "ExclusiveStartShardId", "ShardFilter");
        var arn = ReadArn(RequestJson.Required(request, "StreamArn"), "StreamArn");
        _ = ReadLimit(request, MaxShards);
        var start = RequestJson.Optional(request, "ExclusiveStartShardId") is { } startId ? RequestJson.ReadString(startId, "ExclusiveStartShardId") : null;
        var filtered = RequestJson.Optional(request, "ShardFilter") is { } filter && ReadChildShardsFilter(filter);
        var stream = db.FindStream(arn);

        // Shard ids of DynamoDB grow with the time a shard opens, so the shards after a start are those whose id sorts after it.
        var listed = !filtered && (start is null || string.CompareOrdinal(stream.ShardId, start) > 0);
        response.WriteStartObject("StreamDescription"u8);
        response.WriteString("StreamArn"u8, stream.Arn);
        response.WriteString("StreamLabel"u8, stream.Label);
        WireNames.StreamStatuses.Write(response, "StreamStatus"u8, stream.IsEnabled ? StreamStatus.Enabled : StreamStatus.Disabled);
        WireNames.StreamViewTypes.Write(response, "StreamViewType"u8, stream.ViewType);
        response.WriteNumber("CreationRequestDateTime"u8, stream.CreationDateTime.ToUnixTimeMilliseconds() / 1000m);
        response.WriteString("TableName"u8, stream.TableName);
        TableJson.WriteKeySchema(response, ResponseJson.KeyElements(stream.KeySchema));
        response.WriteStartArray("Shards"u8);
        if (listed)
        {
            response.WriteStartObject();
            response.WriteString("ShardId"u8, stream.ShardId);
            response.WriteStartObject("SequenceNumberRange"u8);
            response.WriteString("StartingSequenceNumber"u8, Text(stream.StartingSequenceNumber));
            if (stream.EndingSequenceNumber is { } ending)
            {
                response.WriteString("EndingSequenceNumber"u8, Text(ending));
            }

            response.WriteEndObject();
            response.WriteEndObject();
        }

        response.WriteEndArray();
        response.WriteEndObject();
    }

    /// <summary>
    /// Answers <c>GetShardIterator</c>: <c>TRIM_HORIZON</c> at the oldest record kept, <c>LATEST</c> behind the newest, <c>AT_SEQUENCE_NUMBER</c> at the first record with that
    /// sequence number or a larger one, and <c>AFTER_SEQUENCE_NUMBER</c> at the first with a larger one. A sequence number outside the range of the shard, and one given with <c>TRIM_HORIZON</c> or <c>LATEST</c>, is a <c>ValidationException</c>,
    /// and a position that has been trimmed is <c>TrimmedDataAccessException</c>.
    /// </summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleGetShardIterator(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, GetShardIteratorName, "StreamArn", "ShardId", "ShardIteratorType", "SequenceNumber");
        var arn = ReadArn(RequestJson.Required(request, "StreamArn"), "StreamArn");
        var shardId = RequestJson.ReadString(RequestJson.Required(request, "ShardId"), "ShardId");
        var type = RequestJson.ReadEnum(RequestJson.Required(request, "ShardIteratorType"), "ShardIteratorType", WireNames.ShardIteratorTypes);
        var given = RequestJson.Optional(request, "SequenceNumber");
        var needsSequenceNumber = type is ShardIteratorType.AtSequenceNumber or ShardIteratorType.AfterSequenceNumber;
        if (needsSequenceNumber != given.HasValue)
        {
            throw FakeError.Validation(needsSequenceNumber
                ? "SequenceNumber is required for the ShardIteratorType AT_SEQUENCE_NUMBER and AFTER_SEQUENCE_NUMBER"
                : "One or more parameter values were invalid: SequenceNumber should not be passed for this iterator type");
        }

        var sequenceNumber = given is { } value ? ReadSequenceNumber(value) : (BigInteger?)null;

        var stream = db.FindStream(arn);
        if (shardId != stream.ShardId)
        {
            throw FakeError.ResourceNotFound($"Requested resource not found: Shard does not exist: {shardId}");
        }

        var now = db.Clock.GetUtcNow();
        var horizon = stream.TrimHorizon(now);
        var position = type switch
        {
            ShardIteratorType.TrimHorizon => horizon,
            ShardIteratorType.Latest => stream.Records.Count,
            _ => Locate(stream, sequenceNumber!.Value, after: type == ShardIteratorType.AfterSequenceNumber, horizon),
        };
        response.WriteString("ShardIterator"u8, new ShardIterator(stream.Arn, stream.ShardId, position, now).Encode());
    }

    /// <summary>
    /// Answers <c>GetRecords</c>: the records from the position of the iterator, at most <c>Limit</c> and 1 MB, and the iterator behind them, which a closed shard read to its end
    /// leaves out.
    /// </summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleGetRecords(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, GetRecordsName, "ShardIterator", "Limit");
        var text = RequestJson.ReadString(RequestJson.Required(request, "ShardIterator"), "ShardIterator");
        var limit = ReadLimit(request, MaxRecords);
        var iterator = ShardIterator.Decode(text) ?? throw InvalidIterator();
        var now = db.Clock.GetUtcNow();
        if (now - iterator.IssuedAt >= ShardIterator.Lifetime)
        {
            throw FakeError.ExpiredIterator();
        }

        var stream = db.FindStream(iterator.StreamArn);
        if (iterator.ShardId != stream.ShardId || iterator.Position > stream.Records.Count)
        {
            throw InvalidIterator();
        }

        if (iterator.Position < stream.TrimHorizon(now))
        {
            throw FakeError.TrimmedDataAccess();
        }

        var next = iterator.Position;
        long bytes = 0;
        response.WriteStartArray("Records"u8);
        while (next < stream.Records.Count && next - iterator.Position < limit && (next == iterator.Position || bytes + stream.Records[next].SizeBytes <= MaxPageBytes))
        {
            bytes += stream.Records[next].SizeBytes;
            WriteRecord(response, stream, stream.Records[next]);
            next++;
        }

        response.WriteEndArray();
        if (stream.IsEnabled || next < stream.Records.Count)
        {
            response.WriteString("NextShardIterator"u8, (iterator with { Position = next, IssuedAt = now }).Encode());
        }
    }

    private static void WriteRecord(Utf8JsonWriter response, FakeStream stream, FakeStreamRecord record)
    {
        response.WriteStartObject();
        response.WriteString("eventID"u8, record.EventId);
        WireNames.OperationTypes.Write(response, "eventName"u8, record.EventName);
        response.WriteString("eventVersion"u8, "1.1");
        response.WriteString("eventSource"u8, "aws:dynamodb");
        response.WriteString("awsRegion"u8, StubClient.RegionName);
        response.WriteStartObject("dynamodb"u8);
        response.WriteNumber("ApproximateCreationDateTime"u8, record.ApproximateCreationDateTime.ToUnixTimeSeconds());
        ResponseJson.WriteItem(response, "Keys", record.Keys);
        if (record.NewImage is { } newImage)
        {
            ResponseJson.WriteItem(response, "NewImage", newImage);
        }

        if (record.OldImage is { } oldImage)
        {
            ResponseJson.WriteItem(response, "OldImage", oldImage);
        }

        response.WriteString("SequenceNumber"u8, Text(record.SequenceNumber));
        response.WriteNumber("SizeBytes"u8, record.SizeBytes);
        WireNames.StreamViewTypes.Write(response, "StreamViewType"u8, stream.ViewType);
        response.WriteEndObject();
        if (record.ByTimeToLive)
        {
            // What DynamoDB writes for a delete of its sweep of time to live, so a consumer can tell it from a delete of a client.
            response.WriteStartObject("userIdentity"u8);
            response.WriteString("PrincipalId"u8, "dynamodb.amazonaws.com");
            response.WriteString("Type"u8, "Service");
            response.WriteEndObject();
        }

        response.WriteEndObject();
    }

    private static int Locate(FakeStream stream, BigInteger sequenceNumber, bool after, int horizon)
    {
        if (sequenceNumber < stream.StartingSequenceNumber || sequenceNumber > stream.LastSequenceNumber)
        {
            throw FakeError.Validation($"Invalid SequenceNumber {Text(sequenceNumber)} for the shard {stream.ShardId}: it is outside the range of the shard");
        }

        var records = stream.Records;
        var position = 0;
        while (position < records.Count && (after ? records[position].SequenceNumber <= sequenceNumber : records[position].SequenceNumber < sequenceNumber))
        {
            position++;
        }

        return position < horizon ? throw FakeError.TrimmedDataAccess() : position;
    }

    private static bool ReadChildShardsFilter(JsonElement filter)
    {
        const string Where = "ShardFilter of DescribeStream";
        RequestJson.Members(filter, Where, "Type", "ShardId");
        _ = RequestJson.ReadEnum(RequestJson.Required(filter, "Type"), "Type", WireNames.ShardFilterTypes);
        _ = RequestJson.ReadString(RequestJson.Required(filter, "ShardId"), "ShardId");
        return true;
    }

    private static string ReadArn(JsonElement value, string member)
    {
        var arn = RequestJson.ReadString(value, member);
        if (arn.Length is < MinArnLength or > MaxArnLength)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{arn}' at '{char.ToLowerInvariant(member[0])}{member[1..]}' failed to satisfy constraint: Member must have length "
                + (arn.Length < MinArnLength ? $"greater than or equal to {MinArnLength}" : $"less than or equal to {MaxArnLength}"));
        }

        return arn;
    }

    private static BigInteger ReadSequenceNumber(JsonElement value)
    {
        var text = RequestJson.ReadString(value, "SequenceNumber");
        if (text.Length is < MinSequenceNumberLength or > MaxSequenceNumberLength || !text.All(char.IsAsciiDigit))
        {
            throw FakeError.Validation($"Invalid SequenceNumber {text}: a sequence number is a decimal number of {MinSequenceNumberLength} to {MaxSequenceNumberLength} digits");
        }

        return BigInteger.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static int ReadLimit(JsonElement request, int max)
    {
        var limit = RequestJson.Optional(request, "Limit") is { } value ? RequestJson.ReadInt(value, "Limit") : max;
        if (limit < 1 || limit > max)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{limit}' at 'limit' failed to satisfy constraint: Member must have value {(limit < 1 ? "greater than or equal to 1" : $"less than or equal to {max}")}");
        }

        return limit;
    }

    private static string Text(BigInteger sequenceNumber) => sequenceNumber.ToString(CultureInfo.InvariantCulture);

    private static FakeError InvalidIterator() => FakeError.Validation("Invalid ShardIterator");
}
