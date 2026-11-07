using DynaSharp.Serialization;

namespace DynaSharp.Operations;

/// <summary>
/// The response of <c>TransactGetItems</c>, read in one place for both APIs. <c>Responses</c> goes to the reader the caller gives: a command locates each entry and reads it when it is asked, and the
/// request API parses them.
/// </summary>
/// <param name="ConsumedCapacity">The consumed capacity per table, empty if not requested.</param>
internal readonly record struct TransactGetEnvelope(IReadOnlyList<ConsumedCapacity> ConsumedCapacity)
{
    /// <summary>Reads the response object; members that are not known are skipped.</summary>
    /// <typeparam name="TResponses">The reader of <c>Responses</c>.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="responses">The reader of the entries, which keeps them. It is not called for a response without the member.</param>
    /// <returns>The members both APIs read alike.</returns>
    public static TransactGetEnvelope Read<TResponses>(ref DynamoDbReader reader, ref TResponses responses)
        where TResponses : struct, IResponseMember
    {
        IReadOnlyList<ConsumedCapacity> capacity = [];
        reader.ReadStartItem();
        while (reader.TryReadAttributeName())
        {
            if (reader.AttributeNameEquals("Responses"u8))
            {
                responses.Read(ref reader);
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

        return new TransactGetEnvelope(capacity);
    }
}
