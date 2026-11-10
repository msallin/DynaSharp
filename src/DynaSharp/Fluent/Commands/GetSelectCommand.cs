using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>GetItem</c> of one attribute, as <see cref="GetCommand{T}.Select{TValue, TConverter}(Attr{TValue, TConverter})"/> leaves it. Nothing can be added any more, which the type enforces, so
/// <c>Consistent</c> and the other clauses come before <c>Select</c>: all that is left is to send it. See <see cref="GetCommand{T}"/> for what a command is and what it cannot do.
/// </summary>
/// <typeparam name="TValue">The type of the attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter">The converter that reads it.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct GetSelectCommand<TValue, TConverter>
    where TConverter : IDynamoDbConverter<TValue>
{
    private const string Operation = "GetItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal GetSelectCommand(CommandState state)
    {
        _state = state;
        _version = state.Version;
        _table = state.Table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>
    /// Reads one more attribute in the same request, so that the read gives a tuple: <c>var (total, status) = (await orders.Get(key).Select(Order.Attrs.Total).And(Order.Attrs.Status).SendAsync(ct)).Value;</c>.
    /// Up to four attributes; it saves bandwidth, not capacity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rules of one attribute.</b> Each attribute is read as <c>Select</c> reads one: a missing one follows the type of its handle, null for a nullable one, an empty set for a set, and otherwise a
    /// <see cref="DynamoDbMappingException"/>. The projection names these attributes and nothing else, with the discriminator where <c>Select</c> adds it, so a <c>required</c> member cannot be in the way.
    /// </para>
    /// <para>
    /// <b>The order of the tuple</b> is the order of the calls: <c>Select(a).And(b)</c> gives <c>(a, b)</c>. An attribute that the read gives already throws, as does a list element, and the command stays as it was.
    /// </para>
    /// </remarks>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps. A list element cannot be read: DynamoDB returns a shortened list, in which its position is not known.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public GetSelectCommand<TValue, TConverter, TNext, TNextConverter> And<TNext, TNextConverter>(Attr<TNext, TNextConverter> attribute)
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>Reads one more nullable attribute in the same request, which gives null for an item without it. See <see cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})"/>.</summary>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public GetSelectCommand<TValue, TConverter, TNext?, NullableConverter<TNext, TNextConverter>> And<TNext, TNextConverter>(Attr<TNext, NullableHandleConverter<TNext, TNextConverter>> attribute)
        where TNext : struct
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>Reads one more nullable attribute in the same request, which gives null for an item without it. See <see cref="GetSelectCommand{TValue, TConverter}.And{TNext, TNextConverter}(Attr{TNext, TNextConverter})"/>.</summary>
    /// <typeparam name="TNext">The type of the attribute.</typeparam>
    /// <typeparam name="TNextConverter">The converter of the attribute.</typeparam>
    /// <param name="attribute">The attribute, a name or a path through maps.</param>
    /// <returns>The command that is left.</returns>
    /// <exception cref="ArgumentException">The handle is a default value, its path has a list index, or the read gives that attribute already.</exception>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public GetSelectCommand<TValue, TConverter, TNext?, NullableReferenceConverter<TNext, TNextConverter>> And<TNext, TNextConverter>(Attr<TNext, NullableReferenceHandleConverter<TNext, TNextConverter>> attribute)
        where TNext : class
        where TNextConverter : IDynamoDbConverter<TNext> => new(Selection.Another(_state, _version, Operation, _table, attribute.Path));

    /// <summary>
    /// Sends the command. An item that does not exist is not an error: <see cref="SelectResult{TValue}.Found"/> is <see langword="false"/>, also for a handle that cannot be null. An attribute that the item does
    /// not have follows the type of the handle: null for a nullable one, an empty set for a set, and otherwise a <see cref="DynamoDbMappingException"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item has no such attribute and the handle cannot be null, the value cannot be read as <typeparamref name="TValue"/>, or the item is of another type.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<SelectResult<TValue>> SendAsync(CancellationToken cancellationToken = default) =>
        Enter().SendAsync<GetSelectOperation<TValue, SelectedItem<TValue, TConverter>>, SelectResult<TValue>>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<SelectResult<TValue>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, and is meant for tests and debugging. It shows what goes out: the attribute, and the
    /// discriminator if the entity has one. The text contains the key.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
