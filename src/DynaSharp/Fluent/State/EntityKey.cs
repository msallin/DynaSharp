using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Fluent;

/// <summary>The key of an entity as a key that writes itself, so that a write by the entity goes the way of a write by a generated key.</summary>
/// <typeparam name="T">The entity.</typeparam>
/// <param name="entity">The entity to take the key from.</param>
internal readonly struct EntityKey<T>(T entity) : IKey
    where T : IDynamoDbEntity<T>
{
    /// <inheritdoc/>
    public void WriteKey(DynamoDbWriter writer) => T.WriteKey(writer, entity);
}
