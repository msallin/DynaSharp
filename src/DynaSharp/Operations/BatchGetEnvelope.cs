using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>BatchGetItem</c>, read in one place for both APIs. <c>Responses</c> and <c>UnprocessedKeys</c> go to the readers the caller gives: a command reads the items into what it
/// returns and locates the keys, which the next request sends back as they are, and the request API parses both.
/// </summary>
/// <param name="ConsumedCapacity">The consumed capacity per table, empty if not requested.</param>
internal readonly record struct BatchGetEnvelope(IReadOnlyList<ConsumedCapacity> ConsumedCapacity)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <typeparam name="TResponses">The reader of <c>Responses</c>.</typeparam>
    /// <typeparam name="TUnprocessed">The reader of <c>UnprocessedKeys</c>.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="responses">The reader of the items, which keeps them. It is not called for a response without the member.</param>
    /// <param name="unprocessedKeys">The reader of what DynamoDB left, which keeps it. It is not called for a response without the member.</param>
    /// <returns>The members both APIs read alike.</returns>
    public static BatchGetEnvelope Read<TResponses, TUnprocessed>(ref DynamoDbReader reader, ref TResponses responses, ref TUnprocessed unprocessedKeys)
        where TResponses : struct, IResponseMember
        where TUnprocessed : struct, IResponseMember
    {
        IReadOnlyList<ConsumedCapacity> capacity = [];
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Responses"u8))
            {
                responses.Read(ref reader);
            }
            else if (reader.AttributeNameEquals("UnprocessedKeys"u8))
            {
                unprocessedKeys.Read(ref reader);
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

        return new BatchGetEnvelope(capacity);
    }
}
