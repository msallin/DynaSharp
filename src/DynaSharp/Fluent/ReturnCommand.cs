using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>
/// An update that returns one attribute, as <see cref="UpdateCommand{T}.Return{TValue, TConverter}(Attr{TValue, TConverter})"/> leaves it. Nothing can be
/// added any more, which the type enforces: all that is left is to send it.
/// </summary>
/// <remarks>The consumed capacity of such an update reaches the capacity metric and no other place: the result is the value.</remarks>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
/// <typeparam name="TConverter">The converter.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct ReturnCommand<TValue, TConverter>
    where TConverter : IDynamoDbConverter<TValue>
{
    private const string Operation = "UpdateItem";

    private readonly CommandState? _state;
    private readonly int _version;
    private readonly TableModel? _table;

    internal ReturnCommand(CommandState state, TableModel table)
    {
        _state = state;
        _version = state.Version;
        _table = table;
    }

    internal string DebuggerText => CommandDebugger.Describe(Operation, _state, _version, _table);

    internal CommandHandle Handle => new(_state, _version, Operation, _table);

    /// <summary>Sends the command.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The attribute as it is after the update. Await it once.</returns>
    /// <exception cref="InvalidOperationException">
    /// The command was already sent, extended or rendered, an entity with a <see cref="VersionAttribute"/> has neither <c>IfVersion</c> nor <c>IgnoreVersion</c>, version 0 stands beside <c>IfExists</c> without <see cref="VersionMissing.Allow"/>, or a change by handle cannot
    /// leave a key consistent, see <see cref="IUpdatable{TSelf}.Set{TValue, TConverter}(Attr{TValue, TConverter}, TValue)"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The client is disposed.</exception>
    /// <exception cref="DynamoDbMappingException">
    /// The response does not hold the attribute, because the update did not change it, or it cannot be read as <typeparamref name="TValue"/>. The update
    /// has been applied, so it must not simply be repeated.
    /// </exception>
    /// <exception cref="ConditionalCheckFailedException">A condition did not hold, so nothing was written.</exception>
    /// <exception cref="ValidationException">DynamoDB rejects the request; the exception shows the expressions.</exception>
    /// <exception cref="ResourceNotFoundException">The table does not exist.</exception>
    /// <exception cref="DynamoDbException">DynamoDB returns any other error; a throttling or server error only after the last attempt.</exception>
    public ValueTask<TValue> SendAsync(CancellationToken cancellationToken = default) =>
        WriteRules.Settle(Enter()).SendAsync<UpdateValueOperation<TValue, TConverter>, TValue>(cancellationToken);

    /// <summary>Lets <c>await</c> send the command. There is no cancellation that way; <see cref="SendAsync"/> takes a token.</summary>
    /// <returns>The awaiter of the sent command.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTaskAwaiter<TValue> GetAwaiter() => SendAsync().GetAwaiter();

    /// <summary>
    /// Renders the body this command would send, as DynamoDB JSON, without sending it. It ends the command, allocates one string, and is
    /// meant for tests and debugging. The text contains the key and the values.
    /// </summary>
    /// <returns>The request body.</returns>
    /// <exception cref="InvalidOperationException">The command was already sent, extended or rendered.</exception>
    public string ToRequestJson() => WriteRules.Settle(Enter()).RenderJson();

    private CommandState Enter() => CommandState.Enter(_state, _version, Operation, _table);
}
