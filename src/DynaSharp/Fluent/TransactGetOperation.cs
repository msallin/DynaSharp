using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// <c>TransactGetItems</c> as a command runs it. The response is copied to an array of its own, which the result owns: the buffer the pipeline read it into
/// goes back to the pool when the call ends, and a result can live on long after that. Only where each entry starts is found here.
/// </summary>
internal readonly struct TransactGetOperation : IActionOperation<TransactGetResult>
{
    public static string Name => TransactGetItemsOperation.Name;

    public static bool EndsCommand => true;

    public static ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers) => state.Finish();

    public static TransactGetResult ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state)
    {
        var response = utf8Json.ToArray();
        var entries = new List<Range>(state.Actions.Count);
        IReadOnlyList<ConsumedCapacity> capacity = [];
        var reader = new DynamoDbReader(response);
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Responses"u8))
            {
                FindEntries(ref reader, entries);
            }
            else if (reader.AttributeNameEquals("ConsumedCapacity"u8))
            {
                capacity = OperationJson.ReadConsumedCapacityList(ref reader);
            }
            else
            {
                OperationJson.SkipValue(ref reader);
            }
        }

        return new TransactGetResult(response, entries, capacity);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(TransactGetResult response) => response.ConsumedCapacity;

    // "Responses":[{"Item":{...}},{},null]: an entry per get, in order.
    private static void FindEntries(ref DynamoDbReader reader, List<Range> entries)
    {
        OperationJson.ReadStartArray(ref reader);
        ref var json = ref reader.Json;
        while (true)
        {
            reader.Advance();
            if (json.TokenType == JsonTokenType.EndArray)
            {
                return;
            }

            if (json.TokenType is not (JsonTokenType.StartObject or JsonTokenType.Null))
            {
                throw new JsonException($"Expected an item response or the end of the array but found {json.TokenType} at byte {json.TokenStartIndex}.");
            }

            var start = (int)json.TokenStartIndex;
            json.Skip();
            entries.Add(start..(int)json.BytesConsumed);
        }
    }
}
