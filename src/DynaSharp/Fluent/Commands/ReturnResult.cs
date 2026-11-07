using System.Diagnostics.CodeAnalysis;

namespace DynaSharp;

/// <summary>
/// What <see cref="ReturnCommand{TValue, TConverter}.TrySendAsync"/> returns: whether the condition of the update held, and then the attribute as it is after the update. A condition
/// that did not hold wrote nothing and has no value.
/// </summary>
/// <example>
/// <code>
/// var claimed = await products.Update(Product.Keys.Of("sku-1")).Add(Product.Attrs.Stock, -1).If(Product.Attrs.Stock.Gt(0)).Return(Product.Attrs.Stock).TrySendAsync(ct);
/// int? left = claimed.ConditionMet ? claimed.Value : null;
/// </code>
/// </example>
/// <typeparam name="TValue">The type of the attribute.</typeparam>
public readonly struct ReturnResult<TValue>
{
    internal ReturnResult(TValue value)
    {
        Value = value;
    }

    private ReturnResult(bool conditionFailed)
    {
        ConditionFailed = conditionFailed;
    }

    /// <summary>
    /// Gets a value indicating whether the condition of the update held, so that the update took effect and <see cref="Value"/> holds the attribute. <see langword="false"/> means that nothing was
    /// written, and it is the one answer that does not throw.
    /// </summary>
    /// <remarks>
    /// After a response that was lost, the client sends the update again, and one whose condition rules out its own result then fails against itself, which on the wire is the same as a condition that
    /// did not hold. An update that must tell the two apart uses <see cref="UpdateCommand{T}.TrySendAsync"/> with <c>ReturnOldOnFailure()</c>, whose item shows what is stored.
    /// </remarks>
    [MemberNotNullWhen(true, nameof(Value))]
    public bool ConditionMet => !ConditionFailed;

    /// <summary>Gets the attribute as it is after the update, or the default value of <typeparamref name="TValue"/> when <see cref="ConditionMet"/> is <see langword="false"/>.</summary>
    public TValue? Value { get; }

    /// <summary>Gets the answer of an update whose condition did not hold.</summary>
    internal static ReturnResult<TValue> Failed { get; } = new(conditionFailed: true);

    // Kept the other way round, so that the default value of the struct reads as an update whose condition held, as WriteResult<T> does.
    private bool ConditionFailed { get; }
}
