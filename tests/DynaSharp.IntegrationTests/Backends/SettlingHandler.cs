using System.Diagnostics;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// Gives the writes of a client time to reach the replicas of real DynamoDB before the client reads eventually consistent. An emulator has one copy of everything, so
/// the tests read right after they write, also from global secondary indexes, where nothing else is possible. On real DynamoDB that read may miss the write, normally for a
/// fraction of a second. This handler lets a test stay as it is, and it holds back only those reads, on one client, which is one test.
/// </summary>
/// <remarks>
/// It waits a fixed time because the service says nothing about when a write has settled. That time is a judgement: a test that fails here for lack of it fails again and differently when
/// run alone, and the answer is a longer time, not a change to the test. A strongly consistent read goes straight through. A read of an index waits longer, because a global
/// secondary index is updated asynchronously, and on a table created seconds before, a query of an index can miss a write, or still find the old key of an item, two seconds after it.
/// </remarks>
internal sealed class SettlingHandler : DelegatingHandler
{
    private const string TargetHeader = "X-Amz-Target";

    private static readonly TimeSpan SettlingTime = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan IndexSettlingTime = TimeSpan.FromSeconds(5);

    private long _lastWrite;

#pragma warning disable CA2000 // The base class takes the handler and disposes it.
    /// <summary>Initializes a new instance of the <see cref="SettlingHandler"/> class that sends over the network.</summary>
    public SettlingHandler()
        : base(new SocketsHttpHandler())
#pragma warning restore CA2000
    {
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var operation = request.Headers.TryGetValues(TargetHeader, out var values) ? values.Single().Split('.')[^1] : string.Empty;
        if (operation is "Query" or "Scan" or "GetItem" or "BatchGetItem" && await ReadsEventuallyAsync(request, cancellationToken) is { } settling)
        {
            var lastWrite = Volatile.Read(ref _lastWrite);
            var left = lastWrite == 0 ? TimeSpan.Zero : settling - Stopwatch.GetElapsedTime(lastWrite);
            if (left > TimeSpan.Zero)
            {
                await Task.Delay(left, cancellationToken);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (operation is "PutItem" or "UpdateItem" or "DeleteItem" or "BatchWriteItem" or "TransactWriteItems")
        {
            Volatile.Write(ref _lastWrite, Stopwatch.GetTimestamp());
        }

        return response;
    }

    // Input: {"TableName":"t","IndexName":"gsi","ConsistentRead":true,...}. A BatchGetItem holds one such member per table, and the first says enough for a test.
    // Returns how long the read waits after a write, or null for a strongly consistent read. A local index takes the time of a global one, which is more than it needs.
    private static async Task<TimeSpan?> ReadsEventuallyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (body.Contains("\"ConsistentRead\":true", StringComparison.Ordinal))
        {
            return null;
        }

        return body.Contains("\"IndexName\":", StringComparison.Ordinal) ? IndexSettlingTime : SettlingTime;
    }
}
