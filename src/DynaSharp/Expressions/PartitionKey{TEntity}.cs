using System.ComponentModel;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A formatted partition key that remembers its entity, which is what the generated <c>Order.Keys.Pk.Of(userId)</c> and <c>Order.Keys.ByStatusPk.Of(status)</c> return.
/// <c>orders.Query(Order.Keys.Pk.Of(id))</c> checks the key against the table, so <c>orders.Query(Customer.Keys.Pk.Of(id))</c> is a compile error. A table without a schema takes the
/// key of any entity, because it reads several: <c>shop.Query(Order.Keys.Pk.Of(id)).Of&lt;Customer, Order&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// It does not convert to <see cref="AttributeValue"/> on its own, because a conversion would let the key of another entity through the overload that takes one. <see cref="Value"/> is
/// the explicit way out, and the <see cref="AttributeValue"/> overloads of <c>Query</c> stay as the escape hatch.
/// </para>
/// <para>
/// The entity is all the type knows, so the partition key of a table and the one of its index have one type: the compiler does not tell them apart. The formatted string exists from the
/// moment the key is made, so this costs what <c>Format</c> costs.
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity the key belongs to.</typeparam>
public readonly struct PartitionKey<TEntity>
    where TEntity : IDynamoDbEntity<TEntity>
{
    /// <summary>Initializes a new instance of the <see cref="PartitionKey{TEntity}"/> struct. The generated <c>Of</c> calls this, and nothing here checks that the text fits the entity, so code of your own uses <c>Of</c>.</summary>
    /// <param name="value">The formatted key.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PartitionKey(string value)
    {
        Value = value;
    }

    /// <summary>Gets the formatted key. On the default value it is empty, which a query rejects as an empty key.</summary>
    public string Value => field ?? string.Empty;

    /// <summary>Returns the formatted key.</summary>
    /// <returns>The same as <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}
