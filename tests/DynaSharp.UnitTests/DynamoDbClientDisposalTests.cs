using DynaSharp.Requests;
using DynaSharp.Streams;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests;

/// <summary>What <see cref="DynamoDbClient.Dispose"/> does and what an operation does on a disposed client.</summary>
public sealed class DynamoDbClientDisposalTests
{
    private static readonly Dictionary<string, AttributeValue> Key = new() { ["pk"] = "user-1" };

    // Every operation of the client. The client's own check must fire in each of them: the HTTP client below would also throw an
    // ObjectDisposedException, but one that names the HTTP client and only after the request was serialized and signed.
    public static IEnumerable<DisposedCall<DynamoDbClient>> Operations()
    {
        yield return new("GetItemAsync", client => client.GetItemAsync(TestClient.GetItem()));
        yield return new("PutItemAsync", client => client.PutItemAsync(TestClient.PutItem()));
        yield return new("UpdateItemAsync", client => client.UpdateItemAsync(new UpdateItemRequest { TableName = "Users", Key = Key }));
        yield return new("DeleteItemAsync", client => client.DeleteItemAsync(new DeleteItemRequest { TableName = "Users", Key = Key }));
        yield return new("QueryAsync", client => client.QueryAsync(new QueryRequest { TableName = "Users", KeyConditionExpression = "pk = :pk" }));
        yield return new("ScanAsync", client => client.ScanAsync(new ScanRequest { TableName = "Users" }));
        yield return new("BatchGetItemAsync", client => client.BatchGetItemAsync(new BatchGetItemRequest { RequestItems = new Dictionary<string, KeysAndAttributes>() }));
        yield return new("BatchWriteItemAsync", client => client.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = new Dictionary<string, IReadOnlyList<WriteRequest>>() }));
        yield return new("TransactGetItemsAsync", client => client.TransactGetItemsAsync(new TransactGetItemsRequest { TransactItems = [] }));
        yield return new("TransactWriteItemsAsync", client => client.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = [] }));
        yield return new("DescribeTableAsync", client => client.DescribeTableAsync(TestClient.DescribeTable()));
        yield return new("CreateTableAsync", client => client.CreateTableAsync(TestClient.CreateTable()));
        yield return new("DeleteTableAsync", client => client.DeleteTableAsync(TestClient.DeleteTable()));
        yield return new("UpdateTimeToLiveAsync", client => client.UpdateTimeToLiveAsync(TestClient.UpdateTimeToLive()));
        yield return new("ListTablesAsync", client => client.ListTablesAsync(TestClient.ListTables()));
        yield return new("DescribeTimeToLiveAsync", client => client.DescribeTimeToLiveAsync(TestClient.DescribeTimeToLive()));
        yield return new("UpdateTableAsync", client => client.UpdateTableAsync(TestClient.UpdateTable()));
    }

    public static IEnumerable<DisposedCall<DynamoDbStreamsClient>> StreamsOperations()
    {
        yield return new("ListStreamsAsync", client => client.ListStreamsAsync(TestClient.ListStreams()));
        yield return new("DescribeStreamAsync", client => client.DescribeStreamAsync(TestClient.DescribeStream()));
        yield return new("GetShardIteratorAsync", client => client.GetShardIteratorAsync(TestClient.GetShardIterator()));
        yield return new("GetRecordsAsync", client => client.GetRecordsAsync(TestClient.GetRecords()));
    }

    [Test]
    [MethodDataSource(nameof(Operations))]
    public async Task Operation_DisposedClient_ThrowsObjectDisposedExceptionNamingTheClient(DisposedCall<DynamoDbClient> operation)
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.Create(handler);
        client.Dispose();

        // Act
        var exception = await TestClient.CaptureAsync<ObjectDisposedException>(() => operation.Call(client));

        // Assert
        await Assert.That(exception.ObjectName).IsEqualTo(typeof(DynamoDbClient).FullName);
    }

    [Test]
    [MethodDataSource(nameof(StreamsOperations))]
    public async Task Operation_DisposedStreamsClient_ThrowsObjectDisposedExceptionNamingTheClient(DisposedCall<DynamoDbStreamsClient> operation)
    {
        // Arrange
        using var handler = new StubHandler();
        var client = TestClient.CreateStreams(handler);
        client.Dispose();

        // Act
        var exception = await TestClient.CaptureAsync<ObjectDisposedException>(() => operation.Call(client));

        // Assert
        await Assert.That(exception.ObjectName).IsEqualTo(typeof(DynamoDbStreamsClient).FullName);
    }

    [Test]
    public async Task Dispose_RequestInFlight_CancelsTheRequest()
    {
        // Arrange (the handler is inside the request and waits for the token)
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHandler(async (_, token) =>
        {
            entered.SetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        var client = TestClient.Create(handler);
        var call = client.GetItemAsync(TestClient.GetItem());
        var token = await entered.Task;

        // Act
        client.Dispose();

        // Assert
        await Assert.That(token.IsCancellationRequested).IsTrue();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);
    }

    [Test]
    public async Task Dispose_StreamsRequestInFlight_CancelsTheRequest()
    {
        // Arrange (the handler is inside the request and waits for the token)
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new DelegateHandler(async (_, token) =>
        {
            entered.SetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        });
        var client = TestClient.CreateStreams(handler);
        var call = client.GetRecordsAsync(TestClient.GetRecords());
        var token = await entered.Task;

        // Act
        client.Dispose();

        // Assert
        await Assert.That(token.IsCancellationRequested).IsTrue();
        await TestClient.CaptureAsync<OperationCanceledException>(() => call);
    }
}
