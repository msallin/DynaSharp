using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>GetItem</c> under construction. Chain the options and end with <see cref="ItemAsync"/> or <see cref="SendAsync"/> in one expression.
/// </summary>
/// <remarks>
/// <para>
/// A command is a ref struct over pooled state, which is what makes it free of allocation. The compiler therefore keeps it from being stored in
/// a field, captured in a lambda or kept across an <c>await</c>: compute values first, or put the chain into a method that returns the
/// command. Every call returns a new handle and uses up the one it was called on, so reusing a command throws
/// <see cref="InvalidOperationException"/>; to send the same request twice, build it twice.
/// </para>
/// </remarks>
/// <typeparam name="T">How the item is read.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct GetCommand<T> : IRetryable<GetCommand<T>>
    where T : IDynamoDbDocument<T>
{
    private const string Operation = "GetItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal GetCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>
    /// Asks for a strongly consistent read, which sees every write that succeeded before it and costs twice the capacity. A read is eventually
    /// consistent otherwise, and can miss a very recent write.
    /// </summary>
    /// <returns>The command, which replaces this one.</returns>
    public GetCommand<T> Consistent()
    {
        var state = Enter();
        state.Read.ConsistentRead = true;
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads only some attributes. It saves bandwidth, not capacity: DynamoDB charges for the whole item. A read of an entity needs its discriminator and its
    /// keys, so they join the projection on their own, and it needs whatever else the entity requires, so a projection is mostly for <see cref="Item"/>.
    /// </summary>
    /// <param name="paths">The names or document paths. Reserved words need no care, and a second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public GetCommand<T> Project(params ReadOnlySpan<string> paths)
    {
        var state = Enter();
        state.Expressions.Project(paths);
        return new(state, state.Table);
    }

    /// <summary>Reads only some attributes, given as handles: <c>Project(Order.Attrs.Status, Order.Attrs.Total)</c>. See <see cref="Project(ReadOnlySpan{string})"/>.</summary>
    /// <param name="attributes">The attributes. A second call adds to the first.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentException">A path is not valid.</exception>
    public GetCommand<T> Project(params ReadOnlySpan<Attr> attributes)
    {
        var state = Enter();
        state.Expressions.Project(attributes);
        return new(state, state.Table);
    }

    /// <summary>
    /// Reads one attribute instead of the entity, for what a service wants from an item more often than the item: <c>var total = await orders.Get(key).Select(Order.Attrs.Total).SendAsync(ct);</c>. It projects the attribute, with the discriminator of an entity, which tells the item from an item of another type as a read of the entity does, so it saves bandwidth, not capacity: DynamoDB
    /// charges for whole items. The result is the value and no entity, so a <c>required</c> member cannot be in the way, which is what this has over <c>Project</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The chain ends here but for its terminal, as with <c>Return</c>: <see cref="Consistent"/>, <see cref="ReturnCapacity"/> and <see cref="MaxAttempts"/> come before this, a second <c>Select</c> and a <c>Project</c> after it do not compile, and a <c>Project</c> before it throws.
    /// </para>
    /// <para>
    /// A missing attribute follows the type of the handle: null for one of a nullable property, an empty set for a set, because DynamoDB has none, and otherwise a <see cref="DynamoDbMappingException"/>. Never a default, and that is where this differs from a read of the entity,
    /// which leaves a property that is not <c>required</c> at its initial value: an <c>int</c> without its attribute is 0 there and throws here, and so does a <c>[Version]</c> on an item without one, which <c>DYNA209</c> reports.
    /// In a nested path the last handle decides, so <c>ShipTo.At(Address.Attrs.City)</c> throws for an item without <c>ShipTo</c> when <c>City</c> cannot be null, which reading <c>ShipTo</c> itself would not.
    /// </para>
    /// <para>
    /// A <c>[KeyOnly]</c> property has no attribute and no handle. A stored property that a key of the table holds has both, and an item that an update created has only the key: a read of the entity restores
    /// the property from it, and this throws, which <c>DYNA209</c> reports.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be read: DynamoDB returns a shortened list, in which its position is not known.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public GetSelectCommand<TValue, TConverter> Select<TValue, TConverter>(Attr<TValue, TConverter> attribute)
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public GetSelectCommand<TValue?, NullableConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableHandleConverter<TValue, TConverter>> attribute)
        where TValue : struct
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Reads one nullable attribute instead of the entity, which gives null for an item without it. See <see cref="Select{TValue, TConverter}(Attr{TValue, TConverter})"/>.</summary>
    /// <typeparam name="TValue">The type of the attribute.</typeparam>
    /// <typeparam name="TConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or it is not valid.</exception>
    /// <exception cref="InvalidOperationException">The read has a <c>Project</c>, which has nothing to add here.</exception>
    public GetSelectCommand<TValue?, NullableReferenceConverter<TValue, TConverter>> Select<TValue, TConverter>(Attr<TValue, NullableReferenceHandleConverter<TValue, TConverter>> attribute)
        where TValue : class
        where TConverter : IDynamoDbConverter<TValue> => new(Selecting(attribute.Path));

    /// <summary>Asks for the consumed capacity, which goes to <see cref="GetResult{T}.ConsumedCapacity"/> and the capacity metric.</summary>
    /// <param name="detail">How much detail to return.</param>
    /// <returns>The command, which replaces this one.</returns>
    public GetCommand<T> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total)
    {
        var state = Enter();
        state.ReturnCapacity = detail;
        return new(state, state.Table);
    }

    /// <inheritdoc/>
    public GetCommand<T> MaxAttempts(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var state = Enter();
        state.MaxAttempts = attempts;
        return new(state, state.Table);
    }

    /// <summary>Sends the command. A missing item is not an error: <see cref="GetResult{T}.Found"/> is <see langword="false"/>.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<GetResult<T>> SendAsync(CancellationToken cancellationToken = default) =>
        Prepare().SendAsync<GetOperation<T>, GetResult<T>>(cancellationToken);

    /// <summary>
    /// Sends the command and returns the item, or <see langword="null"/> if none has the key: <c>var order = await orders.Get(key).ItemAsync(ct);</c>. It is the same request as
    /// <see cref="SendAsync"/>, which stays for <see cref="GetResult{T}.Found"/> and the capacity. An entity that is a struct reads as its default value when there is no item, so only
    /// <see cref="GetResult{T}.Found"/> tells a miss from an item of default values.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The item. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item cannot be read as <typeparamref name="T"/>.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<T?> ItemAsync(CancellationToken cancellationToken = default) =>
        Prepare().SendAsync<GetValueOperation<T>, T?>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command, for <c>await table.Get(pk, sk)</c>. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<GetResult<T>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => Prepare().RenderJson();

    private CommandState Selecting(string attribute)
    {
        Fluent.Selection.ThrowIfNoAttribute(attribute);
        var state = Enter();
        state.SelectAttribute(attribute, state.Table.DiscriminatorOnGet);
        return state;
    }

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);

    private CommandState Prepare()
    {
        var state = Enter();
        state.Table.AddRequiredNames(state);
        return state;
    }
}
