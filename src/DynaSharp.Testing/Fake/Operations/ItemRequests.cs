using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>What the item operations, the batches and the transactions read alike: the maps of placeholders, which a request without an expression must not have.</summary>
internal static class ItemRequests
{
    private static readonly string[] LegacyMembers = ["AttributesToGet", "AttributeUpdates", "ConditionalOperator", "Expected", "KeyConditions", "QueryFilter", "ScanFilter"];

    private static readonly string[] ExpressionMembers = ["ProjectionExpression", "UpdateExpression", "ConditionExpression", "KeyConditionExpression", "FilterExpression"];

    /// <summary>Rejects <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c> in a request without an expression, as DynamoDB does.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static void ThrowIfMapsWithoutExpression(JsonElement request)
    {
        if (RequestJson.Optional(request, "ExpressionAttributeNames") is { } names)
        {
            _ = RequestJson.ReadStringMap(names, "ExpressionAttributeNames");
            throw FakeError.Enveloped("ExpressionAttributeNames can only be specified when using expressions");
        }

        if (RequestJson.Optional(request, "ExpressionAttributeValues") is { } values)
        {
            _ = RequestJson.ReadItem(values, "ExpressionAttributeValues");
            throw FakeError.Enveloped("ExpressionAttributeValues can only be specified when using expressions");
        }
    }

    /// <summary>Rejects a request that mixes the legacy parameters (<c>Expected</c>, <c>AttributesToGet</c>, <c>KeyConditions</c> and the like) with expressions, as DynamoDB does, before it looks at anything else. A legacy parameter alone is not rejected here: the fake does not implement it.</summary>
    /// <param name="request">The request. A body that is not a JSON object is left to the parsing that follows.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c> that names both groups, each in the order DynamoDB lists them.</exception>
    public static void ThrowIfLegacyWithExpressions(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var legacy = Present(request, LegacyMembers);
        var expressions = Present(request, ExpressionMembers);
        if (legacy.Count > 0 && expressions.Count > 0)
        {
            throw FakeError.Validation($"Can not use both expression and non-expression parameters in the same request: Non-expression parameters: {{{string.Join(", ", legacy)}}} Expression parameters: {{{string.Join(", ", expressions)}}}");
        }
    }

    private static List<string> Present(JsonElement request, string[] names)
    {
        var present = new List<string>();
        foreach (var name in names)
        {
            if (RequestJson.Optional(request, name) is not null)
            {
                present.Add(name);
            }
        }

        return present;
    }
}
