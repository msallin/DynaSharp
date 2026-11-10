using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// A <c>GetItem</c> of four attributes, as <c>And</c> leaves it: what is left is the send, because four attributes are the most. Each attribute follows the rules of
/// <see cref="GetSelectCommand{TValue, TConverter}"/>, and the values come as a tuple in the order of the calls.
/// </summary>
/// <typeparam name="T1">The type of the first attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter1">The converter that reads the first attribute.</typeparam>
/// <typeparam name="T2">The type of the second attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter2">The converter that reads the second attribute.</typeparam>
/// <typeparam name="T3">The type of the third attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter3">The converter that reads the third attribute.</typeparam>
/// <typeparam name="T4">The type of the fourth attribute, nullable for a handle of a nullable property.</typeparam>
/// <typeparam name="TConverter4">The converter that reads the fourth attribute.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
[SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1414:Tuple types in signatures should have element names", Justification = "The elements are the attributes in the order of the calls, which a name would only repeat; a caller deconstructs them.")]
public readonly ref struct GetSelectCommand<T1, TConverter1, T2, TConverter2, T3, TConverter3, T4, TConverter4>
    where TConverter1 : IDynamoDbConverter<T1>
    where TConverter2 : IDynamoDbConverter<T2>
    where TConverter3 : IDynamoDbConverter<T3>
    where TConverter4 : IDynamoDbConverter<T4>
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

    /// <summary>
    /// Sends the command. An item that does not exist is not an error: <see cref="SelectResult{TValue}.Found"/> is <see langword="false"/>. An attribute that the item does not have follows the type of its handle,
    /// as for <see cref="GetSelectCommand{TValue, TConverter}.SendAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result, whose value holds the attributes in the order of the calls. Await it once.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request, for example because the key does not fit the table.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbMappingException">The item has no attribute that its handle cannot do without, a value cannot be read as the type of its handle, or the item is of another type.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<SelectResult<(T1, T2, T3, T4)>> SendAsync(CancellationToken cancellationToken = default) =>
        Enter().SendAsync<GetSelectOperation<(T1, T2, T3, T4), SelectedItem<T1, TConverter1, T2, TConverter2, T3, TConverter3, T4, TConverter4>>, SelectResult<(T1, T2, T3, T4)>>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<SelectResult<(T1, T2, T3, T4)>> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, and is meant for tests and debugging. It shows what goes out: the attributes, and the
    /// discriminator if the entity has one. The text contains the key.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent or rendered.</exception>
    public string ToRequestJson() => Enter().RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
