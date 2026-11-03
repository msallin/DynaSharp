using System.Buffers;
using System.Collections.Frozen;
using System.Net;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Serialization;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The entry of the wire: from the <c>X-Amz-Target</c> and the body of a request to the status and the body of the answer, under the gate of the fake, as DynamoDB speaks it.
/// </summary>
/// <remarks>
/// <para>
/// The target is <c>DynamoDB_20120810.GetItem</c>, <c>DynamoDBStreams_20120810.GetRecords</c> or the bare name, which is looked up in DynamoDB first. An operation that is not
/// in the registry is <c>UnknownOperationException</c>, whatever the body. A <see cref="FakeError"/> becomes the error body; any other exception is a bug of the fake and goes
/// through, so that the test fails where it happened instead of reading an HTTP 500. The stream records a request staged are committed when it went through and dropped when it failed.
/// </para>
/// <para>
/// When adding an operation: a static class in <c>Fake/Operations</c> with <c>Name</c> and <c>Handle</c>, its line in <see cref="Handlers"/> (or <see cref="StreamsHandlers"/>) and out of
/// <see cref="NotImplemented"/>, its tests, and the lists of operations in <c>docs/testing.md</c> and the README of the package.
/// </para>
/// </remarks>
internal static class FakeWire
{
    /// <summary>The operations the client can send and the fake does not answer: none, every operation of the client has a handler.</summary>
    public static readonly FrozenSet<string> NotImplemented = [];

    private static readonly FrozenDictionary<string, OperationHandler> Handlers = new Dictionary<string, OperationHandler>(StringComparer.Ordinal)
    {
        [GetItem.Name] = GetItem.Handle,
        [PutItem.Name] = PutItem.Handle,
        [DeleteItem.Name] = DeleteItem.Handle,
        [UpdateItem.Name] = UpdateItem.Handle,
        [Query.Name] = Query.Handle,
        [Scan.Name] = Scan.Handle,
        [BatchGetItem.Name] = BatchGetItem.Handle,
        [BatchWriteItem.Name] = BatchWriteItem.Handle,
        [TransactGetItems.Name] = TransactGetItems.Handle,
        [TransactWriteItems.Name] = TransactWriteItems.Handle,
        [TableOperations.CreateTableName] = TableOperations.HandleCreateTable,
        [TableOperations.DescribeTableName] = TableOperations.HandleDescribeTable,
        [TableOperations.DeleteTableName] = TableOperations.HandleDeleteTable,
        [TableOperations.UpdateTableName] = TableOperations.HandleUpdateTable,
        [TableOperations.ListTablesName] = TableOperations.HandleListTables,
        [TimeToLive.DescribeName] = TimeToLive.HandleDescribe,
        [TimeToLive.UpdateName] = TimeToLive.HandleUpdate,
        [DescribeLimits.Name] = DescribeLimits.Handle,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, OperationHandler> StreamsHandlers = new Dictionary<string, OperationHandler>(StringComparer.Ordinal)
    {
        [StreamOperations.ListStreamsName] = StreamOperations.HandleListStreams,
        [StreamOperations.DescribeStreamName] = StreamOperations.HandleDescribeStream,
        [StreamOperations.GetShardIteratorName] = StreamOperations.HandleGetShardIterator,
        [StreamOperations.GetRecordsName] = StreamOperations.HandleGetRecords,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Tells whether the fake answers an operation of DynamoDB.</summary>
    /// <param name="operation">The bare name, <c>GetItem</c>.</param>
    /// <returns><see langword="true"/> if it has a handler.</returns>
    public static bool Implements(string operation) => Handlers.ContainsKey(operation) || StreamsHandlers.ContainsKey(operation);

    /// <summary>Answers a request.</summary>
    /// <param name="db">The state.</param>
    /// <param name="target">The value of <c>X-Amz-Target</c>, or a bare operation name, or <see langword="null"/>.</param>
    /// <param name="body">The UTF-8 body.</param>
    /// <returns>The answer.</returns>
    public static FakeAnswer Handle(FakeDatabase db, string? target, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(db);
        var requestId = Guid.NewGuid().ToString();
        try
        {
            var handler = Find(target);
            using var document = Parse(body);
            var buffer = new ArrayBufferWriter<byte>();
            using (var json = new Utf8JsonWriter(buffer, WireJson.WriterOptions))
            {
                json.WriteStartObject();
                lock (db.Gate)
                {
                    try
                    {
                        handler(db, document.RootElement, json);
                        db.CommitChanges();
                    }
                    finally
                    {
                        db.DiscardChanges();
                    }
                }

                json.WriteEndObject();
            }

            return new FakeAnswer(HttpStatusCode.OK, buffer.WrittenMemory, requestId);
        }
        catch (FakeError error)
        {
            return new FakeAnswer(error.Status, error.RenderBody(), requestId);
        }
    }

    private static OperationHandler Find(string? target)
    {
        if (target is null)
        {
            throw FakeError.UnknownOperation(null);
        }

        OperationHandler? handler;
        string operation;
        if (target.StartsWith(WireProtocol.TargetPrefix, StringComparison.Ordinal))
        {
            operation = target[WireProtocol.TargetPrefix.Length..];
            Handlers.TryGetValue(operation, out handler);
        }
        else if (target.StartsWith(WireProtocol.StreamsTargetPrefix, StringComparison.Ordinal))
        {
            operation = target[WireProtocol.StreamsTargetPrefix.Length..];
            StreamsHandlers.TryGetValue(operation, out handler);
        }
        else
        {
            operation = target;
            handler = Handlers.GetValueOrDefault(target) ?? StreamsHandlers.GetValueOrDefault(target);
        }

        return handler ?? throw FakeError.UnknownOperation(operation);
    }

    private static JsonDocument Parse(ReadOnlySpan<byte> body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = WireJson.ReaderOptions.MaxDepth });
        }
        catch (JsonException exception)
        {
            throw FakeError.Serialization($"The request body is not JSON: {exception.Message}");
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw FakeError.Serialization("The request body must be a JSON object.");
        }

        return document;
    }
}
