using System.Text.Json;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// A <c>Scan</c>: every partition of the table or an index, or with <c>Segment</c> and <c>TotalSegments</c> those of one segment, cut into pages by <see cref="PageBuilder"/>. The
/// segments split the partitions by <see cref="Segments"/>, so each item is in exactly one of them, and an <c>ExclusiveStartKey</c> must belong to the segment it goes on in.
/// </summary>
internal static class Scan
{
    /// <summary>The operation.</summary>
    public const string Name = "Scan";

    /// <summary>The most segments a parallel scan has.</summary>
    public const int MaxTotalSegments = 1_000_000;

    /// <summary>Answers the operation.</summary>
    /// <param name="db">The state.</param>
    /// <param name="request">The body of the request.</param>
    /// <param name="response">The writer, inside the object of the response.</param>
    public static void Handle(FakeDatabase db, JsonElement request, Utf8JsonWriter response)
    {
        ItemRequests.ThrowIfLegacyWithExpressions(request);
        RequestJson.Members(
            request,
            Name,
            "TableName",
            "IndexName",
            "FilterExpression",
            "ProjectionExpression",
            "ExpressionAttributeNames",
            "ExpressionAttributeValues",
            "Select",
            "Limit",
            "ConsistentRead",
            "Segment",
            "TotalSegments",
            "ExclusiveStartKey",
            "ReturnConsumedCapacity");
        var read = ReadRequest.Read(db, request);
        var (segment, totalSegments) = ReadSegments(request);
        var options = read.ParseOptions(request, queried: null);
        var start = PageBuilder.ReadStartKey(request, read.Table, read.Index);
        if (start is not null && Segments.Of(start[read.KeySchema.PartitionKeyName], totalSegments) != segment)
        {
            throw FakeError.Validation("The provided Exclusive start key does not map to the provided Segment and TotalSegments values.");
        }

        read.Write(response, PageBuilder.Build(read.Table.ScanAll(read.Index, start, segment, totalSegments), options));
    }

    // Segment and TotalSegments come together or not at all: 0 of 1 is a scan without segments.
    private static (int Segment, int TotalSegments) ReadSegments(JsonElement request)
    {
        int? segment = RequestJson.Optional(request, "Segment") is { } segmentValue ? RequestJson.ReadInt(segmentValue, "Segment") : null;
        int? total = RequestJson.Optional(request, "TotalSegments") is { } totalValue ? RequestJson.ReadInt(totalValue, "TotalSegments") : null;
        if (segment is null && total is null)
        {
            return (0, 1);
        }

        if (total is not { } totalSegments)
        {
            throw FakeError.Validation("The TotalSegments parameter is required but was not present in the request when Segment parameter is present");
        }

        if (segment is not { } index)
        {
            throw FakeError.Validation("The Segment parameter is required but was not present in the request when parameter TotalSegments is present");
        }

        if (totalSegments is < 1 or > MaxTotalSegments)
        {
            var bound = totalSegments < 1 ? "greater than or equal to 1" : $"less than or equal to {MaxTotalSegments}";
            throw FakeError.Enveloped(RequestModel.Violation("TotalSegments", $"Member must have value {bound}"));
        }

        if (index < 0)
        {
            throw FakeError.Enveloped(RequestModel.Violation("Segment", "Member must have value greater than or equal to 0"));
        }

        return index < totalSegments
            ? (index, totalSegments)
            : throw FakeError.Validation("The Segment parameter is zero-based and must be less than parameter TotalSegments");
    }
}
