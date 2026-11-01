using System.Text.Json.Nodes;
using DynaSharp.Streams;

namespace DynaSharp.UnitTests.Operations;

// The DynamoDB Streams operations, in the shape of botocore's parsed responses.
internal static partial class ResponseJson
{
    public static string Describe(ListStreamsResponse response) => Build(json =>
    {
        json.WriteStartArray("Streams");
        foreach (var stream in response.Streams)
        {
            json.WriteStartObject();
            json.WriteString("StreamArn", stream.StreamArn);
            WriteString(json, "TableName", stream.TableName);
            WriteString(json, "StreamLabel", stream.StreamLabel);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteString(json, "LastEvaluatedStreamArn", response.LastEvaluatedStreamArn);
    });

    public static string Describe(DescribeStreamResponse response) => Build(json =>
    {
        var description = response.StreamDescription;
        json.WriteStartObject("StreamDescription");
        json.WriteString("StreamArn", description.StreamArn);
        WriteString(json, "StreamLabel", description.StreamLabel);
        WriteString(json, "StreamStatus", description.StreamStatus switch
        {
            StreamStatus.Enabling => "ENABLING",
            StreamStatus.Enabled => "ENABLED",
            StreamStatus.Disabling => "DISABLING",
            StreamStatus.Disabled => "DISABLED",
            _ => null,
        });
        WriteString(json, "StreamViewType", GetWireName(description.StreamViewType));
        WriteDate(json, "CreationRequestDateTime", description.CreationRequestDateTime);
        WriteString(json, "TableName", description.TableName);
        WriteKeySchema(json, "KeySchema", description.KeySchema);
        json.WriteStartArray("Shards");
        foreach (var shard in description.Shards)
        {
            json.WriteStartObject();
            json.WriteString("ShardId", shard.ShardId);
            if (shard.SequenceNumberRange is { } range)
            {
                json.WriteStartObject("SequenceNumberRange");
                WriteString(json, "StartingSequenceNumber", range.StartingSequenceNumber);
                WriteString(json, "EndingSequenceNumber", range.EndingSequenceNumber);
                json.WriteEndObject();
            }

            WriteString(json, "ParentShardId", shard.ParentShardId);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteString(json, "LastEvaluatedShardId", description.LastEvaluatedShardId);
        json.WriteEndObject();
    });

    public static string Describe(GetShardIteratorResponse response) => Build(json => json.WriteString("ShardIterator", response.ShardIterator));

    public static string Describe(GetRecordsResponse response) => Build(json =>
    {
        json.WriteStartArray("Records");
        foreach (var record in response.Records)
        {
            json.WriteStartObject();
            WriteString(json, "eventID", record.EventId);
            WriteString(json, "eventName", record.EventName switch
            {
                OperationType.Insert => "INSERT",
                OperationType.Modify => "MODIFY",
                OperationType.Remove => "REMOVE",
                _ => null,
            });
            WriteString(json, "eventVersion", record.EventVersion);
            WriteString(json, "eventSource", record.EventSource);
            WriteString(json, "awsRegion", record.Region);
            if (record.DynamoDb is { } change)
            {
                json.WriteStartObject("dynamodb");
                WriteDate(json, "ApproximateCreationDateTime", change.ApproximateCreationDateTime);
                WriteItemIfPresent(json, "Keys", change.Keys);
                WriteItemIfPresent(json, "NewImage", change.NewImage);
                WriteItemIfPresent(json, "OldImage", change.OldImage);
                WriteString(json, "SequenceNumber", change.SequenceNumber);
                if (change.SizeBytes is { } size)
                {
                    json.WriteNumber("SizeBytes", size);
                }

                WriteString(json, "StreamViewType", GetWireName(change.StreamViewType));
                json.WriteEndObject();
            }

            if (record.UserIdentity is { } identity)
            {
                json.WriteStartObject("userIdentity");
                WriteString(json, "PrincipalId", identity.PrincipalId);
                WriteString(json, "Type", identity.Type);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteString(json, "NextShardIterator", response.NextShardIterator);
    });

    /// <summary>Adds the defaults of the library to what botocore parsed: an absent list of streams is empty.</summary>
    /// <param name="botocoreJson">What botocore parsed.</param>
    /// <param name="listName">The list that the library reports as empty when it is absent: <c>Streams</c>, <c>Records</c> or <c>TableNames</c>.</param>
    /// <returns>The same with the list added if it was absent.</returns>
    public static string WithEmptyListIfAbsent(string botocoreJson, string listName)
    {
        var response = JsonNode.Parse(botocoreJson)!.AsObject();
        response.TryAdd(listName, new JsonArray());
        return response.ToJsonString();
    }

    /// <summary>Adds the defaults of the library to what botocore parsed for a stream description: no key schema and no shards.</summary>
    /// <param name="botocoreJson">What botocore parsed.</param>
    /// <returns>The same with <c>KeySchema</c> and <c>Shards</c> added if absent.</returns>
    public static string WithStreamDescriptionDefaults(string botocoreJson)
    {
        var response = JsonNode.Parse(botocoreJson)!.AsObject();
        var description = response["StreamDescription"]!.AsObject();
        description.TryAdd("KeySchema", new JsonArray());
        description.TryAdd("Shards", new JsonArray());
        return response.ToJsonString();
    }
}
