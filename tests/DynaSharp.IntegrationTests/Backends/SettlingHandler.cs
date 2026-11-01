using System.Diagnostics;

namespace DynaSharp.IntegrationTests.Backends;

/// <summary>
/// Gives the writes of a client time to reach the replicas of real DynamoDB before the client reads eventually consistent. An emulator has one copy of everything, so
/// the tests read right after they write, also from global secondary indexes, where nothing else is possible. On real DynamoDB that read may miss the write, normally for a
/// fraction of a second. This handler lets a test stay as it is, and it holds back only those reads, on one client, which is one test.
/// </summary>
/// <remarks>
/// It waits a fixed time because the service says nothing about when a write has settled. That time is a judgement: a test that fails here for lack of it fails again and differently when
/// run alone, and the answer is a longer time, not a change to the test. A strongly consistent read goes straight through.
/// </remarks>
internal sealed class SettlingHandler : DelegatingHandler
{
    private const string TargetHeader = "X-Amz-Target";

    private static readonly TimeSpan SettlingTime = TimeSpan.FromSeconds(1.5);

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
        if (operation is "Query" or "Scan" or "GetItem" or "BatchGetItem" && !await IsConsistentAsync(request, cancellationToken))
        {
            var lastWrite = Volatile.Read(ref _lastWrite);
            var left = lastWrite == 0 ? TimeSpan.Zero : SettlingTime - Stopwatch.GetElapsedTime(lastWrite);
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

    // Input: {"TableName":"t","ConsistentRead":true,...}. A BatchGetItem holds one such member per table, and the first says enough for a test.
    private static async Task<bool> IsConsistentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        return body.Contains("\"ConsistentRead\":true", StringComparison.Ordinal);
    }
}
