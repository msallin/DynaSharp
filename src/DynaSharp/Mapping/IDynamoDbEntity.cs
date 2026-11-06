using DynaSharp.Serialization;

namespace DynaSharp.Mapping;

/// <summary>
/// A document with a primary key, which is what can stand on its own in a table. A nested document such as an address has no key, so
/// it implements <see cref="IDynamoDbDocument{TSelf}"/> only, and <see cref="Table{T}.Of{TOther}"/> and
/// <see cref="DynamoDbClient.Table{T}(string)"/> do not take it.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IDynamoDbEntity<TSelf> : IDynamoDbDocument<TSelf>
    where TSelf : IDynamoDbEntity<TSelf>
{
    /// <summary>Gets what the type knows about its table: key names, indexes, discriminator and version attribute.</summary>
    public static abstract DynamoDbEntityInfo Info { get; }

    /// <inheritdoc/>
    static DynamoDbEntityInfo? IDynamoDbDocument<TSelf>.EntityInfo => TSelf.Info;

    /// <summary>Writes the primary key attributes, without the surrounding braces.</summary>
    /// <param name="writer">The writer, positioned inside a <c>Key</c> object.</param>
    /// <param name="entity">The entity to take the key from.</param>
    public static abstract void WriteKey(DynamoDbWriter writer, TSelf entity);

    /// <summary>
    /// Gets the version the entity holds, for a delete that takes it from the entity. A static member keeps it free of a box, also for an entity that is a struct. The generator implements this for a
    /// type with a <see cref="VersionAttribute"/>; a type written by hand that names a version attribute in its <see cref="DynamoDbEntityInfo"/> implements it too.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The version.</returns>
    /// <exception cref="NotSupportedException">The type does not implement this.</exception>
    /// <exception cref="OverflowException">The version does not fit a <see cref="long"/>.</exception>
    public static virtual long ReadVersion(TSelf entity) =>
        throw new NotSupportedException($"{typeof(TSelf)} names a version attribute and does not implement ReadVersion.");
}
