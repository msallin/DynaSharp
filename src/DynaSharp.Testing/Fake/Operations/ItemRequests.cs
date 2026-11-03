using System.Text.Json;
using DynaSharp.Operations;

namespace DynaSharp.Testing.Fake;

/// <summary>What <c>GetItem</c>, <c>PutItem</c> and <c>DeleteItem</c> read alike: the table, and the members they answer as unsupported or reject without an expression.</summary>
internal static class ItemRequests
{
    /// <summary>Answers a member the fake does not implement for the operation as unsupported.</summary>
    /// <param name="request">The request.</param>
    /// <param name="member">The member.</param>
    /// <param name="operation">The operation.</param>
    /// <exception cref="FakeError">An <c>UnsupportedByFakeException</c> if the request has the member.</exception>
    public static void ThrowIfPresent(JsonElement request, string member, string operation)
    {
        if (RequestJson.Optional(request, member) is not null)
        {
            throw FakeError.Unsupported($"the member {member}", operation);
        }
    }

    /// <summary>Answers a value of an enum member other than <c>NONE</c> as unsupported, after checking that it is a value DynamoDB knows.</summary>
    /// <typeparam name="TEnum">The enum, whose zero member is <c>None</c>.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="member">The member.</param>
    /// <param name="names">The spellings.</param>
    /// <param name="operation">The operation.</param>
    /// <exception cref="FakeError">An <c>UnsupportedByFakeException</c>.</exception>
    public static void ThrowIfNotNone<TEnum>(JsonElement request, string member, EnumWireNames<TEnum> names, string operation)
        where TEnum : struct, Enum
    {
        if (RequestJson.Optional(request, member) is { } value
            && !EqualityComparer<TEnum>.Default.Equals(RequestJson.ReadEnum(value, member, names, noneIsDefault: true), default))
        {
            throw FakeError.Unsupported($"{member} {value.GetString()}", operation);
        }
    }

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
