using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A <c>Scan</c> that reads several entity types of a table or index: <c>shop.Scan().Of&lt;Customer, Order, Invoice, Payment&gt;()</c>. It has the clauses of <see cref="ScanCommand{T}"/> and
/// its terminals but <c>CountAsync</c>, and <see cref="MixedQueryCommand{T1, T2, T3, T4}"/> says what the mix adds.
/// </summary>
/// <typeparam name="T1">The first type.</typeparam>
/// <typeparam name="T2">The second type.</typeparam>
/// <typeparam name="T3">The third type.</typeparam>
/// <typeparam name="T4">The fourth type.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct MixedScanCommand<T1, T2, T3, T4> : IFilterable<MixedScanCommand<T1, T2, T3, T4>>, IRetryable<MixedScanCommand<T1, T2, T3, T4>>
    where T1 : IDynamoDbDiscriminated<T1>
    where T2 : IDynamoDbDiscriminated<T2>
    where T3 : IDynamoDbDiscriminated<T3>
    where T4 : IDynamoDbDiscriminated<T4>
{
    private readonly ScanCommand<Mixed<T1, T2, T3, T4>> _command;

    internal MixedScanCommand(ScanCommand<Mixed<T1, T2, T3, T4>> command)
    {
        _command = command;
    }

    internal string DebuggerText => _command.DebuggerText;

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(MixedScanCommand<T1, T2, T3, T4> command) => command._command.Handle;

    /// <inheritdoc cref="ScanCommand{T}.Filter(ExpressionHandler)"/>
    public MixedScanCommand<T1, T2, T3, T4> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition) => new(_command.Filter(condition));

    /// <inheritdoc/>
    public MixedScanCommand<T1, T2, T3, T4> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition => new(_command.Filter(condition));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{string})"/>
    public MixedScanCommand<T1, T2, T3, T4> Project(params ReadOnlySpan<string> paths) => new(_command.Project(paths));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{Attr})"/>
    public MixedScanCommand<T1, T2, T3, T4> Project(params ReadOnlySpan<Attr> attributes) => new(_command.Project(attributes));

    /// <inheritdoc cref="ScanCommand{T}.Limit"/>
    public MixedScanCommand<T1, T2, T3, T4> Limit(int items) => new(_command.Limit(items));

    /// <inheritdoc cref="ScanCommand{T}.Consistent"/>
    public MixedScanCommand<T1, T2, T3, T4> Consistent() => new(_command.Consistent());

    /// <inheritdoc cref="ScanCommand{T}.ReturnCapacity"/>
    public MixedScanCommand<T1, T2, T3, T4> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total) => new(_command.ReturnCapacity(detail));

    /// <inheritdoc/>
    public MixedScanCommand<T1, T2, T3, T4> MaxAttempts(int attempts) => new(_command.MaxAttempts(attempts));

    /// <inheritdoc cref="ScanCommand{T}.Segment"/>
    public MixedScanCommand<T1, T2, T3, T4> Segment(int segment, int of) => new(_command.Segment(segment, of));

    /// <inheritdoc cref="ScanCommand{T}.Parallel"/>
    public ParallelScanCommand<Mixed<T1, T2, T3, T4>> Parallel(int segments)
    {
        CompleteProjection();
        return _command.Parallel(segments);
    }

    /// <inheritdoc cref="ScanCommand{T}.ItemsAsync"/>
    public IAsyncEnumerable<Mixed<T1, T2, T3, T4>> ItemsAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.ItemsAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PagesAsync"/>
    public IAsyncEnumerable<Page<Mixed<T1, T2, T3, T4>>> PagesAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PagesAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PageAsync"/>
    public ValueTask<Page<Mixed<T1, T2, T3, T4>>> PageAsync(ContinuationToken after = default, CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PageAsync(after, cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.ToRequestJson"/>
    public string ToRequestJson()
    {
        CompleteProjection();
        return _command.ToRequestJson();
    }

    private void CompleteProjection() => Mixed<T1, T2, T3, T4>.Schema.CompleteProjection(_command.Handle);
}
