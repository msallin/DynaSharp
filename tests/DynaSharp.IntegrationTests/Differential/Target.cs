using System.Globalization;
using System.Text.Json.Nodes;
using DynaSharp.IntegrationTests.Backends;

namespace DynaSharp.IntegrationTests.Differential;

/// <summary>A backend a sequence runs on, through its raw client, so that every member of a request reaches it as written and every member of the answer is compared.</summary>
/// <param name="Backend">The backend.</param>
/// <param name="Raw">Its raw client.</param>
internal sealed record Target(Backend Backend, RawDynamoDb Raw)
{
    private const int MaxPages = 60;

    // An item larger than this may end a page at 1 MB, and where depends on which items come first: then the number of pages of an unordered read is not compared.
    private const int LargeItemLength = 64 * 1024;

    /// <summary>Creates the two tables of a sequence and waits until they are active.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="main">The name of the main table.</param>
    /// <param name="other">The name of the other table.</param>
    /// <returns>A task that completes when both tables are active.</returns>
    public async Task CreateTablesAsync(Schema schema, string main, string other)
    {
        (await Raw.SendAsync("CreateTable", schema.CreateMain(main))).EnsureSuccess();
        (await Raw.SendAsync("CreateTable", Schema.CreateOther(other))).EnsureSuccess();
        foreach (var name in new[] { main, other })
        {
            for (var attempt = 0; ; attempt++)
            {
                var description = JsonNode.Parse((await Raw.SendAsync("DescribeTable", new JsonObject { ["TableName"] = name }.ToJsonString())).EnsureSuccess().Body)!["Table"]!;
                var indexes = description["GlobalSecondaryIndexes"]?.AsArray() ?? [];
                if (description["TableStatus"]!.GetValue<string>() == "ACTIVE" && indexes.All(static index => index!["IndexStatus"]!.GetValue<string>() == "ACTIVE"))
                {
                    break;
                }

                if (attempt > 400)
                {
                    throw new TimeoutException($"{Backend}: table {name} did not become active.");
                }

                await Task.Delay(25);
            }
        }
    }

    /// <summary>Deletes the tables of a sequence; a failure is ignored, since every table has a name of its own.</summary>
    /// <param name="names">The tables.</param>
    /// <returns>A task that completes when the deletes are answered.</returns>
    public async Task DeleteTablesAsync(params string[] names)
    {
        foreach (var name in names)
        {
            try
            {
                await Raw.SendAsync("DeleteTable", new JsonObject { ["TableName"] = name }.ToJsonString());
            }
            catch (HttpRequestException)
            {
                // The emulator is gone; nothing is left to clean up.
            }
        }
    }

    /// <summary>Sends an operation, all of its pages and segments where it reads them, and reads the outcome.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="names">Replaces the placeholders of the request, such as <see cref="Schema.Main"/>, with the names of the run.</param>
    /// <param name="placeholders">Replaces the names of the run in an answer with the placeholders, the reverse of <paramref name="names"/>.</param>
    /// <returns>The outcome.</returns>
    public async Task<Outcome> SendAsync(Operation operation, Func<string, string> names, Func<string, string> placeholders)
    {
        async Task<RawResponse> Send(JsonObject request)
        {
            var response = await Raw.SendAsync(operation.Name, names(request.ToJsonString()));
            return response with { Body = Unrequested(request, placeholders(response.Body)) };
        }

        return operation.Reading switch
        {
            Reading.Single => Outcome.Of(await Send(operation.Body), Normalizer(operation.Name)),
            Reading.Pages => await PagesAsync(operation, Send),
            _ => await UnorderedAsync(operation, Send),
        };
    }

    // DynamoDB Local reports ConsumedCapacity also when the request did not ask for it, which DynamoDB does not; the fake does not either.
    private static string Unrequested(JsonObject request, string body)
    {
        if (request["ReturnConsumedCapacity"]?.GetValue<string>() is "TOTAL" or "INDEXES" || !body.Contains("\"ConsumedCapacity\"", StringComparison.Ordinal))
        {
            return body;
        }

        var response = JsonNode.Parse(body)!.AsObject();
        response.Remove("ConsumedCapacity");
        return response.ToJsonString();
    }

    private static Func<JsonObject, JsonObject>? Normalizer(string operation) => operation switch
    {
        "BatchGetItem" => NormalizeBatchGet,
        "BatchWriteItem" => NormalizeBatchWrite,
        _ => null,
    };

