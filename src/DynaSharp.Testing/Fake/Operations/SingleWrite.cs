using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The options <c>PutItem</c> and <c>DeleteItem</c> share: the condition and its maps, what to return, the consumed capacity and the item collection metrics. The fake writes the
/// item unconditionally and returns nothing, so each of these with a value that would change the answer is <c>UnsupportedByFakeException</c>, checked before the write.
/// </summary>
internal static class SingleWrite
{
    /// <summary>Checks the options of a write before it changes anything.</summary>
    /// <param name="request">The request.</param>
    /// <param name="operation">The operation.</param>
    /// <exception cref="FakeError">An <c>UnsupportedByFakeException</c> for an option with an effect, a <c>ValidationException</c> for maps without an expression.</exception>
    public static void ThrowIfUnsupported(JsonElement request, string operation)
    {
        ItemRequests.ThrowIfPresent(request, "ConditionExpression", operation);
        ItemRequests.ThrowIfNotNone(request, "ReturnValues", WireNames.ReturnValues, operation);
        ItemRequests.ThrowIfNotNone(request, "ReturnConsumedCapacity", WireNames.ReturnConsumedCapacities, operation);
        ItemRequests.ThrowIfNotNone(request, "ReturnItemCollectionMetrics", WireNames.ReturnItemCollectionMetricsValues, operation);

        // Without a condition, ALL_OLD on a failed condition has nothing to apply to, as on DynamoDB; its spelling is still checked.
        if (RequestJson.Optional(request, "ReturnValuesOnConditionCheckFailure") is { } onFailure)
        {
            _ = RequestJson.ReadEnum(onFailure, "ReturnValuesOnConditionCheckFailure", WireNames.ReturnValuesOnConditionCheckFailures, noneIsDefault: true);
        }

        ItemRequests.ThrowIfMapsWithoutExpression(request);
    }
}
