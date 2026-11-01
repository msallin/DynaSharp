using System.Globalization;
using System.Text;
using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// DynamoDB as far as the queries of a fan-out see it: partitions of items, handed out from a start key in the direction asked for, up to a limit, with a filter that runs after
/// the read. Items are given in their order, so what the order is comes from the test and never from the code under test.
/// </summary>
/// <remarks>Like DynamoDB it does not look ahead: a response that ends at its limit has a next key, even at the last item of a partition.</remarks>
internal sealed class FanOutWire
{
    private readonly Dictionary<string, List<Stored>> _partitions = [with(StringComparer.Ordinal)];

    /// <summary>Gets or sets the most items per response, which stands for the 1 MB of DynamoDB. A response that ends here always has a next key.</summary>
    public int ItemsPerResponse { get; set; } = int.MaxValue;

    /// <summary>Gets the partitions in the order they were read, one entry per response.</summary>
    public List<string> Reads { get; } = [];

    /// <summary>Adds items to the end of a partition.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="sortKeys">The sort keys in the order of the partition, as DynamoDB JSON, or as strings when they do not start with a brace.</param>
    /// <returns>This object.</returns>
    public FanOutWire Put(string partition, params string[] sortKeys)
    {
        foreach (var sortKey in sortKeys)
        {
            Put(partition, sortKey, passes: true);
        }

        return this;
    }

    /// <summary>Adds an item to the end of a partition.</summary>
    /// <param name="partition">The partition.</param>
    /// <param name="sortKey">The sort key.</param>
    /// <param name="passes">Whether the item passes a filter, if the request has one.</param>
    /// <returns>This object.</returns>
    public FanOutWire Put(string partition, string sortKey, bool passes)
    {
        if (!_partitions.TryGetValue(partition, out var items))
        {
            items = [];
            _partitions.Add(partition, items);
        }

        items.Add(new Stored(sortKey.StartsWith('{', StringComparison.Ordinal) ? sortKey : "{\"S\":\"" + sortKey + "\"}", passes));
        return this;
    }

    /// <summary>Answers a <c>Query</c>.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    public StubResponse Answer(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        var body = document.RootElement;
        var partition = body.GetProperty("ExpressionAttributeValues").GetProperty(":pk").GetProperty("S").GetString()!;
        var forward = !body.TryGetProperty("ScanIndexForward", out var direction) || direction.GetBoolean();
        var items = _partitions.TryGetValue(partition, out var stored) ? stored : [];
        var order = forward ? items : [.. Enumerable.Reverse(items)];
        var start = 0;
        if (body.TryGetProperty("ExclusiveStartKey", out var key))
        {
            var after = Normal(key.GetProperty("sk").GetRawText());
            var found = order.FindIndex(item => Normal(item.SortKey) == after);
            if (found < 0 || key.GetProperty("pk").GetProperty("S").GetString() != partition)
            {
                return StubResponse.DynamoDbError("ValidationException", "The provided starting key is invalid.");
            }

            start = found + 1;
        }

        var limit = body.TryGetProperty("Limit", out var requested) ? Math.Min(requested.GetInt32(), ItemsPerResponse) : ItemsPerResponse;
        var read = order.Skip(start).Take(limit).ToList();
        var passed = body.TryGetProperty("FilterExpression", out _) ? [.. read.Where(static item => item.Passes)] : read;
        lock (Reads)
        {
            Reads.Add(partition);
        }

        var response = new StringBuilder("{\"Items\":[").AppendJoin(',', passed.Select(item => item.Json(partition)));
        response.Append("],\"Count\":").Append(passed.Count.ToString(CultureInfo.InvariantCulture)).Append(",\"ScannedCount\":").Append(read.Count.ToString(CultureInfo.InvariantCulture));
        if (read.Count == limit)
        {
            response.Append(",\"LastEvaluatedKey\":{\"pk\":{\"S\":\"").Append(partition).Append("\"},\"sk\":").Append(read[^1].SortKey).Append('}');
        }

        return StubResponse.Json(response.Append('}').ToString());
    }

    // One value has many spellings in JSON.
    private static string Normal(string attributeValue)
    {
        var descriptor = JsonElement.Parse(attributeValue).EnumerateObject().Single();
        return descriptor.Name + ":" + descriptor.Value.GetString();
    }

    // The key comes last or between the other attributes, depending on the sort key: JSON has no order of attributes, and a response of DynamoDB shows that.
    private sealed record Stored(string SortKey, bool Passes)
    {
        public string Json(string partition) => (SortKey.Length % 3) switch
        {
            0 => "{\"pk\":{\"S\":\"" + partition + "\"},\"sk\":" + SortKey + ",\"from\":{\"S\":\"" + partition + "\"}}",
            1 => "{\"from\":{\"S\":\"" + partition + "\"},\"sk\":" + SortKey + ",\"pk\":{\"S\":\"" + partition + "\"}}",
            _ => "{\"sk\":" + SortKey + ",\"from\":{\"S\":\"" + partition + "\"},\"nested\":{\"M\":{\"sk\":{\"S\":\"no\"}}},\"pk\":{\"S\":\"" + partition + "\"}}",
        };
    }
}
