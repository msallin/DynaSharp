using DynaSharp.Fluent;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// An item of a mixed read, <see cref="Mixed{T1, T2}"/> and its siblings of 3 and 4 types: what <see cref="MixedQueryCommand{TMixed}"/> and <see cref="MixedScanCommand{TMixed}"/>
/// read. Only the <c>Mixed</c> types implement it.
/// </summary>
/// <typeparam name="TSelf">The <c>Mixed</c> type itself.</typeparam>
public interface IMixed<TSelf> : IDynamoDbDocument<TSelf>
    where TSelf : IMixed<TSelf>
{
    /// <summary>Gets what the mix knows about its types.</summary>
    internal static abstract MixedSchema Schema { get; }
}
