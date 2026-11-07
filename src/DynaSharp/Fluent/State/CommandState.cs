using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Operations;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>
/// The wire form of one command while it is built and sent: the request body, its expressions and what the options say. Everything is
/// UTF-8 in pooled arrays, so a command costs no managed allocation once the pool of the client is warm.
/// </summary>
/// <remarks>
/// <para>
/// This object is what crosses the await, which the ref struct command in front of it cannot. <see cref="Version"/> makes the command a
/// linear value: every fluent call consumes the handle it was called on and returns a new one. A handle that was used, or a copy of it,
/// or one left over from before the state went back to the pool, no longer matches, so it throws instead of writing into a
/// request that now belongs to someone else. The number only ever grows, and a handle lives on one stack, so reading it needs no
/// synchronization: the thread that holds a stale handle wrote the newer number itself.
/// </para>
/// <para>
/// A state goes back to the pool in one place, <see cref="Release"/>, and only the call that was sent, the read that follows its pages, a batch that sends several, or
/// <see cref="RenderJson"/>, holds the version that lets it through. The compare-and-swap in there means that a second release loses and throws, whatever threads are
/// involved, so one state cannot end up in the pool twice and with two commands.
/// </para>
/// <para>
/// A command that is abandoned is simply collected, with its arrays. <see cref="ArrayPool{T}"/> does not track rented arrays, so the cost
/// is one lost pool entry, not a leak, and no finalizer is needed. Building a command that throws, for example from a custom converter, ends the same way.
/// </para>
/// <para>Who gives the state back, and when. Each owner holds the version it passes to <see cref="Release"/>:</para>
/// <list type="table">
/// <listheader><term>Owner</term><description>Gives the state back</description></listheader>
/// <item><term><see cref="CommandCall{TOperation, TResponse}.Release"/></term><description>One request: when the pipeline ends the call, or at once if the client is disposed.</description></item>
/// <item><term><see cref="ActionCall{TOperation, TResponse}.Release"/></term><description>A transaction, as for one request. It does nothing unless <see cref="IActionOperation{TResponse}.EndsCommand"/>, so a batch keeps its state between requests.</description></item>
/// <item><term><see cref="BatchDriver.SendAsync{TOperation, TResult}"/></term><description>A batch, when the last request is done or the batch gives up. <see cref="BatchDriver.RenderJson{TOperation, TResult}"/> does the same for its <c>ToRequestJson</c>.</description></item>
/// <item><term><see cref="PagedRead{TOperation, T}.PageAsync"/>, <see cref="PagedRead{TOperation, T}.FirstAsync"/> and <see cref="PagedRead{TOperation, T}.CountAsync"/></term><description>One page, the pages up to the first item, or all pages of a count, when the read ends. <see cref="PageCall{TOperation, T}.Release"/> does nothing, because the state lives for all pages.</description></item>
/// <item><term><see cref="PagedRead{TOperation, T}.ThrowIfProjected"/></term><description>A count with a projection, before it throws, and the state never reaches the read.</description></item>
/// <item><term><see cref="RenderJson"/></term><description>Every other <c>ToRequestJson</c>, whatever <see cref="Finish"/> does.</description></item>
/// <item><term><see cref="OnceEnumerable{T}.DisposeAsync"/></term><description>A read that is enumerated, also if its enumerator never moved: the state of the read, or the state a parallel scan or a fan-out copies from. A read nobody enumerates is abandoned.</description></item>
/// <item><term><see cref="ParallelScan{T}"/></term><description>The state of each segment, from <see cref="StartSegment"/>, when its task ends, and the enumerator waits for that.</description></item>
/// <item><term><see cref="FanOutCursor{T}.Release"/></term><description>The state of each partition, from <see cref="StartPartition"/>, when the partition is done or the merge is disposed.</description></item>
/// <item><term><see cref="FanOutRead{T}.PageAsync"/> and <see cref="FanOutRead{T}.CountAsync"/></term><description>The state a fan-out copies from, when the page or the count ends. A partition of a count goes through <see cref="PagedRead{TOperation, T}.CountAsync"/>.</description></item>
/// </list>
/// <para>What a change here must respect:</para>
/// <list type="bullet">
/// <item><term>Linear version</term><description>Every fluent step goes through <see cref="Enter"/>, and a terminal passes on the version it entered with.</description></item>
/// <item><term>CAS release</term><description><see cref="Release"/> is the only way back, and the compare-and-swap comes first, before anything is cleared.</description></item>
/// <item><term>Pending buffer</term><description>The handler of an interpolated expression runs before its method, so the text waits in <see cref="ExpressionWriter.StartPending"/> until <see cref="ExpressionWriter.CommitPending"/> moves it to a clause. Others use that buffer as scratch, so nothing stays there between calls.</description></item>
/// <item><term>Open item</term><description>After <see cref="StartItem"/> nothing else goes into the body until <see cref="CloseItem"/>.</description></item>
/// <item><term>Write-rule flags</term><description><see cref="WriteState"/> holds what waits for <see cref="WriteState.Settle"/>, and a transaction settles it per action.</description></item>
/// <item><term><see cref="MaxRetainedLength"/></term><description>The longest array an idle state keeps, which <see cref="Release"/> passes to every buffer.</description></item>
/// <item><term>Batch actions</term><description><see cref="Actions"/> holds offsets into <see cref="Body"/>, which a batch does not send: <see cref="BatchDriver.WriteBody{TOperation, TResult}"/> cuts a request out of it for each round.</description></item>
/// </list>
/// <para>A new field is reset in <see cref="Release"/>, or listed with its reason in <c>CommandStateResetTests</c>, which fails until it is one or the other.</para>
/// </remarks>
[DebuggerDisplay("{DebuggerText,nq}")]
internal sealed class CommandState
{
    // An idle state keeps arrays up to this size, so that the usual command never goes to the pool. A larger one is lent, not kept: an
    // item can reach 400 KB, and a batch 16 MB.
    private const int MaxRetainedLength = 16 * 1024;

