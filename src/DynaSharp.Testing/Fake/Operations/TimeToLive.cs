using System.Text.Json;
using DynaSharp.Operations;
using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// <c>DescribeTimeToLive</c>, <c>UpdateTimeToLive</c> and the sweep behind <see cref="FakeDynamoDb.ExpireItems"/>. Time to live is <c>ENABLED</c> or <c>DISABLED</c> at once, never
/// <c>ENABLING</c>, and turning it on twice is a <c>ValidationException</c>, as on DynamoDB.
/// </summary>
/// <remarks>
/// Nothing expires by itself. DynamoDB deletes expired items in the background, within days, and its reads may return them until then, so a test that depends on the deletion calls
/// the sweep, which deletes every item whose attribute of time to live is a number of epoch seconds at or before the clock of the fake and not more than five years before it,
/// because DynamoDB does not delete an item whose expiry is older than that. Each delete is a <c>REMOVE</c> record on the stream of its table, with the service as its
/// <c>userIdentity</c>, as DynamoDB writes it.
/// </remarks>
internal static class TimeToLive
{
    /// <summary>The operation.</summary>
    public const string DescribeName = "DescribeTimeToLive";

    /// <summary>The operation.</summary>
    public const string UpdateName = "UpdateTimeToLive";

    // DynamoDB does not delete an item whose expiry lies more than five years in the past.
    private const int MaxAgeYears = 5;

    /// <summary>Answers <c>DescribeTimeToLive</c>: <c>{"TimeToLiveDescription":{"TimeToLiveStatus":"ENABLED","AttributeName":"expires"}}</c>, or <c>DISABLED</c> without a name.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleDescribe(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, DescribeName, "TableName");
        var table = db.DescribeTable(TableOperations.ReadTableName(request));
        response.WriteStartObject("TimeToLiveDescription"u8);
        WireNames.TimeToLiveStatuses.Write(response, "TimeToLiveStatus"u8, table.TimeToLiveAttribute is null ? TimeToLiveStatus.Disabled : TimeToLiveStatus.Enabled);
        OperationJson.WriteStringIfSet(response, "AttributeName"u8, table.TimeToLiveAttribute);
        response.WriteEndObject();
    }

    /// <summary>Answers <c>UpdateTimeToLive</c>, which repeats the specification of the request.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void HandleUpdate(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        RequestJson.Members(request, UpdateName, "TableName", "TimeToLiveSpecification");
        var tableName = TableOperations.ReadTableName(request);
        var specification = RequestJson.Required(request, "TimeToLiveSpecification");
        RequestJson.Members(specification, "TimeToLiveSpecification of " + UpdateName, "Enabled", "AttributeName");
        var enabled = RequestJson.ReadBool(RequestJson.Required(specification, "Enabled"), "Enabled");
        var attribute = RequestJson.ReadString(RequestJson.Required(specification, "AttributeName"), "AttributeName");
        if (attribute.Length is < 1 or > 255)
        {
            throw FakeError.Validation($"1 validation error detected: Value '{attribute}' at 'timeToLiveSpecification.attributeName' failed to satisfy constraint: Member must have length "
                + (attribute.Length < 1 ? "greater than or equal to 1" : "less than or equal to 255"));
        }

        var table = db.DescribeTable(tableName);
        if (enabled && table.TimeToLiveAttribute is not null)
        {
            throw FakeError.Validation("TimeToLive is already enabled");
        }

        if (!enabled && table.TimeToLiveAttribute is null)
        {
            throw FakeError.Validation("TimeToLive is already disabled");
        }

        if (!enabled && table.TimeToLiveAttribute != attribute)
        {
            throw FakeError.Validation($"TimeToLive is active on a different AttributeName: current AttributeName is {table.TimeToLiveAttribute}");
        }

        table.TimeToLiveAttribute = enabled ? attribute : null;
        response.WriteStartObject("TimeToLiveSpecification"u8);
        response.WriteBoolean("Enabled"u8, enabled);
        response.WriteString("AttributeName"u8, attribute);
        response.WriteEndObject();
    }

    /// <summary>Deletes every item that has expired: in a table with time to live on, whose attribute is a number at or before the clock, in epoch seconds, and not more than five years before it.</summary>
    /// <param name="db">The state.</param>
    /// <returns>The number of items deleted.</returns>
    public static int Expire(FakeDatabase db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var clock = db.Clock.GetUtcNow();
        var now = DecimalNumber.FromInt64(clock.ToUnixTimeSeconds());
        var oldest = DecimalNumber.FromInt64(clock.AddYears(-MaxAgeYears).ToUnixTimeSeconds());
        var expired = 0;
        foreach (var table in db.Tables.Values)
        {
            if (table.TimeToLiveAttribute is not { } attribute)
            {
                continue;
            }

            var keys = table.All()
                .Where(item => item.TryGetValue(attribute, out var value) && value.Type == AttributeType.Number && IsDue(DecimalNumber.Parse(value.AsNumber()), oldest, now))
                .Select(table.KeySchema.KeyOf)
                .ToList();
            foreach (var key in keys)
            {
                table.Remove(key);
            }

            expired += keys.Count;
        }

        db.CommitChanges(byTimeToLive: true);
        return expired;
    }

    private static bool IsDue(DecimalNumber expiry, DecimalNumber oldest, DecimalNumber now) => expiry >= oldest && expiry <= now;
}
