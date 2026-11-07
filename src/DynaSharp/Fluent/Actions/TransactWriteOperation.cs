using DynaSharp.Http;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary><c>TransactWriteItems</c> as a command runs it.</summary>
internal readonly struct TransactWriteOperation : IActionOperation<TransactWriteResult>
{
    // The length of a GUID in the "D" format, which is what fits the 36 characters that DynamoDB allows for the token.
    private const int GuidLength = 36;

    public static string Name => TransactWriteItemsOperation.Name;

    public static bool EndsCommand => true;

    /// <summary>
    /// Ends the transaction: the last action and its token. The token goes into the body here, once per send, and the pipeline sends those
    /// bytes on every attempt, so a retry carries the same token. That is what makes a retried transaction idempotent.
    /// </summary>
    /// <param name="state">The state of the command.</param>
    /// <exception cref="InvalidOperationException">The transaction has no action. A loop gets here with nothing to add, and only a round trip would tell otherwise.</exception>
    public static void End(CommandState state)
    {
        if (state.Actions.Count == 0)
        {
            throw new InvalidOperationException("This transaction has no action, and DynamoDB rejects one without. If a loop may add nothing, skip the send.");
        }

        Transaction.EndActions(state);
        var body = state.Body;
        body.Append(",\"ClientRequestToken\":\""u8);
        if (state.Actions.Token is { } token)
        {
            WireJson.AppendEncoded(body, token);
        }
        else
        {
            Span<byte> generated = stackalloc byte[GuidLength];
            _ = Guid.NewGuid().TryFormat(generated, out var written);
            body.Append(generated[..written]);
        }

        body.Append((byte)'"');
    }

    public static ArraySegment<byte> WriteBody(CommandState state, CallBuffers buffers) => state.Finish();

    public static TransactWriteResult ReadResponse(ReadOnlySpan<byte> utf8Json, CommandState state)
    {
        var reader = new DynamoDbReader(utf8Json);
        var envelope = TransactWriteEnvelope.Read(ref reader);
        return new TransactWriteResult(envelope.ConsumedCapacity, envelope.ItemCollectionMetrics);
    }

    public static IReadOnlyList<ConsumedCapacity> GetConsumedCapacity(TransactWriteResult response) => response.ConsumedCapacity;
}
