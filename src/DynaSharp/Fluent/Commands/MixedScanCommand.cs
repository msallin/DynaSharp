using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DynaSharp.Expressions;
using DynaSharp.Fluent;

namespace DynaSharp;

/// <summary>
/// A <c>Scan</c> that reads several entity types of a table or index: <c>shop.Scan().Of&lt;Customer, Order&gt;()</c>. It has the clauses of <see cref="ScanCommand{T}"/> and
/// every way to send it but <c>CountAsync</c>, and <see cref="MixedQueryCommand{TMixed}"/> says what the mix adds.
/// </summary>
/// <typeparam name="TMixed">The item of the read, <see cref="Mixed{T1, T2}"/> or one of its siblings of 3 and 4 types, which name the entity types.</typeparam>
[DebuggerDisplay("{DebuggerText,nq}")]
public readonly ref struct MixedScanCommand<TMixed> : IFilterable<MixedScanCommand<TMixed>>, IRetryable<MixedScanCommand<TMixed>>, IReadCommand
    where TMixed : IMixed<TMixed>
{
    private readonly ScanCommand<TMixed> _command;

    internal MixedScanCommand(ScanCommand<TMixed> command)
    {
        _command = command;
    }

    internal string DebuggerText => _command.DebuggerText;

    /// <summary>Converts a command for the compiler, which passes it to the handler of an interpolated filter.</summary>
    /// <param name="command">The command.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static implicit operator CommandHandle(MixedScanCommand<TMixed> command) => command._command.Handle;

    /// <inheritdoc cref="ScanCommand{T}.Filter(ExpressionHandler)"/>
    public MixedScanCommand<TMixed> Filter([InterpolatedStringHandlerArgument("")] ExpressionHandler condition) => new(_command.Filter(condition));

    /// <inheritdoc/>
    public MixedScanCommand<TMixed> Filter<TCondition>(TCondition condition)
        where TCondition : ICondition => new(_command.Filter(condition));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{string})"/>
    public MixedScanCommand<TMixed> Project(params ReadOnlySpan<string> paths) => new(_command.Project(paths));

    /// <inheritdoc cref="ScanCommand{T}.Project(ReadOnlySpan{Attr})"/>
    public MixedScanCommand<TMixed> Project(params ReadOnlySpan<Attr> attributes) => new(_command.Project(attributes));

    /// <inheritdoc cref="ScanCommand{T}.Limit"/>
    public MixedScanCommand<TMixed> Limit(int items) => new(_command.Limit(items));

    /// <inheritdoc cref="ScanCommand{T}.Take"/>
    public MixedScanCommand<TMixed> Take(int items) => new(_command.Take(items));

    /// <inheritdoc cref="ScanCommand{T}.Consistent"/>
    public MixedScanCommand<TMixed> Consistent() => new(_command.Consistent());

    /// <inheritdoc cref="ScanCommand{T}.ReturnCapacity"/>
    public MixedScanCommand<TMixed> ReturnCapacity(ReturnConsumedCapacity detail = ReturnConsumedCapacity.Total) => new(_command.ReturnCapacity(detail));

    /// <summary>Skips an item that none of the types can read, or that its type cannot read, and hands its <see cref="DynamoDbMappingException"/> to <paramref name="onFailure"/>, where the read would otherwise end with it. See <see cref="ScanCommand{T}.OnMappingFailure"/>.</summary>
    /// <param name="onFailure">What to do with the failure of a skipped item.</param>
    /// <returns>The command, which replaces this one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="onFailure"/> is <see langword="null"/>.</exception>
    public MixedScanCommand<TMixed> OnMappingFailure(Action<DynamoDbMappingException> onFailure) => new(_command.OnMappingFailure(onFailure));

    /// <inheritdoc/>
    public MixedScanCommand<TMixed> MaxAttempts(int attempts) => new(_command.MaxAttempts(attempts));

    /// <inheritdoc cref="ScanCommand{T}.Segment"/>
    public MixedScanCommand<TMixed> Segment(int segment, int of) => new(_command.Segment(segment, of));

    /// <inheritdoc cref="ScanCommand{T}.Parallel"/>
    public ParallelScanCommand<TMixed> Parallel(int segments)
    {
        CompleteProjection();
        return _command.Parallel(segments);
    }

    /// <inheritdoc cref="ScanCommand{T}.ItemsAsync"/>
    public IAsyncEnumerable<TMixed> ItemsAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.ItemsAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PagesAsync"/>
    public IAsyncEnumerable<Page<TMixed>> PagesAsync(CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PagesAsync(cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PageAsync(ContinuationToken?, CancellationToken)"/>
    public ValueTask<Page<TMixed>> PageAsync(ContinuationToken? after = null, CancellationToken cancellationToken = default)
    {
        CompleteProjection();
        return _command.PageAsync(after, cancellationToken);
    }

    /// <inheritdoc cref="ScanCommand{T}.PageAsync(CancellationToken)"/>
    public ValueTask<Page<TMixed>> PageAsync(CancellationToken cancellationToken) => PageAsync(null, cancellationToken);

    /// <inheritdoc cref="ScanCommand{T}.ToRequestJson"/>
    public string ToRequestJson()
    {
        CompleteProjection();
        return _command.ToRequestJson();
    }

    private void CompleteProjection() => TMixed.Schema.CompleteProjection(_command.Handle);
}
