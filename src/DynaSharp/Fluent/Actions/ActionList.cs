using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// What a command of several actions knows about them: a batch or a transaction. The actions themselves are bytes in the body of the
/// <see cref="CommandState"/>, one after the other, so this is a list of offsets into that one buffer and the table each action belongs to. Nothing
/// here is a request object, and an idle list keeps its arrays, so that a batch allocates nothing per action.
/// </summary>
/// <remarks>
/// A batch is cut into requests when it is sent. <see cref="First"/> and <see cref="End"/> say which actions the request on its way holds, and
/// <see cref="Carry"/> what DynamoDB left of them.
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A pooled object: Clear gives the array back, and the object lives on.")]
internal sealed class ActionList
{
    private const int FirstCapacity = 32;
    private const int MaxRetainedCapacity = 1024;

    private int[] _offsets = new int[FirstCapacity + 1];
    private int[] _tableIndexes = new int[FirstCapacity];

    // Where the key of each action that names its item by key starts and ends in the body, two entries per action; an empty range for one that does not.
    private int[] _keys = new int[FirstCapacity * 2];
    private TableModel[] _tables = new TableModel[4];

    /// <summary>Initializes a new instance of the <see cref="ActionList"/> class.</summary>
    /// <param name="pool">The pool of <see cref="Carry"/>.</param>
    public ActionList(ArrayPool<byte> pool)
    {
        Carry = new PooledByteBuffer(pool);
    }

    /// <summary>Gets the number of actions.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the number of distinct tables, which differ by name: two views of one table are one table.</summary>
    public int TableCount { get; private set; }

    /// <summary>Gets or sets the first action of the request on its way.</summary>
    public int First { get; set; }

    /// <summary>Gets or sets the action after the last one of the request on its way.</summary>
    public int End { get; set; }