    // The items of a table in a BatchGetItem come in any order.
    private static JsonObject NormalizeBatchGet(JsonObject response)
    {
        DropEmpty(response, "UnprocessedKeys");
        if (response["Responses"] is JsonObject tables)
        {
            foreach (var (_, items) in tables)
            {
                SortItems(items!.AsArray());
            }
        }

        return response;
    }

    private static JsonObject NormalizeBatchWrite(JsonObject response)
    {
        DropEmpty(response, "UnprocessedItems");
        return response;
    }

    // DynamoDB may leave UnprocessedKeys and UnprocessedItems out or send them empty; both mean nothing is left.
    private static void DropEmpty(JsonObject response, string member)
    {
        if (response[member] is JsonObject { Count: 0 })
        {
            response.Remove(member);
        }
    }

    private static void SortItems(JsonArray items)
    {
        var sorted = items.Select(static item => Canonical.Of(item)).Order(StringComparer.Ordinal).ToList();
        items.Clear();
        foreach (var item in sorted)
        {
            items.Add(JsonNode.Parse(item));
        }
    }

    private static async Task<Outcome> PagesAsync(Operation operation, Func<JsonObject, Task<RawResponse>> send)
    {
        var pages = new JsonArray();
        var capacities = new JsonArray();
        var request = (JsonObject)operation.Body.DeepClone();
        for (var page = 0; page < MaxPages; page++)
        {
            var response = await send(request);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return Outcome.Of(response);
            }

            var body = JsonNode.Parse(response.Body)!.AsObject();
            if (body["ConsumedCapacity"] is { } consumed)
            {
                capacities.Add(consumed.DeepClone());
                body.Remove("ConsumedCapacity");
            }

            pages.Add(body.DeepClone());
            if (body["LastEvaluatedKey"] is not { } next)
            {
                break;
            }

            request["ExclusiveStartKey"] = next.DeepClone();
        }

        return Outcome.Success(new JsonObject { ["Pages"] = pages }, capacities.Count > 0 ? capacities : null);
    }

    private static async Task<Outcome> UnorderedAsync(Operation operation, Func<JsonObject, Task<RawResponse>> send)
    {
        var segments = operation.Body["TotalSegments"]?.GetValue<int>();
        var items = new JsonArray();
        long count = 0, scanned = 0;
        var pages = 0;
        var ordered = true;
        var large = false;
        JsonNode? capacity = null;
        for (var segment = 0; segment < (segments ?? 1); segment++)
        {
            var request = (JsonObject)operation.Body.DeepClone();
            if (segments is not null)
            {
                request["Segment"] = segment;
            }

            for (var page = 0; page < MaxPages; page++)
            {
                var response = await send(request);
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    return Outcome.Of(response);
                }

                var body = JsonNode.Parse(response.Body)!.AsObject();
                pages++;
                count += body["Count"]?.GetValue<long>() ?? 0;
                scanned += body["ScannedCount"]?.GetValue<long>() ?? 0;
                capacity = body["ConsumedCapacity"]?.DeepClone();
                var pageItems = body["Items"]?.AsArray() ?? [];
                ordered &= InIndexOrder(operation, pageItems);
                foreach (var item in pageItems)
                {
                    large |= item!.ToJsonString().Length > LargeItemLength;
                    items.Add(item.DeepClone());
                }

                if (body["LastEvaluatedKey"] is not { } next)
                {
                    break;
                }

                request["ExclusiveStartKey"] = next.DeepClone();
            }
        }

        SortItems(items);
        var result = new JsonObject { ["Items"] = items, ["Count"] = count, ["ScannedCount"] = scanned, ["InIndexOrder"] = ordered };

        // How many pages a limit makes does not depend on the order; a cut at 1 MB does. The capacity of a single page is that of all its items.
        if (operation.Body["Limit"] is not null && segments is null && !large)
        {
            result["Pages"] = pages;
        }

        return Outcome.Success(result, pages == 1 ? capacity : null);
    }

    // A query of the index returns its items in the order of the index sort key, the reverse with ScanIndexForward false. Items of one sort key value come in any order.
    private static bool InIndexOrder(Operation operation, JsonArray items)
    {
        if (operation.Name != "Query")
        {
            return true;
        }

        var forward = operation.Body["ScanIndexForward"]?.GetValue<bool>() ?? true;
        decimal? previous = null;
        foreach (var item in items)
        {
            if (item?["gsk"]?["N"]?.GetValue<string>() is not { } text)
            {
                continue;
            }

            var value = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (previous is { } last && (forward ? value < last : value > last))
            {
                return false;
            }

            previous = value;
        }

        return true;
    }
}
