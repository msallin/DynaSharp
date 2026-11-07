using System.Diagnostics;
using System.Text.Json;
using DynaSharp.Errors;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;
using DynaSharp.Telemetry;

namespace DynaSharp.Fluent;

/// <summary>What the responses of <c>PutItem</c>, <c>UpdateItem</c> and <c>DeleteItem</c> share, read in one place.</summary>
internal static class WriteResponse
{
    private const string ConditionalCheckFailedCode = "ConditionalCheckFailedException";

    /// <summary>Reads the response of a write.</summary>
    /// <typeparam name="T">How the returned attributes are read.</typeparam>
    /// <param name="reader">The reader, positioned before the response object.</param>
    /// <param name="state">The state of the command.</param>
    /// <returns>The result.</returns>
    public static WriteResult<T> Read<T>(ref DynamoDbReader reader, CommandState state)
        where T : IDynamoDbDocument<T>
    {
        var envelope = WriteEnvelope<T>.Read(ref reader);
        state.ResponseCapacity = envelope.ConsumedCapacity;
        return state.ReturnsWrittenItem
            ? new WriteResult<T>(state.ReadWrittenItem<T>(), hasItem: true, envelope.ConsumedCapacity, envelope.ItemCollectionMetrics, state.Write.StoredVersion, conditionMet: true)
            : new WriteResult<T>(envelope.Attributes, envelope.HasAttributes, envelope.ConsumedCapacity, envelope.ItemCollectionMetrics, state.Write.StoredVersion, conditionMet: true);
    }

    /// <summary>
    /// Reads the error response of a write that <c>TrySendAsync</c> sent, when it is a failed condition: the result holds the item the condition saw, if <c>ReturnOldOnFailure()</c> asked for
    /// it, and nothing else, because the write stored nothing. Any other error, a body that is not JSON, or a write that <c>SendAsync</c> sent, is left to the pipeline to throw.
    /// </summary>
    /// <typeparam name="T">How the item is read.</typeparam>
    /// <param name="utf8Json">The body of the error response: <c>{"__type":"com.amazonaws.dynamodb.v20120810#ConditionalCheckFailedException","Item":{...},"message":"..."}</c>.</param>
    /// <param name="state">The state of the command.</param>
    /// <param name="result">The result, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for a failed condition of a write that takes one as an answer.</returns>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    public static bool TryReadFailure<T>(ReadOnlySpan<byte> utf8Json, CommandState state, out WriteResult<T> result)
        where T : IDynamoDbDocument<T>
    {
        result = default;
        if (!state.ToleratesFailedCondition)
        {
            return false;
        }

        string? type = null;
        var item = default(T);
        var hasItem = false;
        try
        {
            var reader = new DynamoDbReader(utf8Json);
            reader.ReadStartItem();
            while (reader.TryReadAttributeName())
            {
                if (reader.AttributeNameEquals("__type"u8))
                {
                    type = OperationJson.ReadString(ref reader);
                }
                else if (reader.AttributeNameEquals("Item"u8))
                {
                    reader.ReadStartItem();
                    item = T.ReadAttributes(ref reader);
                    hasItem = true;
                }
                else
                {
                    OperationJson.SkipValue(ref reader);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // The pipeline maps the body again and reports what it is.
            return false;
        }

        if (ErrorMapper.ExtractCode(type) != ConditionalCheckFailedCode)
        {
            return false;
        }

        result = new WriteResult<T>(item, hasItem, consumedCapacity: null, itemCollectionMetrics: null, version: null, conditionMet: false);
        return true;
    }

    /// <summary>Tags the span of a write whose condition failed and that took it as an answer, which is all that tells it from a write that went through.</summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="activity">The span, with all data requested.</param>
    /// <param name="response">The response.</param>
    public static void SetResponseTags<T>(Activity activity, WriteResult<T> response)
    {
        if (!response.ConditionMet)
        {
            activity.SetTag(TelemetryNames.ConditionMet, false);
        }
    }
}
