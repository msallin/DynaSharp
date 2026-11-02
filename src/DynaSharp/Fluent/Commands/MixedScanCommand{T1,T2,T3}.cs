using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>Scan</c> that reads several entity types of a table or index: <c>shop.Scan().Of&lt;Customer, Order, Invoice&gt;()</c>. It has the clauses of <see cref="ScanCommand{T}"/> and
/// its terminals but <c>CountAsync</c>, and <see cref="MixedQueryCommand{T1, T2, T3}"/> says what the mix adds.
/// </summary>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct MixedScanCommand<T1, T2, T3> : IFilterable<MixedScanCommand<T1, T2, T3>>, IRetryable<MixedScanCommand<T1, T2, T3>>, IReadCommand
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
{
    private readonly ScanCommand<Mixed<T1, T2, T3>> _command;

    internal MixedScanCommand(ScanCommand<Mixed<T1, T2, T3>> command)
    {
        _command = command;
    }

    internal string DebuggerText => _command.DebuggerText;

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(MixedScanCommand<T1, T2, T3> command) => command._command.Handle;

    /// <inheritdoc cref="ScanCommand{T}.Filter(ExpressionHandler)"/>
    public MixedScanCommand<T1, T2, T3> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition) => new(_command.Filter(condition));

    /// <inheritdoc/>
    public MixedScanCommand<T1, T2, T3> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition => new(_command.Filter(condition));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{string})"/>
    public MixedScanCommand<T1, T2, T3> Project(params ReadOnlySpan<string> paths) => new(_command.Project(paths));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{Attr})"/>
    public MixedScanCommand<T1, T2, T3> Project(params ReadOnlySpan<Attr> attributes) => new(_command.Project(attributes));

    /// <inheritdoc cref="ScanCommand{T}.Limit"/>
    public MixedScanCommand<T1, T2, T3> Limit(int items) => new(_command.Limit(items));

    /// <inheritdoc cref="ScanCommand{T}.Consistent"/>
    public MixedScanCommand<T1, T2, T3> Consistent() => new(_command.Consistent());

    /// <inheritdoc cref="ScanCommand{T}.ReturnCapacity"/>
    public MixedScanCommand<T1, T2, T3> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total) => new(_command.ReturnCapacity(detail));

    /// <inheritdoc/>
    public MixedScanCommand<T1, T2, T3> MaxAttempts(int attempts) => new(_command.MaxAttempts(attempts));

    /// <inheritdoc cref="ScanCommand{T}.Segment"/>
    public MixedScanCommand<T1, T2, T3> Segment(int segment, int of) => new(_command.Segment(segment, of));

    /// <inheritdoc cref="ScanCommand{T}.Parallel"/>
    public ParallelScanCommand<Mixed<T1, T2, T3>> Parallel(int segments)
    {
        CompleteProjection();
        return _command.Parallel(segments);
    }

    /// <inheritdoc cref="ScanCommand{T}.ItemsAsync"/>
    public IAsyncEnumerable<Mixed<T1, T2, T3>> ItemsAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.ItemsAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PagesAsync"/>
    public IAsyncEnumerable<Page<Mixed<T1, T2, T3>>> PagesAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PagesAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PageAsync(ContinuationToken, CancellationToken)"/>
    public ValueTask<Page<Mixed<T1, T2, T3>>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PageAsync(after, cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PageAsync(CancellationToken)"/>
    public ValueTask<Page<Mixed<T1, T2, T3>>> PageAsync(CancellationToken cancellationToken) => PageAsync(default, cancellationToken);

    /// <inheritdoc cref="ScanCommand{T}.ToRequestJson"/>
    public string ToRequestJson()
    {
        CompleteProjection();
        return _command.ToRequestJson();
    }

    private void CompleteProjection() => Mixed<T1, T2, T3>.Schema.CompleteProjection(_command.Handle);
}
