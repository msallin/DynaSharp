using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>What the item operations, the batches and the transactions read alike: the maps of placeholders, which a request without an expression must not have.</summary>
internal static class ItemRequests
{
    /// <summary>Rejects <c>ExpressionAttributeNames</c> and <c>ExpressionAttributeValues</c> in a request without an expression, as DynamoDB does.</summary>
    /// <param name="request">The request.</param>
    /// <exception cref="FakeError">A <c>ValidationException</c>.</exception>
    public static void ThrowIfMapsWithoutExpression(JsonElement request)
    {
        if (RequestJson.Optional(request, "ExpressionAttributeNames") is { } names)
        {
            _ = RequestJson.ReadStringMap(names, "ExpressionAttributeNames");
            throw FakeError.Validation("ExpressionAttributeNames can only be specified when using expressions");
        }

        if (RequestJson.Optional(request, "ExpressionAttributeValues") is { } values)
        {
            _ = RequestJson.ReadItem(values, "ExpressionAttributeValues");
            throw FakeError.Validation("ExpressionAttributeValues can only be specified when using expressions");
        }
    }
}