    /// <summary>
    /// Gets what the last response left unprocessed, exactly as it came: DynamoDB gives it in the form of <c>RequestItems</c>, so the next request
    /// sends it back without parsing it. Empty if nothing was left.
    /// </summary>
    public PooledByteBuffer Carry { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the body of the last response could not be read. DynamoDB answered that request, but what it did is not known, so the response leaves
    /// <see cref="Carry"/> and <see cref="Results"/> as they were before the request, and a batch that gives up reports its actions as unprocessed.
    /// </summary>
    public bool ReadFailed { get; set; }

    /// <summary>Gets or sets the body of the request on its way, which explains a rejection. Valid during the call.</summary>
    public ArraySegment<byte> SentBody { get; set; }

    /// <summary>Gets or sets the idempotency token a transaction was given, or <see langword="null"/> to generate one.</summary>
    public string? Token { get; set; }

    /// <summary>Gets or sets what a batch has read so far: a list of the type of its table, which this class does not know.</summary>
    public object? Results { get; set; }

    /// <summary>Gets the name of the table, if all actions have one.</summary>
    public string? OnlyTableName => TableCount == 1 ? _tables[0].Name : null;

    /// <summary>Adds an action.</summary>
    /// <param name="table">The table of the action.</param>
    /// <param name="offset">Where the action starts in the body.</param>
    public void Add(TableModel table, int offset)
    {
        if (Count == _tableIndexes.Length)
        {
            Array.Resize(ref _tableIndexes, Count * 2);
            Array.Resize(ref _offsets, (Count * 2) + 1);
            Array.Resize(ref _keys, Count * 4);
        }

        _keys[Count * 2] = 0;
        _keys[(Count * 2) + 1] = 0;
        _offsets[Count] = offset;
        _tableIndexes[Count++] = IndexOf(table);
    }

    /// <summary>Records the key of the last action, which names its item by key, and finds an action before it on the same item.</summary>
    /// <param name="start">Where the key object starts in the body.</param>
    /// <param name="body">The body, which ends with the key.</param>
    /// <returns>The index of the earlier action on the same item, or -1 if there is none.</returns>
    public int AddKey(int start, ReadOnlySpan<byte> body)
    {
        // The same item of the same table has the same key bytes, as both are written the same way; a key written apart, such as a number as 1 and as 1.0, is left to DynamoDB.
        var index = Count - 1;
        _keys[index * 2] = start;
        _keys[(index * 2) + 1] = body.Length;
        var key = body[start..];
        for (var i = 0; i < index; i++)
        {
            if (_tableIndexes[i] == _tableIndexes[index] && body[_keys[i * 2].._keys[(i * 2) + 1]].SequenceEqual(key))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Marks the end of the last action. Call it before anything asks where an action ends.</summary>
    /// <param name="bodyLength">The length of the body.</param>
    public void Seal(int bodyLength) => _offsets[Count] = bodyLength;

    /// <summary>Gets the bytes of an action.</summary>
    /// <param name="index">The index of the action.</param>
    /// <param name="body">The body.</param>
    /// <returns>The bytes.</returns>
    public ReadOnlySpan<byte> Entry(int index, ReadOnlySpan<byte> body) => body[_offsets[index].._offsets[index + 1]];

    /// <summary>Gets how many bytes a range of actions takes.</summary>
    /// <param name="first">The index of the first action.</param>
    /// <param name="end">The index after the last action.</param>
    /// <returns>The number of bytes.</returns>
    public int LengthOf(int first, int end) => _offsets[end] - _offsets[first];

    /// <summary>
    /// Gets the verb of an action of a transaction, which is the name of the one member the action starts with in the body, for the message of a failure. It is read back rather
    /// than kept, so a transaction that gets through pays nothing for it.
    /// </summary>
    /// <param name="index">The index of the action.</param>
    /// <param name="body">The body.</param>
    /// <returns>The verb.</returns>
    public string VerbOf(int index, ReadOnlySpan<byte> body)
    {
        // {"Put":{"TableName":"Users",... gives Put.
        var name = body[(_offsets[index] + "{\""u8.Length)..];
        return Encoding.UTF8.GetString(name[..name.IndexOf((byte)'"')]);
    }

    /// <summary>Gets a table.</summary>
    /// <param name="tableIndex">The index among the distinct tables.</param>
    /// <returns>The table.</returns>
    public TableModel TableAt(int tableIndex) => _tables[tableIndex];

    /// <summary>Gets which of the distinct tables an action belongs to.</summary>
    /// <param name="index">The index of the action.</param>
    /// <returns>The index among the distinct tables.</returns>
    public int TableIndexOf(int index) => _tableIndexes[index];

    /// <summary>Adds the tables of the request on its way to a scan. A request that repeats unprocessed actions names the tables of the ones it started with.</summary>
    /// <param name="scan">The scan.</param>
    public void AddTables(ref TableScan scan)
    {
        for (var i = First; i < End; i++)
        {
            scan.Add(_tables[_tableIndexes[i]].Name);
        }
    }

    /// <summary>Empties the list for the next command.</summary>
    /// <param name="maxRetainedLength">The longest array <see cref="Carry"/> keeps.</param>
    public void Clear(int maxRetainedLength)
    {
        if (_tableIndexes.Length > MaxRetainedCapacity)
        {
            _tableIndexes = new int[FirstCapacity];
            _offsets = new int[FirstCapacity + 1];
            _keys = new int[FirstCapacity * 2];
        }

        Array.Clear(_tables, 0, TableCount);
        TableCount = 0;
        Count = 0;
        First = 0;
        End = 0;
        Carry.Clear(maxRetainedLength);
        SentBody = default;
        Token = null;
        Results = null;
        ReadFailed = false;
    }

    private int IndexOf(TableModel table)
    {
        for (var i = 0; i < TableCount; i++)
        {
            if (string.Equals(_tables[i].Name, table.Name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        if (TableCount == _tables.Length)
        {
            Array.Resize(ref _tables, TableCount * 2);
        }

        _tables[TableCount] = table;
        return TableCount++;
    }
}
