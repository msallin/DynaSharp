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
        var entries = new Entries(new List<Range>(state.Actions.Count));
        var reader = new DynamoDbReader(response);
        var envelope = TransactGetEnvelope.Read(ref reader, ref entries);
        return new TransactGetResult(response, entries.Ranges, envelope.ConsumedCapacity);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(TransactGetResult response) => response.ConsumedCapacity;

    // "Responses":[{"Item":{...}},{},null]: an entry per get, in order.
    private readonly struct Entries(List<Range> ranges) : IResponseMember
    {
        public List<Range> Ranges => ranges;

        public void Read(ref DynamoDbReader reader)
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
                ranges.Add(start..(int)json.BytesConsumed);
            }
        }
    }
}
