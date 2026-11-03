using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>Query</c> that reads several entity types of one partition, the adjacency list of a single table: <c>shop.Query(pk).Of&lt;Customer, Order&gt;()</c>. It has the
/// clauses of <see cref="QueryCommand{T}"/> and its terminals but <c>CountAsync</c>, because a count has nothing to dispatch.
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
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct MixedQueryCommand<T1, T2> : IFilterable<MixedQueryCommand<T1, T2>>, IRetryable<MixedQueryCommand<T1, T2>>, IReadCommand
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
{
    private readonly QueryCommand<Mixed<T1, T2>> _command;

    internal MixedQueryCommand(QueryCommand<Mixed<T1, T2>> command)
    {
        _command = command;
    }

    internal string DebuggerText => _command.DebuggerText;

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(MixedQueryCommand<T1, T2> command) => command._command.Handle;

    /// <inheritdoc cref="QueryCommand{T}.Filter(ExpressionHandler)"/>
    public MixedQueryCommand<T1, T2> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition) => new(_command.Filter(condition));

    /// <inheritdoc/>
    public MixedQueryCommand<T1, T2> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition => new(_command.Filter(condition));

    /// <inheritdoc cref="QueryCommand{T}.Project(ReadOnlySpan{string})"/>
    public MixedQueryCommand<T1, T2> Project(params ReadOnlySpan<string> paths) => new(_command.Project(paths));

    /// <inheritdoc cref="QueryCommand{T}.Project(ReadOnlySpan{Attr})"/>
    public MixedQueryCommand<T1, T2> Project(params ReadOnlySpan<Attr> attributes) => new(_command.Project(attributes));

    /// <inheritdoc cref="QueryCommand{T}.Limit"/>
    public MixedQueryCommand<T1, T2> Limit(int items) => new(_command.Limit(items));

    /// <inheritdoc cref="QueryCommand{T}.Descending"/>
    public MixedQueryCommand<T1, T2> Descending() => new(_command.Descending());

    /// <inheritdoc cref="QueryCommand{T}.Consistent"/>
    public MixedQueryCommand<T1, T2> Consistent() => new(_command.Consistent());

    /// <inheritdoc cref="QueryCommand{T}.ReturnCapacity"/>
    public MixedQueryCommand<T1, T2> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total) => new(_command.ReturnCapacity(detail));

    /// <summary>Skips an item that none of the types can read, or that its type cannot read, and hands its <see cref="DynamoDbMappingException"/> to <paramref name="onFailure"/>, where the read would otherwise end with it. See <see cref="QueryCommand{T}.OnMappingFailure"/>.</summary>
    /// <param name="onFailure">What to do with the failure of a skipped item.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public MixedQueryCommand<T1, T2> OnMappingFailure(Action<DynamoDbMappingException> onFailure) => new(_command.OnMappingFailure(onFailure));

    /// <inheritdoc/>
    public MixedQueryCommand<T1, T2> MaxAttempts(int attempts) => new(_command.MaxAttempts(attempts));

    /// <inheritdoc cref="QueryCommand{T}.ItemsAsync"/>
    public IAsyncEnumerable<Mixed<T1, T2>> ItemsAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.ItemsAsync(cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PagesAsync"/>
    public IAsyncEnumerable<Page<Mixed<T1, T2>>> PagesAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PagesAsync(cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PageAsync(ContinuationToken, CancellationToken)"/>
    public ValueTask<Page<Mixed<T1, T2>>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PageAsync(after, cancellationToken);
    }

    /// <inheritdoc cref="QueryCommand{T}.PageAsync(CancellationToken)"/>
    public ValueTask<Page<Mixed<T1, T2>>> PageAsync(CancellationToken cancellationToken) => PageAsync(default, cancellationToken);

    /// <inheritdoc cref="QueryCommand{T}.ToRequestJson"/>
    public string ToRequestJson()
    {
        CompleteProjection();
        return _command.ToRequestJson();
    }

    private void CompleteProjection() => Mixed<T1, T2>.Schema.CompleteProjection(_command.Handle);
}
