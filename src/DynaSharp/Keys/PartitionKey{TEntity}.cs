using System.ComponentModel;
using DynaSharp.Mapping;

namespace DynaSharp;

/// <summary>
/// A partition key that remembers its entity, which is what the generated <c>Order.Keys.Pk.Of(userId)</c> and <c>Order.Keys.ByStatusPk.Of(status)</c> return.
/// <c>orders.Query(Order.Keys.Pk.Of(id))</c> checks the key against the table, so <c>orders.Query(Customer.Keys.Pk.Of(id))</c> is a compile error. A table without a schema takes the
/// key of any entity, because it reads several: <c>shop.Query(Order.Keys.Pk.Of(id)).Of&lt;Customer, Order&gt;()</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>No conversion.</b> It does not convert to <see cref="AttributeValue"/> on its own: a table of an entity takes no key given as its stored values, which would skip the template, so a key
/// of another entity would have a way through. <see cref="Value"/> is the explicit way out, and a table without a schema, <c>client.Table(name, partitionKey, sortKey)</c>, takes the values.
/// </para>
/// <para>
/// The entity is all the type knows, so the partition key of a table and the one of its index have one type: the compiler does not tell them apart. The key remembers the attribute it was built
/// for, so <c>Query</c> does, and throws <see cref="ArgumentException"/> naming the key builder to use instead of asking DynamoDB for a partition that cannot exist. Two keys of one attribute are one
/// key to it, which is what lets several entities overload an index. The formatted string exists from the moment the key is made, so this costs what <c>Format</c> costs.
/// </para>
/// <para>
/// A key holds an <see cref="AttributeValue"/>, a struct, because a plain key can be a number or binary, which a string would send with the wrong type. A key of a template is a string in that
/// struct at no further cost, and a plain key of another type pays for its text once.
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
        Key = value;
    }

    /// <summary>Initializes a new instance of the <see cref="PartitionKey{TEntity}"/> struct. The generated <c>Of</c> calls this, and nothing here checks that the text fits the entity, so code of your own uses <c>Of</c>.</summary>
    /// <param name="value">The formatted key.</param>
    /// <param name="attributeName">The attribute the key was built for, which <c>Query</c> compares with what it reads.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PartitionKey(string value, string attributeName)
    {
        Key = value;
        AttributeName = attributeName;
    }

    /// <summary>Initializes a new instance of the <see cref="PartitionKey{TEntity}"/> struct. The generated <c>Of</c> of a plain key calls this, for a key that may be a number or binary.</summary>
    /// <param name="value">The key as it is stored.</param>
    /// <param name="attributeName">The attribute the key was built for, which <c>Query</c> compares with what it reads.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PartitionKey(AttributeValue value, string attributeName)
    {
        Key = value;
        AttributeName = attributeName;
    }

    /// <summary>Gets the key as text: the formatted string, the digits of a number, the base64 of binary. On the default value it is empty, which a query rejects as an empty key.</summary>
    public string Value => Key.Value switch
    {
        string text => text,
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => string.Empty,
    };

    /// <summary>Gets the key as it goes to DynamoDB, with its type.</summary>
    internal AttributeValue Key { get; }

    /// <summary>Gets the attribute the key was built for, or <see langword="null"/> for a key that does not say, which fits every query.</summary>
    internal string? AttributeName { get; }

    /// <summary>Returns the formatted key.</summary>
    /// <returns>The same as <see cref="Value"/>.</returns>
    public override string ToString() => Value;
}
