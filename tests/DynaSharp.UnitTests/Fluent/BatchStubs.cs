using System.Text.Json;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>What DynamoDB says to a batch, and what a test wants to know about a request of one.</summary>
internal static class BatchStubs
{
    /// <summary>The name of the attribute the items of these tests hold their number in.</summary>
    public const string Index = "index";

    /// <summary>Makes an item of the numbered kind.</summary>
    /// <param name="index">The number.</param>
    /// <returns>The item.</returns>
    public static Item Numbered(int index) => new() { ["pk"] = "item-" + index, [Index] = index };

    /// <summary>Takes nothing of a BatchWriteItem: the answer of a table that stays throttled.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    public static StubResponse LeaveAllWrites(RecordedRequest request) => StubResponse.Json("{\"UnprocessedItems\":" + RequestItems(request) + "}");

    /// <summary>Takes nothing of a BatchGetItem.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    public static StubResponse LeaveAllKeys(RecordedRequest request) => StubResponse.Json("{\"UnprocessedKeys\":" + RequestItems(request) + "}");

    /// <summary>Gets the <c>RequestItems</c> of a batch as JSON.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The JSON.</returns>
    public static string RequestItems(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        return document.RootElement.GetProperty("RequestItems").GetRawText();
    }

    /// <summary>Counts the writes of a BatchWriteItem, on all tables.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The number.</returns>
    public static int CountWrites(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        return document.RootElement.GetProperty("RequestItems").EnumerateObject().Sum(static table => table.Value.GetArrayLength());
    }

    /// <summary>Counts the keys of a BatchGetItem, on all tables.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The number.</returns>
    public static int CountKeys(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        return document.RootElement.GetProperty("RequestItems").EnumerateObject().Sum(static table => table.Value.GetProperty("Keys").GetArrayLength());
    }

    /// <summary>Answers a BatchGetItem with an item for each key, with the key as its attributes.</summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    public static StubResponse AnswerKeys(RecordedRequest request)
    {
        using var document = JsonDocument.Parse(request.BodyText);
        var tables = document.RootElement.GetProperty("RequestItems").EnumerateObject()
            .Select(static table => $"\"{table.Name}\":[{string.Join(',', table.Value.GetProperty("Keys").EnumerateArray().Select(static key => key.GetRawText()))}]");
        return StubResponse.Json("{\"Responses\":{" + string.Join(',', tables) + "}}");
    }
}
