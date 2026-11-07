using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> that reads several entity types of one partition, the adjacency list of a single table: <c>shop.Query(pk).Of&lt;Customer, Order&gt;()</c>. It has the
/// clauses of <see cref="QueryCommand{T}"/> and every way to send it but <c>CountAsync</c>, because a count has nothing to dispatch.
/// </summary>
/// <remarks>
/// <para>
/// The filter gains <c>#_type IN (:a, :b)</c>, in the request for everyone to see, so items of other types never reach the client. Filters run after the read:
/// <see cref="Page{T}.ScannedCount"/> exceeds <see cref="Page{T}.Count"/> by the items of other types, and capacity is charged for them all.
/// A table or index keyed by the discriminator is the exception, because DynamoDB refuses a filter on a key of what a query reads: the request has none, the key condition has to keep other
/// types out, and an item of another type throws <see cref="DynamoDbMappingException"/>.
/// </para>
/// <para>
/// An item finds its type through its discriminator, wherever that stands among its attributes, and is read by the mapping of that type with no <see cref="Item"/> in
/// between. An item that a type cannot read throws <see cref="DynamoDbMappingException"/>. A projection gains the discriminator and the keys of the types.
/// </para>
/// </remarks>
/// <typeparam name="TMixed">The item of the read, <see cref="Mixed{T1, T2}"/> or one of its siblings of 3 and 4 types, which name the entity types.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct MixedQueryCommand<TMixed> : IFilterable<MixedQueryCommand<TMixed>>, IRetryable<MixedQueryCommand<TMixed>>, IReadCommand
    where TMixed : IMixed<TMixed>
{
    private readonly QueryCommand<TMixed> _command;

    internal MixedQueryCommand(QueryCommand<TMixed> command)
    {
        _command = command;
    }

    internal string DebuggerText => _command.DebuggerText;

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(MixedQueryCommand<TMixed> command) => command._command.Handle;

    /// <inheritdoc cref="QueryCommand{T}.Filter(ExpressionHandler)"/>
    public MixedQueryCommand<TMixed> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition) => new(_command.Filter(condition));

    /// <inheritdoc/>
    public MixedQueryCommand<TMixed> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition => new(_command.Filter(condition));

    /// <inheritdoc cref="QueryCommand{T}.Project(ReadOnlySpan{string})"/>
    public MixedQueryCommand<TMixed> Project(params ReadOnlySpan<string> paths) => new(_command.Project(paths));

    /// <inheritdoc cref="QueryCommand{T}.Project(ReadOnlySpan{Attr})"/>
    public MixedQueryCommand<TMixed> Project(params ReadOnlySpan<Attr> attributes) => new(_command.Project(attributes));

    /// <inheritdoc cref="QueryCommand{T}.Limit"/>
    public MixedQueryCommand<TMixed> Limit(int items) => new(_command.Limit(items));

    /// <inheritdoc cref="QueryCommand{T}.Descending()"/>
    public MixedQueryCommand<TMixed> Descending() => new(_command.Descending());

    /// <inheritdoc cref="QueryCommand{T}.Descending(bool)"/>
    public MixedQueryCommand<TMixed> Descending(bool descending) => new(_command.Descending(descending));

    /// <inheritdoc cref="QueryCommand{T}.Consistent"/>
    public MixedQueryCommand<TMixed> Consistent() => new(_command.Consistent());

    /// <inheritdoc cref="QueryCommand{T}.ReturnCapacity"/>
    public MixedQueryCommand<TMixed> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total) => new(_command.ReturnCapacity(detail));

    /// <summary>Skips an item that none of the types can read, or that its type cannot read, and hands its <see cref="DynamoDbMappingException"/> to <paramref name="onFailure"/>, where the read would otherwise end with it. See <see cref="QueryCommand{T}.OnMappingFailure"/>.</summary>
    /// <param name="onFailure">What to do with the failure of a skipped item.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public MixedQueryCommand<TMixed> OnMappingFailure(Action<DynamoDbMappingException> onFailure) => new(_command.OnMappingFailure(onFailure));

    /// <inheritdoc/>
    public MixedQueryCommand<TMixed> MaxAttempts(int attempts) => new(_command.MaxAttempts(attempts));

    /// <inheritdoc cref="QueryCommand{T}.ItemsAsync"/>
    public IAsyncEnumerable<TMixed> ItemsAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.ItemsAsync(cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PagesAsync"/>
    public IAsyncEnumerable<Page<TMixed>> PagesAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PagesAsync(cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PageAsync(ContinuationToken?, CancellationToken)"/>
    public ValueTask<Page<TMixed>> PageAsync(ContinuationToken? after = null, CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PageAsync(after, cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PageAsync(CancellationToken)"/>
    public ValueTask<Page<TMixed>> PageAsync(CancellationToken cancellationToken) => PageAsync(null, cancellationToken);

    /// <inheritdoc cref="QueryCommand{T}.ToRequestJson"/>
    public string ToRequestJson()
    {
        CompleteProjection();
        return _command.ToRequestJson();
    }

    private void CompleteProjection() => TMixed.Schema.CompleteProjection(_command.Handle);
}