    // A key of two attributes as JSON, which is all that most reads ever hold.
    private const int FirstPageKeyLength = 256;

    private readonly ClientCore _core;
    private ActionList? _actions;
    private KeyInputChanges? _keyInputs;
    private HeldKey? _keyHolder;
    private WriteState _write;
    private ReadOptions _read;
    private int _version;
    private bool _itemOpen;

    // Where the object of the key of the action that is being written starts in the body.
    private int _keyStart;

    // Where the body of a paged read ends before its start key. Zero until the first page is written.
    private int _pageMark;

    /// <summary>Initializes a new instance of the <see cref="CommandState"/> class.</summary>
    /// <param name="core">The client, which the state goes back to.</param>
    /// <param name="pool">The pool of the arrays, which are rented when they are first written to.</param>
    public CommandState(ClientCore core, ArrayPool<byte> pool)
    {
        _core = core;
        Pool = pool;
        Body = new PooledByteBuffer(pool);
        BodyJson = WireJson.CreateWriter(Body);
        Expressions = new ExpressionWriter(pool);
        PageKey = new PooledByteBuffer(pool, FirstPageKeyLength);
    }

    /// <summary>Gets the number a handle must hold to use this state. It changes with every step of the command.</summary>
    public int Version => _version;

    /// <summary>Gets the request body as far as it is written. The expressions and options join it in <see cref="Finish"/>.</summary>
    public PooledByteBuffer Body { get; }

    /// <summary>Gets the writer on <see cref="Body"/>. It is clean, except while the item of a put is open.</summary>
    public Utf8JsonWriter BodyJson { get; }

    /// <summary>Gets the expressions of the request.</summary>
    public ExpressionWriter Expressions { get; }

    /// <summary>
    /// Gets the raw JSON of the key the next page of a read starts after: <c>LastEvaluatedKey</c> as DynamoDB sent it, or the key of a token. It is
    /// kept apart from the body, because the body of the page before may still be sent again.
    /// </summary>
    public PooledByteBuffer PageKey { get; }

    /// <summary>Gets or sets the table of the command, which for a transaction is the table of the action that is being written.</summary>
    public TableModel Table { get; set; } = null!;

    /// <summary>Gets what a command of several actions keeps about them. A state that never serves such a command never makes one.</summary>
    public ActionList Actions => _actions ??= new ActionList(Pool);

