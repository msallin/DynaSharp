using System.Diagnostics;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A scan of several segments at once, from <see cref="ScanCommand{T}.Parallel"/>. All it can do is give the merged items: pages of several segments have no
/// single key to continue from, so what <c>PageAsync</c> would mean does not compile. Paging across requests goes through
/// <see cref="ScanCommand{T}.Segment"/>, one segment at a time.
/// </summary>
/// <remarks>See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.</remarks>
/// <typeparam name="T">How the items are read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct ParallelScanCommand<T>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "Scan";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal ParallelScanCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    /// <summary>
    /// Reads all items of all segments as they come. The order is unspecified: items of one page stay together and in the order of the page, and pages of
    /// segments mix in the order they arrive, which differs from run to run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is sent until the enumeration starts. Each segment has one request in flight at a time, so there are never more requests than segments, and a
    /// segment waits with its next page while the consumer is behind, so a slow consumer keeps only about two pages per segment in memory. Each page is one
    /// call with its own span and retries, and the spans carry the segment.
    /// </para>
    /// <para>
    /// The first failure of a segment stops all others and surfaces once, from <c>MoveNextAsync</c>, as the exception it is and not as an aggregate; items
    /// that arrived before it may still come first. Cancelling stops all segments, and so does leaving the enumeration early. In every case disposing the enumerator, which
    /// <c>await foreach</c> does, waits until no segment runs any more.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read, as does <c>WithCancellation</c> on the result.</param>
    /// <returns>The items, which can be enumerated once; a second enumeration throws <see cref="InvalidOperationException"/>, so build the command again to read again.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public IAsyncEnumerable<T> ItemsAsync(CancellationToken cancellationToken = default)
    {
        var state = CommandState.Enter(_state, _version, Operation, _table);
        state.Table.AddRequiredNames(state);
        return ParallelScan<T>.Items(state, cancellationToken);
    }
}
