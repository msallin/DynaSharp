using System.Globalization;
using System.Numerics;
using DynaSharp.Requests;

namespace DynaSharp.Testing.Fake;

/// <summary>
/// The state of one <see cref="FakeDynamoDb"/>: its tables by name, its streams, its clock and the gate every request and every direct call holds from start to end.
/// </summary>
/// <remarks>
/// <para>
/// One lock for everything keeps the fake simple and makes each request atomic, also a batch, which is stricter than DynamoDB and cannot fail a test that passes there. Nothing inside
/// the gate waits, so a test with many clients over one fake cannot deadlock on it.
/// </para>
/// <para>
/// Every entry that changes items, the wire, the direct API and the sweep of time to live, ends with <see cref="CommitChanges"/> when it went through and with
/// <see cref="DiscardChanges"/> in any case, so that the stream records of a request are there all at once or not at all.
/// </para>
/// </remarks>
/// <param name="clock">The clock of the creation times, of time to live and of the windows of client request tokens.</param>
internal sealed class FakeDatabase(TimeProvider clock)
{
    // Every stream ARN the fake handed out, and how many shards it opened, which Reset keeps: a stream enabled after it has another ARN and shard ids than one before it on the same
    // clock, so an iterator of a stream that is gone finds none.
    private readonly HashSet<string> _streamArns = new(StringComparer.Ordinal);
    private int _shardsOpened;

    // Sequence numbers of DynamoDB Streams are decimal strings of 21 digits and more, and those of the fake grow from here, over all its streams.
    private BigInteger _nextSequence = BigInteger.Pow(10, 20);

    // Completes at the next change of a stream, and is replaced then: the readers of a client of CreateStreamsClient end their waits on it.
    private TaskCompletionSource _nextStreamChange = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the gate.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Gets the clock.</summary>
    public TimeProvider Clock { get; } = clock;

    /// <summary>Gets the tables by name, in ordinal order, which is also the order of <c>ListTables</c>.</summary>
    public SortedDictionary<string, FakeTable> Tables { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the client request tokens of the transactions that went through.</summary>
    public IdempotencyCache Tokens { get; } = new();

    /// <summary>Gets every stream in the order it was enabled, those of deleted tables and the disabled ones too; <see cref="FakeStream.IsGone"/> tells which no request finds.</summary>
    public List<FakeStream> Streams { get; } = [];

    /// <summary>Gets a table for an item operation.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable GetTable(string name) =>
        Tables.TryGetValue(name, out var table) ? table : throw FakeError.ResourceNotFound();

    /// <summary>Gets a table for a table operation, which DynamoDB answers with another message than an item operation.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable DescribeTable(string name) =>
        Tables.TryGetValue(name, out var table) ? table : throw FakeError.ResourceNotFound($"Requested resource not found: Table: {name} not found");

    /// <summary>Adds a table.</summary>
    /// <param name="table">The table.</param>
    /// <exception cref="FakeError">A <c>ResourceInUseException</c>: a table of that name exists.</exception>
    public void Create(FakeTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!Tables.TryAdd(table.Name, table))
        {
            throw FakeError.ResourceInUse($"Table already exists: {table.Name}");
        }
    }

    /// <summary>Removes a table with its items. The name is free at once.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The table.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>.</exception>
    public FakeTable Delete(string name)
    {
        var table = DescribeTable(name);
        Tables.Remove(name);
        return table;
    }

    /// <summary>
    /// Enables a stream on a table: a new one, with a label of the clock that no stream of the table had before, also before a <see cref="Reset"/>, so that enabling again gives a new ARN.
    /// </summary>
    /// <param name="table">The table, which has no enabled stream.</param>
    /// <param name="viewType">What the records hold of the item.</param>
    /// <returns>The stream, which is now <see cref="FakeTable.LatestStream"/> of the table.</returns>
    public FakeStream EnableStream(FakeTable table, StreamViewType viewType)
    {
        ArgumentNullException.ThrowIfNull(table);
        var now = Clock.GetUtcNow();
        var at = now;
        while (!_streamArns.Add(table.Arn + "/stream/" + Label(at)))
        {
            at = at.AddMilliseconds(1);
        }

        var created = new FakeStream(table, viewType, Label(at), now, NextSequenceNumber(), NextShardNumber());
        Streams.Add(created);
        table.LatestStream = created;
        StreamsChanged();
        return created;

        // 2026-01-01T00:00:00.000, as DynamoDB labels a stream.
        static string Label(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    /// <summary>Finds a stream a request names.</summary>
    /// <param name="arn">The ARN.</param>
    /// <returns>The stream.</returns>
    /// <exception cref="FakeError">A <c>ResourceNotFoundException</c>: there is no such stream, or it is gone.</exception>
    public FakeStream FindStream(string arn)
    {
        var now = Clock.GetUtcNow();
        return Streams.Find(stream => stream.Arn == arn && !stream.IsGone(now)) ?? throw FakeError.ResourceNotFound($"Requested resource not found: Stream: {arn} not found");
    }

    /// <summary>Gives the next sequence number of the fake.</summary>
    /// <returns>A number larger than every one before.</returns>
    public BigInteger NextSequenceNumber() => _nextSequence++;

    /// <summary>
    /// Gives a task that completes at the next change of a stream: a record, a split, a stream enabled or disabled, a <see cref="Reset"/>. Its continuations run on the pool, not under
    /// the gate. A request that changes nothing on a stream, such as a <c>GetRecords</c>, completes none, so readers that wait on it do not wake each other.
    /// </summary>
    /// <returns>The task.</returns>
    public Task NextStreamChange()
    {
        lock (Gate)
        {
            return _nextStreamChange.Task;
        }
    }

    /// <summary>Gives the number of the id of a new shard.</summary>
    /// <returns>A number larger than every one before, also before a <see cref="Reset"/>.</returns>
    public int NextShardNumber() => ++_shardsOpened;

    /// <summary>Turns the changes the request staged on the streams into records.</summary>
    /// <param name="byTimeToLive"><see langword="true"/> for the deletes of the sweep of time to live.</param>
    public void CommitChanges(bool byTimeToLive = false)
    {
        var now = Clock.GetUtcNow();
        var changed = false;
        foreach (var stream in Streams)
        {
            changed |= stream.Commit(NextSequenceNumber, now, byTimeToLive);
        }

        if (changed)
        {
            StreamsChanged();
        }
    }

    /// <summary>Drops what a request staged on the streams and did not commit, because it failed.</summary>
    public void DiscardChanges()
    {
        foreach (var stream in Streams)
        {
            stream.Discard();
        }
    }

    /// <summary>Drops every table and what belongs to them, every stream, and every client request token. The ARNs of the streams stay taken.</summary>
    public void Reset()
    {
        Tables.Clear();
        Streams.Clear();
        Tokens.Clear();
        StreamsChanged();
    }

    private void StreamsChanged()
    {
        var changed = _nextStreamChange;
        _nextStreamChange = new(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.SetResult();
    }
}
