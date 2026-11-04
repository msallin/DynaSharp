using System.Text;
using DynaSharp.Coordination;
using DynaSharp.Requests;
using DynaSharp.Testing;

namespace DynaSharp.UnitTests.Coordination;

/// <summary>
/// A table of locks in a <see cref="FakeDynamoDb"/>, behind a client whose clock moves only when the test says so. The fake answers every request, unless
/// a queued response or <see cref="Intercept"/> answers first, so a test can fail, hold back or watch one request. Each harness has a table name of its own,
/// which keeps the spans, metrics and logs of parallel tests apart.
/// </summary>
internal sealed class LockHarness : IDisposable
{
    public LockHarness(bool sortKey = true, int maxAttempts = 1)
    {
        TableName = "Locks-" + Guid.NewGuid().ToString("N");
        Fake = new FakeDynamoDb(Clock);
        Fake.CreateTable(new CreateTableRequest
        {
            TableName = TableName,
            AttributeDefinitions = sortKey
                ? [new AttributeDefinition("pk", ScalarAttributeType.String), new AttributeDefinition("sk", ScalarAttributeType.String)]
                : [new AttributeDefinition("pk", ScalarAttributeType.String)],
            KeySchema = sortKey ? [new KeySchemaElement("pk", KeyType.Hash), new KeySchemaElement("sk", KeyType.Range)] : [new KeySchemaElement("pk", KeyType.Hash)],
            BillingMode = BillingMode.PayPerRequest,
        });
        Handler = new StubHandler(AnswerAsync);
        Client = StubClient.Create(Handler, options => options with { TimeProvider = Clock, LoggerFactory = Logs, MaxAttempts = maxAttempts });
        Table = Client.Table(TableName, "pk", sortKey ? "sk" : null);
        Locks = new LockTable(Table);
    }

    public string TableName { get; }

    public ManualTimeProvider Clock { get; } = new();

    public FakeDynamoDb Fake { get; }

    public StubHandler Handler { get; }

    public CapturingLoggerFactory Logs { get; } = new();

    /// <summary>Gets what the locks logged, without what the client logged.</summary>
    public IReadOnlyList<CapturingLoggerFactory.LogEntry> LockLogs => [.. Logs.Entries.Where(static entry => entry.Category == "DynaSharp.Coordination.LockTable")];

    public DynamoDbClient Client { get; }

    public Table<Item> Table { get; }

    public LockTable Locks { get; }

    /// <summary>Gets or sets what runs before the fake answers a request: it may answer in its place, or wait, or move the clock.</summary>
    public Func<RecordedRequest, CancellationToken, Task<StubResponse?>>? Intercept { get; set; }

    /// <summary>
    /// Holds back every request but a release until it is cancelled, as a DynamoDB that does not answer would. A release still goes through, so that
    /// disposing a lease at the end of a test ends.
    /// </summary>
    /// <param name="onHold">What runs when a request is held back, before it waits.</param>
    public void HoldBack(Action? onHold = null) =>
        Intercept = async (request, cancellationToken) =>
        {
            if (Encoding.UTF8.GetString(request.Body.Span).Contains("REMOVE", StringComparison.Ordinal))
            {
                return null;
            }

            onHold?.Invoke();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        };

    /// <summary>Gets the key of the item of a lock in this table.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <returns>The key.</returns>
    public Item KeyOf(string name) => Table.SortKeyName is null ? new() { ["pk"] = "LOCK#" + name } : new() { ["pk"] = "LOCK#" + name, ["sk"] = "LOCK" };

    /// <summary>Reads the item of a lock straight from the fake.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Item? ItemOf(string name) => Fake.Get(TableName, KeyOf(name));

    /// <summary>Writes the owner of a lock straight into the fake, as a contender that took it over would.</summary>
    /// <param name="name">The name of the lock.</param>
    /// <param name="owner">The new owner.</param>
    public void Steal(string name, string owner)
    {
        var item = ItemOf(name)!;
        item["owner"] = owner;
        item["rvn"] = "stolen";
        Fake.Put(TableName, item);
    }

    /// <summary>
    /// Makes a second process: a client of its own on the same fake, with a clock of its own that the test moves apart from this one. Its locks see the
    /// holders of this bench as a process on another machine would.
    /// </summary>
    /// <param name="clock">The clock of the other process.</param>
    /// <returns>The lock table of the other process.</returns>
    public LockTable OtherProcess(ManualTimeProvider clock)
    {
        var client = StubClient.Create(Fake.CreateHandler(), options => options with { TimeProvider = clock });
        return new LockTable(client.Table(TableName, "pk", Table.SortKeyName));
    }

    /// <summary>Moves the clock in steps of <paramref name="step"/> until <paramref name="task"/> completes, at most <paramref name="steps"/> times, and returns its result.</summary>
    /// <typeparam name="T">The result.</typeparam>
    /// <param name="task">The task, which waits on the clock.</param>
    /// <param name="step">How far each step moves the clock.</param>
    /// <param name="steps">The most steps.</param>
    /// <returns>The result of the task.</returns>
    public async Task<T> RunAsync<T>(Task<T> task, TimeSpan step, int steps = 1000)
    {
        // The code under test runs on other threads between the steps, so each step gives it a moment of real time, and a task that never completes
        // fails the test instead of hanging it.
        for (var moved = 0; !task.IsCompleted && moved < steps; moved++)
        {
            Clock.Advance(step);
            await ((Task)task).WaitAsync(TimeSpan.FromMilliseconds(2), TimeProvider.System).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        return await task.WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);
    }

    public void Dispose()
    {
        Client.Dispose();
        Handler.Dispose();
        Logs.Dispose();
    }

    private async Task<StubResponse> AnswerAsync(RecordedRequest request, CancellationToken cancellationToken)
    {
        if (Intercept is { } intercept && await intercept(request, cancellationToken) is { } answer)
        {
            return answer;
        }

        return Fake.Answer(request);
    }
}