    /// <summary>Gets what an update did to properties that feed keys. A state that never updates such a property never makes one.</summary>
    public KeyInputChanges KeyInputs => _keyInputs ??= new KeyInputChanges();

    /// <summary>Gets a value indicating whether an update changed a property that feeds a key.</summary>
    public bool HasKeyInputs => _keyInputs is { Count: > 0 };

    /// <summary>Gets what the write rules keep on this command. It is the field itself, because they change it in place.</summary>
    public ref WriteState Write => ref _write;

    /// <summary>Gets the options of a read. It is the field itself, because a clause changes it in place.</summary>
    public ref ReadOptions Read => ref _read;

    /// <summary>Gets or sets the number of attempts of this call, or zero for what the client says.</summary>
    public int MaxAttempts { get; set; }

    /// <summary>Gets or sets how many requests in a row a batch may send without progress, or zero for the default of <see cref="BatchDriver"/>.</summary>
    public int MaxStalledRounds { get; set; }

    /// <summary>Gets or sets what a write returns.</summary>
    public ReturnValue ReturnValues { get; set; }

    /// <summary>Gets or sets what a failed condition returns.</summary>
    public ReturnValuesOnConditionCheckFailure ReturnOnFailure { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a failed condition is an answer of the write rather than an error, which <c>TrySendAsync</c> sets. The request is the same either way,
    /// and the pipeline asks <see cref="IWireCall{TResponse}.TryReadFailure"/> about an error response before it throws.
    /// </summary>
    public bool ToleratesFailedCondition { get; set; }

    /// <summary>Gets or sets where a fan-out keeps the keys of the items of a page, or <see langword="null"/> for a read that does not need them.</summary>
    public FanOutKeys? ItemKeys { get; set; }

    /// <summary>Gets the pool of the arrays, for what lives and dies with this state.</summary>
    public ArrayPool<byte> Pool { get; }

    /// <summary>Gets or sets the capacity detail to return.</summary>
    public ReturnConsumedCapacity ReturnCapacity { get; set; }

    /// <summary>Gets or sets the item collection metrics to return.</summary>
    public ReturnItemCollectionMetrics ReturnMetrics { get; set; }

    /// <summary>Gets or sets the attribute that the response is read for, when one attribute is all a command returns.</summary>
    public string? ReturnPath { get; set; }

    /// <summary>
    /// Gets or sets the capacity of the response, for the span and the metric. It stays here, not in the result, because the result of a
    /// command that returns one attribute is that attribute.
    /// </summary>
    public ConsumedCapacity? ResponseCapacity { get; set; }

    /// <summary>Gets what the debugger shows: what the command would send so far.</summary>
    internal string DebuggerText => Describe(_version);

    /// <summary>Checks that a handle may use the state, and consumes the handle.</summary>
    /// <param name="state">The state of the handle.</param>
    /// <param name="version">The version of the handle.</param>
    /// <param name="operation">The operation of the command, for the message.</param>
    /// <param name="table">The table of the command, for the message.</param>
    /// <returns>The state.</returns>
    /// <exception cref="InvalidOperationException">The handle is stale, or it is a default value.</exception>
    public static CommandState Enter(CommandState? state, int version, string operation, TableModel? table)
    {
        var checkedState = Check(state, version, operation, table);
        checkedState._version = version + 1;
        return checkedState;
    }

    /// <summary>Checks that a handle may use the state, and leaves the handle valid.</summary>
    /// <param name="state">The state of the handle.</param>
    /// <param name="version">The version of the handle.</param>
    /// <param name="operation">The operation of the command, for the message.</param>
    /// <param name="table">The table of the command, for the message.</param>
    /// <returns>The state.</returns>
    /// <exception cref="InvalidOperationException">The handle is stale, or it is a default value.</exception>
    public static CommandState Check(CommandState? state, int version, string operation, TableModel? table)
    {
        if (state is null)
        {
            throw new InvalidOperationException($"This {operation} command is a default value. A command comes from a table: table.Update(...), not default.");
        }

        if (state._version != version)
        {
            throw new InvalidOperationException(
                $"This {operation} on {table!.Name} was already sent, extended or rendered. ToRequestJson() ends a command as sending does, and a command is built in one chain and used once; to use it again, build it again.");
        }

        return state;
    }

    /// <summary>Ends a value that went through a writer. A value that took the direct way left the writer untouched, and that saves two calls.</summary>
    /// <param name="json">The writer.</param>
    public static void EndWrite(Utf8JsonWriter json)
    {
        if (json.BytesPending != 0 || json.BytesCommitted != 0)
        {
            json.Flush();
            json.Reset();
        }
    }

    /// <summary>Writes a member whose value is the wire name of an enum, if it has one.</summary>
    /// <param name="request">The request, with at least one member before this.</param>
    /// <param name="member">What comes before the name: <c>,"Select":"</c>.</param>
    /// <param name="name">The wire name, or <see langword="null"/> for an option that was not set.</param>
    public static void WriteEnumMember(PooledByteBuffer request, ReadOnlySpan<byte> member, string? name)
    {
        if (name is not null)
        {
            request.Append(member);
            request.AppendAscii(name);
            request.Append((byte)'"');
        }
    }

    /// <summary>Starts the command on a state that came from the pool.</summary>
    /// <param name="table">The table of the command.</param>
    public void Start(TableModel table)
    {
        Table = table;
        Body.Append(table.BodyStart);
    }

    /// <summary>Writes a key of attribute values.</summary>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The sort key, or the default value if there is none.</param>
    public void WriteKey(AttributeValue partitionKey, AttributeValue sortKey)
    {
        // ,"Key":{"pk":{"S":"a"},"sk":{"S":"b"}}
        var table = Table;
        _keyStart = Body.WrittenCount + ",\"Key\":"u8.Length;
        WriteKey(table, table.KeyStart, partitionKey, sortKey);
    }

    /// <summary>Writes a key of attribute values as an object of its own, for a list of keys.</summary>
    /// <param name="table">The table of the key.</param>
    /// <param name="partitionKey">The partition key.</param>
    /// <param name="sortKey">The sort key, or the default value if there is none.</param>
    public void WriteKeyObject(TableModel table, AttributeValue partitionKey, AttributeValue sortKey)
    {
        // {"pk":{"S":"a"},"sk":{"S":"b"}}
        WriteKey(table, table.KeyStart.AsSpan(",\"Key\":"u8.Length), partitionKey, sortKey);
    }

    /// <summary>Starts the <c>Key</c> member of a key that writes itself. Write the key to what this returns, then call <see cref="EndKey"/>.</summary>
    /// <returns>The writer, inside the object.</returns>
    public DynamoDbWriter StartKey()
    {
        Body.Append(",\"Key\":"u8);
        _keyStart = Body.WrittenCount;
        BodyJson.WriteStartObject();
        return new DynamoDbWriter(BodyJson);
    }

    /// <summary>Ends what <see cref="StartKey"/> started.</summary>
    public void EndKey()
    {
        BodyJson.WriteEndObject();
        EndWrite(BodyJson);
    }

    /// <summary>
    /// Writes a key that writes itself as the key condition of a query. It goes through the body, which is what such a key can write to, and leaves it again, because a query has no
    /// <c>Key</c> member.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="key">The key.</param>
    public void WriteKeyCondition<TKey>(TKey key)
        where TKey : IKey
    {
        var mark = Body.WrittenCount;
        key.WriteKey(StartKey());
        EndKey();
        Expressions.AppendKeyCondition(Body.WrittenSpan[_keyStart..]);
        Body.Truncate(mark);
    }

    /// <summary>Keeps a generated key for the terminal of the update, in the holder of the last update if that took the same type of key.</summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <param name="key">The key.</param>
    public void HoldKey<TKey>(TKey key)
        where TKey : IKey
    {
        var holder = _keyHolder as TypedHeldKey<TKey> ?? new TypedHeldKey<TKey>();
        holder.Key = key;
        _keyHolder = holder;
        _write.HeldKey = holder;
    }

    /// <summary>Reads back the key of an update that is about to settle, which nothing follows in the body yet, because its expressions wait in their own segments.</summary>
    /// <returns>The key attributes.</returns>
    public Item ReadKey()
    {
        var reader = new DynamoDbReader(Body.WrittenSpan[_keyStart..]);
        reader.ReadStartItem();
        return AttributeValueConverter.ReadAttributes(ref reader);
    }

    /// <summary>
    /// Starts the <c>Item</c> member. The item stays open until the command is sent, so that attributes can follow, and nothing else
    /// is written to the body until then: conditions go to their own segments.
    /// </summary>
    /// <returns>The writer, inside the object.</returns>
    public DynamoDbWriter StartItem()
    {
        Body.Append(",\"Item\":"u8);
        BodyJson.WriteStartObject();
        _itemOpen = true;
        return new DynamoDbWriter(BodyJson);
    }

    /// <summary>Writes the rest of the body: the open item, the expressions and the options. Everything is written once and the bytes do not change after this.</summary>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option holds a value that is not a member of its enum.</exception>
    public ArraySegment<byte> Finish()
    {
        WriteMembers();
        Body.Append((byte)'}');
        return Body.WrittenSegment;
    }

    /// <summary>
    /// Writes the body up to its closing brace. <see cref="Finish"/> is this and the brace; a paged read that adds its start key for each
    /// page calls this once and adds the rest itself.
    /// </summary>
    public void WriteMembers()
    {
        CloseItem();
        _read.WriteMembers(Body);
        Expressions.WriteMembers(Body);
        WriteEnumMember(Body, ",\"ReturnValues\":\""u8, WireNames.ReturnValues.GetNameIfSet(ReturnValues));
        WriteEnumMember(Body, ",\"ReturnValuesOnConditionCheckFailure\":\""u8, WireNames.ReturnValuesOnConditionCheckFailures.GetNameIfSet(ReturnOnFailure));
        WriteRequestOptions(Body);
    }

    /// <summary>Writes the options that belong to a request as a whole, for a command whose request is not <see cref="Body"/>.</summary>
    /// <param name="request">The request, with at least one member before this.</param>
    public void WriteRequestOptions(PooledByteBuffer request)
    {
        WriteEnumMember(request, ",\"ReturnConsumedCapacity\":\""u8, WireNames.ReturnConsumedCapacities.GetNameIfSet(ReturnCapacity));
        WriteEnumMember(request, ",\"ReturnItemCollectionMetrics\":\""u8, WireNames.ReturnItemCollectionMetricsValues.GetNameIfSet(ReturnMetrics));
    }

    /// <summary>
    /// Ends the action of a transaction that is open: its item, its expressions, which leave the writer empty for the next action, and what its
    /// own condition returns. Each action has placeholders of its own, so <c>#n0</c> means something else in each.
    /// </summary>
    public void EndAction()
    {
        _write.Settle(this);
        CloseItem();
        Expressions.WriteMembers(Body);
        Expressions.Clear(MaxRetainedLength);
        WriteEnumMember(Body, ",\"ReturnValuesOnConditionCheckFailure\":\""u8, WireNames.ReturnValuesOnConditionCheckFailures.GetNameIfSet(ReturnOnFailure));
        ReturnOnFailure = default;
        Body.Append("}}"u8);
    }

    /// <summary>
    /// Writes the body of the next page of a read. The members are written for the first page and stay; each page replaces what follows them, which
    /// is the start key, so paging costs the copy of a key and nothing else.
    /// </summary>
    /// <returns>The body, which stays as it is until the next page.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option holds a value that is not a member of its enum.</exception>
    public ArraySegment<byte> WritePageBody()
    {
        if (_pageMark == 0)
        {
            WriteMembers();
            _pageMark = Body.WrittenCount;
        }

        // ...,"ExclusiveStartKey":{"pk":{"S":"a"},"sk":{"S":"b"}}}
        Body.Truncate(_pageMark);
        if (PageKey.WrittenCount > 0)
        {
            Body.Append(",\"ExclusiveStartKey\":"u8);
            Body.Append(PageKey.WrittenSpan);
        }

        Body.Append((byte)'}');
        return Body.WrittenSegment;
    }

    /// <summary>Starts one segment of a parallel scan as a command of its own, with everything this scan says. This state stays as it is.</summary>
    /// <param name="segment">The segment.</param>
    /// <returns>The state of the segment, which goes back with <see cref="Release"/>.</returns>
    public CommandState StartSegment(int segment)
    {
        var copy = CopyRead();
        copy._read.Segment = segment;
        return copy;
    }

    /// <summary>Starts one partition of a fan-out query as a command of its own, with everything this query says. This state stays as it is, and several threads may do this at once.</summary>
    /// <param name="partitionKey">The partition.</param>
    /// <param name="keys">Where the keys of the items go, or <see langword="null"/> if they are of no use.</param>
    /// <returns>The state of the partition, which goes back with <see cref="Release"/>.</returns>
    public CommandState StartPartition(AttributeValue partitionKey, FanOutKeys? keys)
    {
        var copy = CopyRead();
        copy.ItemKeys = keys;
        copy.Expressions.Placeholders.AddPartitionKey(partitionKey);
        return copy;
    }

    /// <summary>Finishes a command that is not sent, returns its body as text and gives the state back. The caller entered the state.</summary>
    /// <returns>The body.</returns>
    public string RenderJson()
    {
        var version = _version;
        try
        {
            return Encoding.UTF8.GetString(Finish());
        }
        finally
        {
            Release(version);
        }
    }

    /// <summary>Narrows a read to one attribute. The chain ends here, because the type of the command that follows has no clause left, so nothing adds to the projection later.</summary>
    /// <param name="path">The attribute.</param>
    /// <param name="discriminator">What the read does about items of other types. A read that looks at each item needs the discriminator to come back.</param>
    /// <exception cref="InvalidOperationException">The read has a projection already.</exception>
    /// <exception cref="ArgumentException">The path is not valid.</exception>
    public void SelectAttribute(string path, DiscriminatorCheck discriminator)
    {
        if (Expressions[Clause.Projection].WrittenCount > 0)
        {
            throw new InvalidOperationException($"This read of {Table.Name} gives one attribute, so it has no use for Project.");
        }

        Expressions.Projection.Add(new ReadOnlySpan<string>(in path));
        ReturnPath = path;
        _read.ChecksDiscriminator = discriminator == DiscriminatorCheck.CheckEachItem;
        if (_read.ChecksDiscriminator)
        {
            Expressions.Projection.AddName(Table.DiscriminatorAttributeName!);
        }
    }

    /// <summary>Sends the command. The caller entered the state, and from here on the call owns it: the pipeline releases it, whatever happens.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResponse">What the call returns.</typeparam>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, a task that the caller awaits exactly once.</returns>
    public ValueTask<TResponse> SendAsync<TOperation, TResponse>(CancellationToken cancellationToken)
        where TOperation : ICommandOperation<TResponse> =>
        _core.SendAsync<CommandCall<TOperation, TResponse>, TResponse>(new CommandCall<TOperation, TResponse>(this, _version), cancellationToken);

    /// <summary>Sends a write that takes a failed condition as its answer, see <see cref="ToleratesFailedCondition"/>. Otherwise the same as <see cref="SendAsync{TOperation, TResponse}"/>.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResponse">What the call returns.</typeparam>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, a task that the caller awaits exactly once.</returns>
    public ValueTask<TResponse> TrySendAsync<TOperation, TResponse>(CancellationToken cancellationToken)
        where TOperation : ICommandOperation<TResponse>
    {
        ToleratesFailedCondition = true;
        return SendAsync<TOperation, TResponse>(cancellationToken);
    }

    /// <summary>Sends one request of a command of several actions. Who owns the state then is up to the operation, see <see cref="IActionOperation{TResponse}.EndsCommand"/>.</summary>
    /// <typeparam name="TOperation">The operation.</typeparam>
    /// <typeparam name="TResponse">What the call returns.</typeparam>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response, a task that the caller awaits exactly once.</returns>
    public ValueTask<TResponse> SendActionsAsync<TOperation, TResponse>(CancellationToken cancellationToken)
        where TOperation : IActionOperation<TResponse> =>
        _core.SendAsync<ActionCall<TOperation, TResponse>, TResponse>(new ActionCall<TOperation, TResponse>(this, _version), cancellationToken);

    /// <summary>
    /// Gives the state back to the client for the next command. This is the one way back, and it lets the holder of the current version
    /// through once, see the remarks on this class.
    /// </summary>
    /// <param name="version">The version the owner of the state holds.</param>
    /// <exception cref="InvalidOperationException">The state was released already, which is a bug in this library.</exception>
    public void Release(int version)
    {
        if (Interlocked.CompareExchange(ref _version, version + 1, version) != version)
        {
            throw new InvalidOperationException("A command tried to release its state twice. The state stays with its current owner.");
        }

        Body.Clear(MaxRetainedLength);
        BodyJson.Reset();
        Expressions.Clear(MaxRetainedLength);
        PageKey.Clear(MaxRetainedLength);
        _actions?.Clear(MaxRetainedLength);
        _keyInputs?.Clear();
        _keyHolder?.Clear();
        _write = default;
        _read = default;
        Table = null!;
        _itemOpen = false;
        _keyStart = 0;
        _pageMark = 0;
        MaxAttempts = 0;
        MaxStalledRounds = 0;
        ReturnValues = default;
        ReturnOnFailure = default;
        ToleratesFailedCondition = false;
        ReturnCapacity = default;
        ReturnMetrics = default;
        ReturnPath = null;
        ResponseCapacity = null;
        ItemKeys = null;
        _core.Commands.Return(this);
    }

    /// <summary>Lets the write go out without looking at the version, which it still advances, see <see cref="WriteState.IgnoreVersion"/>.</summary>
    /// <param name="isUpdate">Whether the write is an update, which advances the version in its expressions.</param>
    /// <exception cref="InvalidOperationException">The write already has <c>IfVersion</c>.</exception>
    public void IgnoreVersion(bool isUpdate = false) => _write.IgnoreVersion(isUpdate);

    /// <summary>Ends what a write owes before it goes out, see <see cref="WriteState.Settle"/>.</summary>
    /// <returns>This state, for a chain.</returns>
    /// <exception cref="InvalidOperationException">The write breaks a rule that only its whole chain shows.</exception>
    public CommandState Settle()
    {
        _write.Settle(this);
        return this;
    }

    /// <summary>Gives the arrays to the pool, for a state that does not go back to the client.</summary>
    public void ReleaseArrays()
    {
        Body.Release();
        Expressions.Release();
        PageKey.Release();
        _actions?.Clear(maxRetainedLength: 0);
    }

    /// <summary>Renders what a command would send so far, for the debugger. It leaves the command as it is, apart from flushing the writer.</summary>
    /// <param name="version">The version of the handle that asks.</param>
    /// <returns>The text.</returns>
    public string Describe(int version)
    {
        if (version != _version || Table is null)
        {
            return "(sent, extended or rendered)";
        }

        BodyJson.Flush();
        using var members = new PooledByteBuffer();
        Expressions.WriteMembers(members);
        return Encoding.UTF8.GetString(Body.WrittenSpan) + Encoding.UTF8.GetString(members.WrittenSpan);
    }

    // What a read that fans out hands every copy of itself. The options of a read go by value, so only what every command has is named here.
    private CommandState CopyRead()
    {
        var copy = Table.Begin();
        copy._read = _read;
        copy.MaxAttempts = MaxAttempts;
        copy.ReturnCapacity = ReturnCapacity;
        copy.Expressions.CopyFrom(Expressions);
        return copy;
    }

    private void WriteKey(TableModel table, ReadOnlySpan<byte> start, AttributeValue partitionKey, AttributeValue sortKey)
    {
        Body.Append(start);
        WriteBodyValue(partitionKey);
        if (sortKey.Type != AttributeType.None)
        {
            Body.Append(table.SortKeyStart);
            WriteBodyValue(sortKey);
        }

        Body.Append((byte)'}');
    }

    private void CloseItem()
    {
        if (_itemOpen)
        {
            BodyJson.WriteEndObject();
            EndWrite(BodyJson);
            _itemOpen = false;
        }
    }

    private void WriteBodyValue(AttributeValue value)
    {
        AttributeValueConverter.Write(new DynamoDbWriter(BodyJson, Body), value);
        EndWrite(BodyJson);
    }
}
